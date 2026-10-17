using System.Net.Http.Headers;
using System.Net.Http.Json;
using Academia.Application.Contracts;
using Academia.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academia.Api.Tests;

/// <summary>
/// Sobe a API em memória contra o SQL Server local (porta 3311), numa base AcademiaTests_&lt;guid&gt;
/// criada pelas migrations no arranque e apagada no fim. Outra instância: variável ACADEMIA_TEST_SQL.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string DefaultServer = "Server=localhost,3311;User Id=sa;Password=Change_this_password_123!;TrustServerCertificate=True;Encrypt=False";

    private readonly string connectionString = NewTestDatabase();

    /// <summary>Connection string para uma base de testes nova (ainda por criar) no SQL Server de testes.</summary>
    public static string NewTestDatabase() => new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ACADEMIA_TEST_SQL") ?? DefaultServer)
    {
        InitialCatalog = $"AcademiaTests_{Guid.NewGuid():N}",
        ConnectTimeout = 5
    }.ConnectionString;

    /// <summary>Os testes criam dezenas de contas a partir do mesmo "IP"; só os testes de rate limiting baixam isto.</summary>
    protected virtual int AuthPermitLimit => 100_000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Fora de Development: sem Lab nem Swagger, e só o appsettings.json versionado.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Academia", connectionString);
        builder.UseSetting("RateLimiting:AuthPermitLimit", AuthPermitLimit.ToString());
        builder.UseSetting("RateLimiting:LearningPermitLimit", "100000");
        // Como em Development: pedidos ilegíveis lançam BadHttpRequestException, para testar o handler que os trata.
        builder.ConfigureTestServices(services => services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true));
    }

    public async Task InitializeAsync()
    {
        // Program.cs regista e engole a falha das migrations; aqui ela tem de parar os testes com uma causa clara.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademiaDbContext>();
        if (!await db.Database.CanConnectAsync() || (await db.Database.GetPendingMigrationsAsync()).Any())
            throw new InvalidOperationException(
                "Base de testes indisponível. Arranca o SQL Server: docker compose -f .\\backend\\docker-compose.yml up -d sqlserver");
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AcademiaDbContext>().Database.EnsureDeletedAsync();
        await DisposeAsync();
    }

    /// <summary>Regista uma conta nova e devolve um cliente já autenticado com o token dela.</summary>
    public async Task<(HttpClient Client, AuthResponse Auth)> CreateUserAsync()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("Utilizador de teste", $"teste-{Guid.NewGuid():N}@exemplo.pt", "password-de-teste-123"));
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth);
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
