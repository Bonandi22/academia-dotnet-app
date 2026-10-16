using Academia.Infrastructure.Lab;
using Microsoft.EntityFrameworkCore;

namespace Academia.Api.Lab;

public static class LabSetup
{
    public static bool LabAtivo(this IConfiguration configuration) => configuration.GetValue<bool>("Lab:Enabled");

    public static IServiceCollection AddLab(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Lab")
            ?? throw new InvalidOperationException("Lab:Enabled = true mas ConnectionStrings:Lab não está configurada.");

        services.AddDbContext<LabDbContext>(options => options.UseSqlServer(connectionString, sql => sql.CommandTimeout(120)));
        services.AddSingleton<LabSeedJob>();
        services.AddHostedService<OutboxPublisher>();
        return services;
    }

    public static async Task InitializeLabAsync(this IServiceProvider services, ILogger logger)
    {
        try
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
            await db.Database.EnsureCreatedAsync();

            // Recovery SIMPLE: com SqlBulkCopy + TableLock a carga fica minimamente registada no log.
            // Só para o laboratório; uma base bancária real está em FULL (precisa de point-in-time restore).
            await db.Database.ExecuteSqlRawAsync("ALTER DATABASE CURRENT SET RECOVERY SIMPLE;");
            logger.LogInformation("[LAB] Base de dados do laboratório pronta. Próximo passo: POST /api/lab/seed");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LAB] Não foi possível inicializar a base AcademiaLab. Verifica ConnectionStrings:Lab e o SQL Server.");
        }
    }
}
