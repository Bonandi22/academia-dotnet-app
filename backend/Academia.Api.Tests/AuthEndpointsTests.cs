using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academia.Application.Contracts;
using Academia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academia.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class AuthEndpointsTests(ApiFactory api)
{
    private static string NewEmail() => $"auth-{Guid.NewGuid():N}@exemplo.pt";

    [Fact]
    public async Task Registo_valido_devolve_201_com_token_e_email_normalizado()
    {
        var email = NewEmail();
        var response = await api.CreateClient().PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("  Ana  ", $"  {email.ToUpperInvariant()} ", "password-de-teste-123"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.False(string.IsNullOrWhiteSpace(auth!.AccessToken));
        Assert.Equal(email, auth.User.Email);
        Assert.Equal("Ana", auth.User.DisplayName);
        Assert.True(auth.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Registo_invalido_devolve_so_os_erros_dos_campos_errados()
    {
        var response = await api.CreateClient().PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("X", NewEmail(), "curta"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var fields = problem.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name).Order();
        Assert.Equal(["displayName", "password"], fields);
    }

    [Fact]
    public async Task Registo_com_email_repetido_devolve_409()
    {
        var client = api.CreateClient();
        var request = new RegisterRequest("Ana", NewEmail(), "password-de-teste-123");
        (await client.PostAsJsonAsync("/api/auth/register", request)).EnsureSuccessStatusCode();

        var duplicate = await client.PostAsJsonAsync("/api/auth/register", request with { Email = request.Email.ToUpperInvariant() });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Login_devolve_401_com_password_errada_e_200_com_a_certa()
    {
        var client = api.CreateClient();
        var email = NewEmail();
        (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Ana", email, "password-de-teste-123"))).EnsureSuccessStatusCode();

        var wrong = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "password-errada-123"));
        var right = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "password-de-teste-123"));

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
    }

    [Fact]
    public async Task Token_de_conta_que_ja_nao_existe_devolve_401_em_vez_de_falhar_na_escrita()
    {
        var (client, auth) = await api.CreateUserAsync();
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcademiaDbContext>();
            await db.Users.Where(u => u.Id == auth.User.Id).ExecuteDeleteAsync();
        }

        var sync = await client.PostAsJsonAsync("/api/learning/sync", new SyncRequest(Lessons: [new("csharp", "async-await", true)]));
        var me = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, sync.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Me_exige_token_e_devolve_o_proprio_utilizador()
    {
        var anonymous = await api.CreateClient().GetAsync("/api/auth/me");
        var (client, auth) = await api.CreateUserAsync();

        var me = await client.GetFromJsonAsync<UserResponse>("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(auth.User.Id, me!.Id);
    }
}
