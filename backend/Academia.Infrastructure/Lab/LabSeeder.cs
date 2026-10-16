using System.Data;
using System.Diagnostics;
using Academia.Domain.Lab;
using Microsoft.Data.SqlClient;

namespace Academia.Infrastructure.Lab;

public sealed record SeedOptions(int Clientes = 10_000, int Contas = 50_000, int Transacoes = 5_000_000, int TamanhoLote = 100_000);

public sealed record SeedResultado(int Clientes, int Contas, long Transacoes, double SegundosTotal, double SegundosTransacoes, double LinhasPorSegundo);

// Carga em volume. Padrão usado em produção para cargas grandes:
//   1. remover índices nonclustered da tabela de destino;
//   2. carregar em lotes com SqlBulkCopy (TableLock + recovery SIMPLE = log mínimo);
//   3. recriar índices e voltar a validar as constraints;
//   4. atualizar estatísticas.
public static class LabSeeder
{
    // Primeiras 500 contas recebem ~30% das transações: distribuição desigual de propósito,
    // para o exercício de parameter sniffing (01-medicoes.sql, secção 6).
    public const int ContasQuentes = 500;

    private static readonly DateTime InicioDados = new(2024, 1, 1);
    private static readonly DateTime FimDados = new(2026, 10, 16);

    private static readonly string[] PrimeirosNomes =
        ["Ana", "João", "Maria", "Pedro", "Inês", "Rui", "Sofia", "Tiago", "Beatriz", "Miguel", "Carla", "Nuno", "Rita", "Luís", "Marta", "Hugo"];
    private static readonly string[] Apelidos =
        ["Silva", "Santos", "Ferreira", "Pereira", "Oliveira", "Costa", "Rodrigues", "Martins", "Sousa", "Fernandes", "Gomes", "Lopes", "Marques", "Alves"];

    public static async Task<SeedResultado> SeedAsync(string connectionString, SeedOptions opcoes, IProgress<long>? progresso, CancellationToken ct)
    {
        var total = Stopwatch.StartNew();
        var random = new Random(42); // semente fixa: dados reprodutíveis entre execuções

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await ExecAsync(conn, LabSql.RemoverIndicesNonClustered, ct);
        await ExecAsync(conn, """
            TRUNCATE TABLE dbo.Transacoes;
            TRUNCATE TABLE dbo.PedidosIdempotentes;
            TRUNCATE TABLE dbo.Outbox;
            DELETE FROM dbo.Contas;
            DELETE FROM dbo.Clientes;
            """, ct);

        await BulkAsync(conn, "dbo.Clientes", GerarClientes(opcoes.Clientes, random), ct);
        await BulkAsync(conn, "dbo.Contas", GerarContas(opcoes.Contas, opcoes.Clientes, random), ct);

        var cronoTransacoes = Stopwatch.StartNew();
        long inseridas = 0;
        var gerador = new GeradorTransacoes(opcoes.Contas, random);
        while (inseridas < opcoes.Transacoes)
        {
            ct.ThrowIfCancellationRequested();
            var quantidade = (int)Math.Min(opcoes.TamanhoLote, opcoes.Transacoes - inseridas);
            var tabela = gerador.ProximoLote(quantidade);
            await BulkAsync(conn, "dbo.Transacoes", tabela, ct);
            inseridas += quantidade;
            progresso?.Report(inseridas);
        }
        cronoTransacoes.Stop();

        // O SqlBulkCopy, por omissão, NÃO valida FKs: a constraint fica marcada como "not trusted"
        // e o otimizador deixa de confiar nela. WITH CHECK volta a validar e a torná-la confiável.
        await ExecAsync(conn, "ALTER TABLE dbo.Contas WITH CHECK CHECK CONSTRAINT ALL;", ct);
        await ExecAsync(conn, "ALTER TABLE dbo.Transacoes WITH CHECK CHECK CONSTRAINT ALL;", ct);
        await ExecAsync(conn, "UPDATE STATISTICS dbo.Transacoes WITH FULLSCAN;", ct);

        total.Stop();
        return new SeedResultado(opcoes.Clientes, opcoes.Contas, inseridas, total.Elapsed.TotalSeconds,
            cronoTransacoes.Elapsed.TotalSeconds, inseridas / Math.Max(cronoTransacoes.Elapsed.TotalSeconds, 0.001));
    }

    // Usado pelo benchmark de inserção: mesmas regras de geração, sem truncar nada.
    public static async Task BulkInsertTransacoesAsync(string connectionString, int quantidade, int totalContas, string descricao, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        var tabela = new GeradorTransacoes(totalContas, new Random()).ProximoLote(quantidade, descricao);
        await BulkAsync(conn, "dbo.Transacoes", tabela, ct, tableLock: false);
    }

    public static IEnumerable<Transacao> GerarTransacoesEntidades(int quantidade, int totalContas, string descricao)
    {
        var tabela = new GeradorTransacoes(totalContas, new Random()).ProximoLote(quantidade, descricao);
        foreach (DataRow r in tabela.Rows)
        {
            yield return new Transacao
            {
                ContaId = (int)r["ContaId"],
                DataMovimento = (DateTime)r["DataMovimento"],
                Valor = (decimal)r["Valor"],
                Tipo = (string)r["Tipo"],
                Descricao = descricao
            };
        }
    }

    private static async Task BulkAsync(SqlConnection conn, string tabelaDestino, DataTable dados, CancellationToken ct, bool tableLock = true)
    {
        var opcoes = tableLock ? SqlBulkCopyOptions.TableLock : SqlBulkCopyOptions.Default;
        using var bulk = new SqlBulkCopy(conn, opcoes, externalTransaction: null)
        {
            DestinationTableName = tabelaDestino,
            BatchSize = dados.Rows.Count,
            BulkCopyTimeout = 0
        };
        // Mapeamento explícito por nome: a coluna IDENTITY (Transacoes.Id) fica de fora e é gerada pelo SQL Server.
        foreach (DataColumn coluna in dados.Columns)
            bulk.ColumnMappings.Add(coluna.ColumnName, coluna.ColumnName);
        await bulk.WriteToServerAsync(dados, ct);
    }

    private static async Task ExecAsync(SqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 0 };
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static DataTable GerarClientes(int quantidade, Random random)
    {
        var t = new DataTable();
        t.Columns.Add("Id", typeof(int));
        t.Columns.Add("Nome", typeof(string));
        t.Columns.Add("Pais", typeof(string));
        for (var i = 1; i <= quantidade; i++)
        {
            var nome = $"{PrimeirosNomes[random.Next(PrimeirosNomes.Length)]} {Apelidos[random.Next(Apelidos.Length)]} {Apelidos[random.Next(Apelidos.Length)]}";
            var sorteio = random.NextDouble();
            var pais = sorteio < 0.80 ? "PT" : sorteio < 0.90 ? "ES" : sorteio < 0.96 ? "FR" : "BR";
            t.Rows.Add(i, nome, pais);
        }
        return t;
    }

    private static DataTable GerarContas(int quantidade, int totalClientes, Random random)
    {
        var t = new DataTable();
        t.Columns.Add("Id", typeof(int));
        t.Columns.Add("Numero", typeof(string));
        t.Columns.Add("ClienteId", typeof(int));
        t.Columns.Add("DataAbertura", typeof(DateTime));
        var inicio = new DateTime(2015, 1, 1);
        for (var i = 1; i <= quantidade; i++)
            t.Rows.Add(i, $"PT50{i:D12}", random.Next(1, totalClientes + 1), inicio.AddDays(random.Next(0, 3650)));
        return t;
    }

    private sealed class GeradorTransacoes(int totalContas, Random random)
    {
        private readonly long _intervaloMs = (long)(FimDados - InicioDados).TotalMilliseconds;
        private (int Conta, DateTime Data, decimal Valor, string Tipo)? _ultima;

        public DataTable ProximoLote(int quantidade, string? descricaoFixa = null)
        {
            var t = new DataTable();
            t.Columns.Add("ContaId", typeof(int));
            t.Columns.Add("DataMovimento", typeof(DateTime));
            t.Columns.Add("Valor", typeof(decimal));
            t.Columns.Add("Tipo", typeof(string));
            t.Columns.Add("Descricao", typeof(string));

            for (var i = 0; i < quantidade; i++)
            {
                // ~0,1% de duplicados exatos (mesma conta, data, valor e tipo) para o exercício 7.
                if (descricaoFixa is null && _ultima is { } dup && random.NextDouble() < 0.001)
                {
                    t.Rows.Add(dup.Conta, dup.Data, dup.Valor, dup.Tipo, DBNull.Value);
                    continue;
                }

                var conta = random.NextDouble() < 0.30
                    ? random.Next(1, Math.Min(ContasQuentes, totalContas) + 1)
                    : random.Next(1, totalContas + 1);
                var data = InicioDados.AddMilliseconds(random.NextInt64(0, _intervaloMs));

                var sorteio = random.NextDouble();
                var tipo = sorteio < 0.45 ? TiposTransacao.Debito
                         : sorteio < 0.75 ? TiposTransacao.Credito
                         : sorteio < 0.90 ? TiposTransacao.Pagamento
                         : TiposTransacao.Transferencia;

                var valor = tipo == TiposTransacao.Credito
                    ? Math.Round((decimal)(10 + random.NextDouble() * 4990), 2)
                    : -Math.Round((decimal)(1 + random.NextDouble() * 1999), 2);

                object descricao = descricaoFixa is not null ? descricaoFixa
                    : random.NextDouble() < 0.2 ? $"{tipo} ref {random.Next(100000, 999999)}"
                    : DBNull.Value;

                t.Rows.Add(conta, data, valor, tipo, descricao);
                _ultima = (conta, data, valor, tipo);
            }
            return t;
        }
    }
}
