using System.Net;
using System.Net.Http.Json;
using System.Text;
using Academia.Api.Learning;
using Academia.Application.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Academia.Api.Tests;

/// <summary>Fábrica própria com limite de auth baixo; tem base de dados própria para não afetar os outros testes.</summary>
public sealed class StrictRateLimitFactory : ApiFactory
{
    public const int Limit = 3;
    protected override int AuthPermitLimit => Limit;
}

public sealed class AuthRateLimitTests(StrictRateLimitFactory api) : IClassFixture<StrictRateLimitFactory>
{
    [Fact]
    public async Task Login_acima_do_limite_por_minuto_devolve_429()
    {
        var client = api.CreateClient();
        var attempts = new List<HttpStatusCode>();

        for (var i = 0; i <= StrictRateLimitFactory.Limit; i++)
            attempts.Add((await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("ninguem@exemplo.pt", "password-errada-123"))).StatusCode);
        var blocked = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("ninguem@exemplo.pt", "password-errada-123"));

        Assert.All(attempts.Take(StrictRateLimitFactory.Limit), status => Assert.Equal(HttpStatusCode.Unauthorized, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Contains("Demasiados pedidos", await blocked.Content.ReadAsStringAsync());
    }
}

[Collection(ApiCollection.Name)]
public sealed class SecurityTests(ApiFactory api)
{
    [Fact]
    public async Task Json_malformado_devolve_400_com_problem_details_e_nao_500()
    {
        var response = await api.CreateClient().PostAsync("/api/auth/login",
            new StringContent("{ \"email\": ", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Pedido inválido", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Revisoes_acima_do_limite_por_conta_sao_rejeitadas_mas_trocar_uma_por_outra_e_permitido()
    {
        var (client, _) = await api.CreateUserAsync();
        ReviewRequest Review(int i) => new($"quiz:limite-{i}", "Pergunta", "csharp", "C#", "quiz", "Errada.");
        var full = new SyncRequest(Reviews: Enumerable.Range(0, LearningStore.MaxReviewsPerUser).Select(Review).ToList());

        var fill = await client.PostAsJsonAsync("/api/learning/sync", full);
        var overflow = await client.PutAsJsonAsync("/api/learning/reviews", Review(LearningStore.MaxReviewsPerUser));
        var swap = await client.PostAsJsonAsync("/api/learning/sync",
            new SyncRequest(DeletedReviewKeys: ["quiz:limite-0"], Reviews: [Review(LearningStore.MaxReviewsPerUser)]));
        var update = await client.PutAsJsonAsync("/api/learning/reviews", Review(1) with { IsResolved = true });

        Assert.Equal(HttpStatusCode.OK, fill.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, overflow.StatusCode);
        Assert.Contains("Limite de", await overflow.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, swap.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode); // atualizar um item existente não conta para o limite
    }

    [Fact]
    public void Api_recusa_arrancar_com_o_lab_ligado_fora_de_development()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Lab:Enabled", "true");
        });

        var error = Assert.ThrowsAny<Exception>(() => factory.Services);

        Assert.Contains("Lab:Enabled só é permitido em Development", error.ToString());
    }
}
