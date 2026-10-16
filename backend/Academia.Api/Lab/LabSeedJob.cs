using Academia.Infrastructure.Lab;
using Microsoft.EntityFrameworkCore;

namespace Academia.Api.Lab;

// A carga de milhões de linhas demora minutos: não pode prender um pedido HTTP.
// O endpoint responde 202 Accepted e a carga corre em background; o estado consulta-se por GET.
// É o mesmo padrão que deves descrever para exportações e processamentos pesados.
public sealed class LabSeedJob(IServiceScopeFactory scopes, IHostApplicationLifetime lifetime, ILogger<LabSeedJob> logger)
{
    private readonly object _lock = new();
    private EstadoSeed _estado = new("parado", 0, 0, null, null, null, null);

    public EstadoSeed Estado
    {
        get { lock (_lock) return _estado; }
    }

    public bool TryStart(SeedOptions opcoes)
    {
        lock (_lock)
        {
            if (_estado.Situacao == "a correr") return false;
            _estado = new EstadoSeed("a correr", 0, opcoes.Transacoes, DateTime.UtcNow, null, null, null);
        }
        _ = Task.Run(() => ExecutarAsync(opcoes, lifetime.ApplicationStopping));
        return true;
    }

    private async Task ExecutarAsync(SeedOptions opcoes, CancellationToken ct)
    {
        try
        {
            string connectionString;
            using (var scope = scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
                connectionString = db.Database.GetConnectionString()
                    ?? throw new InvalidOperationException("ConnectionStrings:Lab não configurada.");
            }

            logger.LogInformation("[LAB] Seed iniciado: {Transacoes:N0} transações em lotes de {Lote:N0}", opcoes.Transacoes, opcoes.TamanhoLote);
            var progresso = new ProgressoSincrono(n =>
            {
                lock (_lock) _estado = _estado with { Inseridas = n };
                logger.LogInformation("[LAB] {Inseridas:N0} / {Total:N0} transações", n, opcoes.Transacoes);
            });

            var resultado = await LabSeeder.SeedAsync(connectionString, opcoes, progresso, ct);
            lock (_lock) _estado = _estado with { Situacao = "concluido", TerminadoEm = DateTime.UtcNow, Resultado = resultado };
            logger.LogInformation("[LAB] Seed concluído em {Segundos:N1}s ({LinhasPorSegundo:N0} linhas/s)", resultado.SegundosTotal, resultado.LinhasPorSegundo);
        }
        catch (Exception ex)
        {
            lock (_lock) _estado = _estado with { Situacao = "erro", TerminadoEm = DateTime.UtcNow, Erro = ex.Message };
            logger.LogError(ex, "[LAB] Seed falhou");
        }
    }

    // Progress<T> envia o callback para o thread pool e pode reordenar relatórios; aqui queremos execução imediata.
    private sealed class ProgressoSincrono(Action<long> acao) : IProgress<long>
    {
        public void Report(long value) => acao(value);
    }
}
