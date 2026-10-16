using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academia.Domain.Lab;
using Academia.Infrastructure.Lab;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Academia.Api.Lab;

public static class LabEndpoints
{
    public static IEndpointRouteBuilder MapLabEndpoints(this IEndpointRouteBuilder app)
    {
        // Sem autenticação de propósito: o laboratório só é mapeado com Lab:Enabled = true (Development).
        var lab = app.MapGroup("/api/lab").WithTags("Laboratório de volume").AllowAnonymous();

        lab.MapPost("/seed", IniciarSeed);
        lab.MapGet("/seed/estado", (LabSeedJob job) => Results.Ok(job.Estado));
        lab.MapGet("/estatisticas", Estatisticas);
        lab.MapPost("/indices/{cenario}", AplicarCenarioIndices);

        lab.MapGet("/transacoes/offset", PaginarOffset);
        lab.MapGet("/transacoes/keyset", PaginarKeyset);
        lab.MapGet("/transacoes/{id:long}", ObterTransacao);
        lab.MapPost("/transacoes", CriarTransacao);

        lab.MapGet("/relatorios/mensal", RelatorioMensal);
        lab.MapGet("/relatorios/top-contas", TopContas);
        lab.MapGet("/exportar", ExportarCsv);
        lab.MapGet("/outbox", UltimasMensagensOutbox);

        lab.MapPost("/benchmark/insercao", BenchmarkInsercao);
        return app;
    }

    // ---------------------------------------------------------------- carga e índices

    private static IResult IniciarSeed(LabSeedJob job, int? transacoes, int? contas, int? clientes)
    {
        var opcoes = new SeedOptions(clientes ?? 10_000, contas ?? 50_000, transacoes ?? 5_000_000);
        if (opcoes.Transacoes is < 1_000 or > 20_000_000 || opcoes.Contas is < 100 or > 1_000_000 || opcoes.Clientes < 10 || opcoes.Clientes > opcoes.Contas)
            return Results.BadRequest(new { mensagem = "Limites: transacoes 1 000–20 000 000, contas 100–1 000 000, clientes 10–contas." });

        return job.TryStart(opcoes)
            ? Results.Accepted("/api/lab/seed/estado", job.Estado)
            : Results.Conflict(new { mensagem = "Já existe uma carga em curso.", estado = job.Estado });
    }

    private static async Task<IResult> AplicarCenarioIndices(string cenario, LabDbContext db, CancellationToken ct)
    {
        if (!LabSql.Cenarios.TryGetValue(cenario, out var definicao))
            return Results.BadRequest(new { mensagem = "Cenário desconhecido.", cenarios = LabSql.Cenarios.Keys });

        // Criar índices sobre milhões de linhas demora: aumenta o timeout só para este comando.
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(15));
        var cronometro = Stopwatch.StartNew();
        await db.Database.ExecuteSqlRawAsync(LabSql.RemoverIndicesNonClustered, ct);
        if (definicao.Sql.Length > 0)
            await db.Database.ExecuteSqlRawAsync(definicao.Sql, ct);
        cronometro.Stop();

        return Results.Ok(new
        {
            cenario,
            definicao.Descricao,
            segundos = Math.Round(cronometro.Elapsed.TotalSeconds, 1),
            indices = await ListarIndices(db, ct)
        });
    }

    private static async Task<IResult> Estatisticas(LabDbContext db, LabSeedJob job, CancellationToken ct)
    {
        var tabelas = await db.Database.SqlQueryRaw<LinhaContagemTabela>("""
            SELECT t.name AS Tabela, CAST(SUM(p.rows) AS BIGINT) AS Linhas
            FROM sys.tables AS t
            JOIN sys.partitions AS p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
            GROUP BY t.name
            ORDER BY t.name
            """).ToListAsync(ct);

        return Results.Ok(new { tabelas, indicesTransacoes = await ListarIndices(db, ct), seed = job.Estado });
    }

    private static Task<List<LinhaIndice>> ListarIndices(LabDbContext db, CancellationToken ct) =>
        db.Database.SqlQueryRaw<LinhaIndice>("""
            SELECT i.name AS Indice,
                   i.type_desc AS Tipo,
                   CAST(SUM(ps.used_page_count) * 8 / 1024.0 AS DECIMAL(18, 1)) AS TamanhoMb
            FROM sys.indexes AS i
            JOIN sys.dm_db_partition_stats AS ps ON ps.object_id = i.object_id AND ps.index_id = i.index_id
            WHERE i.object_id = OBJECT_ID(N'dbo.Transacoes')
            GROUP BY i.name, i.type_desc
            ORDER BY i.type_desc, i.name
            """).ToListAsync(ct);

    // ---------------------------------------------------------------- paginação

    // OFFSET/FETCH: para chegar à página N, o SQL Server lê e descarta (N-1) * tamanho linhas.
    private static async Task<IResult> PaginarOffset(LabDbContext db, int? contaId, int? pagina, int? tamanho, bool? incluirDescricao, CancellationToken ct)
    {
        var p = pagina ?? 1;
        var t = tamanho ?? 50;
        if (p is < 1 or > 1_000_000 || t is < 1 or > 500)
            return Results.BadRequest(new { mensagem = "pagina 1–1 000 000; tamanho 1–500." });

        var consulta = Projetar(
            Filtrar(db, contaId)
                .OrderByDescending(x => x.DataMovimento).ThenByDescending(x => x.Id)
                .Skip((p - 1) * t).Take(t),
            incluirDescricao ?? false);

        return await Executar("offset", consulta, ct);
    }

    // Keyset (seek): continua a partir da última linha vista. Com um índice na ordem da navegação,
    // a página 1 e a página "50 000" custam o mesmo. Não permite saltar para uma página arbitrária.
    private static async Task<IResult> PaginarKeyset(LabDbContext db, int? contaId, DateTime? depoisData, long? depoisId, int? tamanho, bool? incluirDescricao, CancellationToken ct)
    {
        var t = tamanho ?? 50;
        if (t is < 1 or > 500) return Results.BadRequest(new { mensagem = "tamanho 1–500." });
        if (depoisData.HasValue != depoisId.HasValue)
            return Results.BadRequest(new { mensagem = "Indica depoisData e depoisId juntos (vêm do proximoCursor da página anterior)." });

        var consulta = Filtrar(db, contaId);
        if (depoisData is { } data && depoisId is { } id)
            consulta = consulta.Where(x => x.DataMovimento < data || (x.DataMovimento == data && x.Id < id));

        var pagina = Projetar(
            consulta.OrderByDescending(x => x.DataMovimento).ThenByDescending(x => x.Id).Take(t),
            incluirDescricao ?? false);

        return await Executar("keyset", pagina, ct);
    }

    private static IQueryable<Transacao> Filtrar(LabDbContext db, int? contaId)
    {
        var consulta = db.Transacoes.AsNoTracking();
        return contaId is { } id ? consulta.Where(x => x.ContaId == id) : consulta;
    }

    // Duas projeções separadas de propósito. Sem Descricao, os índices de cobertura bastam.
    // Com Descricao (coluna fora do índice), aparece um Key Lookup por linha no plano de execução.
    // Um "incluir ? t.Descricao : null" numa só projeção geraria CASE WHEN e leria a coluna na mesma.
    private static IQueryable<TransacaoDto> Projetar(IQueryable<Transacao> consulta, bool incluirDescricao) =>
        incluirDescricao
            ? consulta.Select(x => new TransacaoDto(x.Id, x.ContaId, x.DataMovimento, x.Valor, x.Tipo, x.Descricao))
            : consulta.Select(x => new TransacaoDto(x.Id, x.ContaId, x.DataMovimento, x.Valor, x.Tipo, null));

    private static async Task<IResult> Executar(string estrategia, IQueryable<TransacaoDto> consulta, CancellationToken ct)
    {
        var sql = consulta.ToQueryString();
        var cronometro = Stopwatch.StartNew();
        var itens = await consulta.ToListAsync(ct);
        cronometro.Stop();

        var ultimo = itens.LastOrDefault();
        var cursor = ultimo is null ? null : new Cursor(ultimo.DataMovimento, ultimo.Id);
        return Results.Ok(new PaginaResposta(estrategia, Math.Round(cronometro.Elapsed.TotalMilliseconds, 2), itens.Count, itens, cursor, sql));
    }

    // ---------------------------------------------------------------- escrita idempotente + outbox

    private static async Task<IResult> ObterTransacao(long id, LabDbContext db, CancellationToken ct)
    {
        var transacao = await db.Transacoes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return transacao is null ? Results.NotFound() : Results.Ok(ParaDto(transacao));
    }

    private static async Task<IResult> CriarTransacao(
        CriarTransacaoRequest pedido,
        [FromHeader(Name = "Idempotency-Key")] string? chave,
        HttpContext http,
        LabDbContext db,
        CancellationToken ct)
    {
        // 1. Validação de formato.
        if (string.IsNullOrWhiteSpace(chave) || chave.Length > 100)
            return Results.BadRequest(new { mensagem = "Header Idempotency-Key obrigatório (até 100 caracteres)." });
        if (pedido.Valor == 0 || Math.Abs(pedido.Valor) > 1_000_000 || !TiposTransacao.Todos.Contains(pedido.Tipo) || (pedido.Descricao?.Length ?? 0) > 200)
            return Results.BadRequest(new { mensagem = "Valor diferente de zero e até 1 000 000; tipo em " + string.Join(", ", TiposTransacao.Todos) + "." });

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{pedido.ContaId}|{pedido.Valor}|{pedido.Tipo}|{pedido.Descricao}"))));

        // 2. Caminho rápido: chave já usada → devolve o resultado original, não cria nada.
        var existente = await db.PedidosIdempotentes.AsNoTracking().SingleOrDefaultAsync(x => x.Chave == chave, ct);
        if (existente is not null) return await Repetir(existente, hash, http, db, ct);

        // 3. Regra de negócio.
        if (!await db.Contas.AnyAsync(x => x.Id == pedido.ContaId, ct))
            return Results.NotFound(new { mensagem = $"Conta {pedido.ContaId} não existe." });

        // 4. Transação curta: dado de negócio + registo de idempotência + evento no outbox, tudo ou nada.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var transacao = new Transacao
            {
                ContaId = pedido.ContaId,
                DataMovimento = DateTime.UtcNow,
                Valor = pedido.Valor,
                Tipo = pedido.Tipo,
                Descricao = pedido.Descricao
            };
            db.Transacoes.Add(transacao);
            await db.SaveChangesAsync(ct); // obtém o Id gerado pelo IDENTITY

            db.PedidosIdempotentes.Add(new PedidoIdempotente { Chave = chave, TransacaoId = transacao.Id, HashPedido = hash, CriadoEm = DateTime.UtcNow });
            db.Outbox.Add(new OutboxMessage
            {
                Tipo = "TransacaoCriada",
                Payload = JsonSerializer.Serialize(new { transacao.Id, transacao.ContaId, transacao.Valor, transacao.Tipo, transacao.DataMovimento }),
                CriadoEm = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Results.Created($"/api/lab/transacoes/{transacao.Id}", ParaDto(transacao));
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 })
        {
            // Corrida: dois pedidos com a mesma chave ao mesmo tempo. A PK de PedidosIdempotentes
            // garante que só um vence; o perdedor faz rollback (a transação dele desaparece) e devolve a do vencedor.
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var vencedor = await db.PedidosIdempotentes.AsNoTracking().SingleAsync(x => x.Chave == chave, ct);
            return await Repetir(vencedor, hash, http, db, ct);
        }
    }

    private static async Task<IResult> Repetir(PedidoIdempotente pedido, string hash, HttpContext http, LabDbContext db, CancellationToken ct)
    {
        if (!string.Equals(pedido.HashPedido, hash, StringComparison.Ordinal))
            return Results.UnprocessableEntity(new { mensagem = "Esta Idempotency-Key já foi usada com um pedido diferente." });

        var original = await db.Transacoes.AsNoTracking().SingleAsync(x => x.Id == pedido.TransacaoId, ct);
        http.Response.Headers["Idempotent-Replayed"] = "true";
        return Results.Ok(ParaDto(original));
    }

    private static async Task<IResult> UltimasMensagensOutbox(LabDbContext db, int? top, CancellationToken ct)
    {
        var mensagens = await db.Outbox.AsNoTracking().OrderByDescending(x => x.Id).Take(Math.Clamp(top ?? 20, 1, 200)).ToListAsync(ct);
        var pendentes = await db.Outbox.CountAsync(x => x.ProcessadoEm == null, ct);
        return Results.Ok(new { pendentes, mensagens });
    }

    private static TransacaoDto ParaDto(Transacao x) => new(x.Id, x.ContaId, x.DataMovimento, x.Valor, x.Tipo, x.Descricao);

    // ---------------------------------------------------------------- relatórios

    // sargable=true  → WHERE DataMovimento >= @inicio AND DataMovimento < @fim  (permite seek num índice por data)
    // sargable=false → WHERE YEAR(DataMovimento) = @ano                         (função na coluna: scan)
    private static async Task<IResult> RelatorioMensal(LabDbContext db, int? ano, bool? sargable, CancellationToken ct)
    {
        var a = ano ?? 2026;
        if (a is < 2000 or > 2100) return Results.BadRequest(new { mensagem = "Ano inválido." });
        var usarSargable = sargable ?? true;
        var inicio = new DateTime(a, 1, 1);

        var filtro = usarSargable
            ? "t.DataMovimento >= @inicio AND t.DataMovimento < @fim"
            : "YEAR(t.DataMovimento) = @ano";
        var sql = $"""
            SELECT YEAR(t.DataMovimento) AS Ano,
                   MONTH(t.DataMovimento) AS Mes,
                   t.Tipo AS Tipo,
                   COUNT_BIG(*) AS Quantidade,
                   CAST(SUM(t.Valor) AS DECIMAL(18, 2)) AS Total
            FROM dbo.Transacoes AS t
            WHERE {filtro}
            GROUP BY YEAR(t.DataMovimento), MONTH(t.DataMovimento), t.Tipo
            ORDER BY Ano, Mes, Tipo
            """;

        object[] parametros = usarSargable
            ? [Parametro("@inicio", SqlDbType.DateTime2, inicio), Parametro("@fim", SqlDbType.DateTime2, inicio.AddYears(1))]
            : [Parametro("@ano", SqlDbType.Int, a)];

        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
        var cronometro = Stopwatch.StartNew();
        var linhas = await db.Database.SqlQueryRaw<LinhaRelatorioMensal>(sql, parametros).ToListAsync(ct);
        cronometro.Stop();

        return Results.Ok(new { sargable = usarSargable, tempoMs = Math.Round(cronometro.Elapsed.TotalMilliseconds, 2), sql, linhas });
    }

    // CTE + agregação + window function (RANK) + joins.
    private static async Task<IResult> TopContas(LabDbContext db, int? ano, int? top, CancellationToken ct)
    {
        var a = ano ?? 2026;
        var n = Math.Clamp(top ?? 10, 1, 100);
        var inicio = new DateTime(a, 1, 1);
        const string sql = """
            WITH Totais AS (
                SELECT t.ContaId,
                       SUM(t.Valor) AS Saldo,
                       COUNT_BIG(*) AS NumTransacoes
                FROM dbo.Transacoes AS t
                WHERE t.DataMovimento >= @inicio AND t.DataMovimento < @fim
                GROUP BY t.ContaId
            )
            SELECT TOP (@top)
                   RANK() OVER (ORDER BY x.Saldo DESC) AS Posicao,
                   c.Numero AS Numero,
                   cl.Nome AS Cliente,
                   CAST(x.Saldo AS DECIMAL(18, 2)) AS Saldo,
                   x.NumTransacoes AS NumTransacoes
            FROM Totais AS x
            JOIN dbo.Contas AS c ON c.Id = x.ContaId
            JOIN dbo.Clientes AS cl ON cl.Id = c.ClienteId
            ORDER BY x.Saldo DESC
            """;

        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
        var cronometro = Stopwatch.StartNew();
        var linhas = await db.Database.SqlQueryRaw<LinhaTopConta>(sql,
            Parametro("@inicio", SqlDbType.DateTime2, inicio),
            Parametro("@fim", SqlDbType.DateTime2, inicio.AddYears(1)),
            Parametro("@top", SqlDbType.Int, n)).ToListAsync(ct);
        cronometro.Stop();

        return Results.Ok(new { tempoMs = Math.Round(cronometro.Elapsed.TotalMilliseconds, 2), linhas });
    }

    private static SqlParameter Parametro(string nome, SqlDbType tipo, object valor)
    {
        var parametro = new SqlParameter(nome, tipo) { Value = valor };
        if (tipo == SqlDbType.DateTime2) parametro.Scale = 3; // igual à coluna datetime2(3)
        return parametro;
    }

    // ---------------------------------------------------------------- exportação em streaming

    // Exporta milhões de linhas com memória constante: lê com AsAsyncEnumerable (um DataReader por baixo)
    // e escreve cada linha diretamente na resposta. Nunca ToList() de tudo.
    private static async Task ExportarCsv(HttpContext http, LabDbContext db, ILoggerFactory loggers, int? ano, int? contaId, CancellationToken ct)
    {
        var a = ano ?? 2026;
        if (a is < 2000 or > 2100)
        {
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            await http.Response.WriteAsync("Ano inválido.", ct);
            return;
        }

        var inicio = new DateTime(a, 1, 1);
        var fim = inicio.AddYears(1);
        var consulta = Filtrar(db, contaId)
            .Where(x => x.DataMovimento >= inicio && x.DataMovimento < fim)
            .OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.ContaId, x.DataMovimento, x.Valor, x.Tipo });

        http.Response.ContentType = "text/csv; charset=utf-8";
        http.Response.Headers.ContentDisposition = $"attachment; filename=transacoes-{a}{(contaId is null ? "" : $"-conta{contaId}")}.csv";

        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(30));
        var cronometro = Stopwatch.StartNew();
        long linhas = 0;
        await using (var escritor = new StreamWriter(http.Response.Body, new UTF8Encoding(false), bufferSize: 64 * 1024))
        {
            await escritor.WriteLineAsync("Id;ContaId;DataMovimento;Valor;Tipo");
            await foreach (var x in consulta.AsAsyncEnumerable().WithCancellation(ct))
            {
                await escritor.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                    $"{x.Id};{x.ContaId};{x.DataMovimento:yyyy-MM-dd HH:mm:ss.fff};{x.Valor};{x.Tipo}"));
                linhas++;
            }
        }
        cronometro.Stop();

        loggers.CreateLogger("Lab.Exportacao").LogInformation(
            "[LAB] Exportadas {Linhas:N0} linhas em {Segundos:N1}s; memória gerida do processo: {Mb:N0} MB",
            linhas, cronometro.Elapsed.TotalSeconds, GC.GetTotalMemory(false) / 1024 / 1024);
    }

    // ---------------------------------------------------------------- benchmark de escrita

    private static async Task<IResult> BenchmarkInsercao(LabDbContext db, int? linhas, int? linhasLinhaALinha, CancellationToken ct)
    {
        var n = linhas ?? 20_000;
        var nLinhaALinha = linhasLinhaALinha ?? 1_000;
        if (n is < 100 or > 200_000 || nLinhaALinha is < 10 or > 10_000)
            return Results.BadRequest(new { mensagem = "linhas 100–200 000; linhasLinhaALinha 10–10 000." });

        var totalContas = await db.Contas.CountAsync(ct);
        if (totalContas == 0) return Results.BadRequest(new { mensagem = "Corre primeiro POST /api/lab/seed." });

        var connectionString = db.Database.GetConnectionString()!;
        var idAntes = await db.Transacoes.MaxAsync(x => (long?)x.Id, ct) ?? 0;
        var resultados = new List<ResultadoBenchmark>();
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

        // A) Anti-padrão: um SaveChanges (um round-trip e uma transação) por linha.
        var cronometro = Stopwatch.StartNew();
        foreach (var transacao in LabSeeder.GerarTransacoesEntidades(nLinhaALinha, totalContas, "BENCH"))
        {
            db.Transacoes.Add(transacao);
            await db.SaveChangesAsync(ct);
        }
        resultados.Add(Medir("SaveChanges por linha", nLinhaALinha, cronometro));
        db.ChangeTracker.Clear();

        // B) AddRange + um SaveChanges: o EF Core agrupa os INSERT em batches (MERGE ... OUTPUT).
        cronometro.Restart();
        db.Transacoes.AddRange(LabSeeder.GerarTransacoesEntidades(n, totalContas, "BENCH"));
        await db.SaveChangesAsync(ct);
        resultados.Add(Medir("AddRange + SaveChanges", n, cronometro));
        db.ChangeTracker.Clear();

        // C) SqlBulkCopy: protocolo de carga em massa do SQL Server.
        cronometro.Restart();
        await LabSeeder.BulkInsertTransacoesAsync(connectionString, n, totalContas, "BENCH", ct);
        resultados.Add(Medir("SqlBulkCopy", n, cronometro));

        // Limpeza com ExecuteDelete: um só DELETE com seek no clustered index (Id > idAntes), sem carregar entidades.
        var removidas = await db.Transacoes.Where(x => x.Id > idAntes).ExecuteDeleteAsync(ct);

        return Results.Ok(new { resultados, linhasRemovidasNaLimpeza = removidas });
    }

    private static ResultadoBenchmark Medir(string metodo, int linhas, Stopwatch cronometro)
    {
        cronometro.Stop();
        var segundos = cronometro.Elapsed.TotalSeconds;
        return new ResultadoBenchmark(metodo, linhas, Math.Round(segundos, 3), Math.Round(linhas / Math.Max(segundos, 0.001)));
    }
}
