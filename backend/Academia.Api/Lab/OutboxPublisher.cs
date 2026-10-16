using Academia.Infrastructure.Lab;
using Microsoft.EntityFrameworkCore;

namespace Academia.Api.Lab;

// Worker do outbox pattern. Lê mensagens pendentes por ordem, "publica-as" e marca-as como processadas.
//
// Garantia: AT-LEAST-ONCE. Se o processo cair depois de publicar e antes de marcar, a mensagem
// volta a ser publicada no arranque seguinte. Por isso o CONSUMIDOR tem de ser idempotente.
// Num sistema real, o LogInformation abaixo seria um IProducer<TKey,TValue>.ProduceAsync do Confluent.Kafka,
// com a chave = ContaId para garantir ordem por conta dentro da partição.
public sealed class OutboxPublisher(IServiceScopeFactory scopes, ILogger<OutboxPublisher> logger) : BackgroundService
{
    private const int TamanhoLote = 100;
    private bool _emFalha;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await PublicarLoteAsync(stoppingToken);
                    _emFalha = false;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Regista só a primeira falha de uma sequência (ex.: SQL Server ainda a arrancar).
                    if (!_emFalha) logger.LogWarning(ex, "[OUTBOX] Falha ao publicar; nova tentativa a cada 2s.");
                    _emFalha = true;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Encerramento normal da aplicação.
        }
    }

    private async Task PublicarLoteAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();

        // Usa o índice filtrado IX_Outbox_Pendentes (WHERE ProcessadoEm IS NULL).
        var lote = await db.Outbox.AsNoTracking()
            .Where(m => m.ProcessadoEm == null)
            .OrderBy(m => m.Id)
            .Take(TamanhoLote)
            .ToListAsync(ct);

        if (lote.Count == 0) return;

        foreach (var mensagem in lote)
            logger.LogInformation("[OUTBOX] Publicado {Tipo} #{Id}: {Payload}", mensagem.Tipo, mensagem.Id, mensagem.Payload);

        var ids = lote.Select(m => m.Id).ToList();
        DateTime? agora = DateTime.UtcNow;
        await db.Outbox
            .Where(m => ids.Contains(m.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessadoEm, agora), ct);
    }
}
