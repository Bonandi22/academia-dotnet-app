using System.Net;
using System.Net.Http.Json;
using Academia.Application.Contracts;
using Academia.Domain.Entities;
using Academia.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academia.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class ServerSessionTests(ApiFactory api)
{
    private const string Password = "password-de-teste-123";

    private static string? SessionCookieValue(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.Select(c => c.Split(';')[0]).FirstOrDefault(c => c.StartsWith("academia_session=") && c.Length > "academia_session=".Length)
            : null;

    private async Task<HttpStatusCode> MeWithCopiedCookieAsync(string cookie)
    {
        var attacker = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("Cookie", cookie);
        return (await attacker.SendAsync(request)).StatusCode;
    }

    private async Task<T> WithDbAsync<T>(Func<AcademiaDbContext, Task<T>> action)
    {
        using var scope = api.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AcademiaDbContext>());
    }

    private Task<UserSession> SessionOfAsync(Guid userId) =>
        WithDbAsync(db => db.Sessions.AsNoTracking().SingleAsync(s => s.UserId == userId && s.RevokedAt == null));

    [Fact]
    public async Task Cookie_copiado_deixa_de_valer_depois_do_logout()
    {
        var client = api.CreateClient();
        var register = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Ana", $"copia-{Guid.NewGuid():N}@exemplo.pt", Password));
        var stolen = SessionCookieValue(register)!;

        var beforeLogout = await MeWithCopiedCookieAsync(stolen);
        await client.PostAsync("/api/auth/logout", null);
        var afterLogout = await MeWithCopiedCookieAsync(stolen);

        Assert.Equal(HttpStatusCode.OK, beforeLogout);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout); // o JWT ainda não expirou, mas a sessão foi revogada
    }

    [Fact]
    public async Task Logout_em_todos_os_dispositivos_termina_as_outras_sessoes()
    {
        var email = $"todos-{Guid.NewGuid():N}@exemplo.pt";
        var laptop = api.CreateClient();
        var phone = api.CreateClient();
        await laptop.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Ana", email, Password));
        await phone.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync("/api/auth/me")).StatusCode);

        var logoutAll = await laptop.PostAsync("/api/auth/logout-all", null);

        Assert.Equal(HttpStatusCode.OK, logoutAll.StatusCode);
        Assert.Contains("\"revokedSessions\":2", await logoutAll.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await laptop.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Sessao_em_uso_e_renovada_e_a_expiracao_por_inatividade_avanca()
    {
        try
        {
            api.Clock.Offset = TimeSpan.FromHours(-2); // login "há duas horas"
            var (client, auth) = await api.CreateUserAsync();
            var before = await SessionOfAsync(auth.User.Id);
            api.Clock.Offset = TimeSpan.Zero;

            var renewed = await client.GetAsync("/api/auth/me");
            var after = await SessionOfAsync(auth.User.Id);
            var next = await client.GetAsync("/api/auth/me");

            Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
            Assert.NotNull(SessionCookieValue(renewed)); // token com mais de RenewAfterMinutes: cookie novo
            Assert.True(after.ExpiresAt - before.ExpiresAt > TimeSpan.FromHours(1.9), "A expiração por inatividade devia ter avançado ~2 h.");
            Assert.Equal(before.AbsoluteExpiresAt, after.AbsoluteExpiresAt);
            Assert.Null(SessionCookieValue(next)); // o token acabado de emitir não precisa de renovação
        }
        finally { api.Clock.Offset = TimeSpan.Zero; }
    }

    [Fact]
    public async Task Sessao_parada_mais_do_que_o_tempo_de_inatividade_expira()
    {
        var (client, auth) = await api.CreateUserAsync();

        await WithDbAsync(db => db.Sessions.Where(s => s.UserId == auth.User.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Renovacao_nunca_passa_do_limite_absoluto_e_depois_dele_a_sessao_acaba()
    {
        try
        {
            api.Clock.Offset = TimeSpan.FromHours(-2);
            var (client, auth) = await api.CreateUserAsync();
            api.Clock.Offset = TimeSpan.Zero;
            var absolute = DateTimeOffset.UtcNow.AddHours(1);
            await WithDbAsync(db => db.Sessions.Where(s => s.UserId == auth.User.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.AbsoluteExpiresAt, absolute)));

            await client.GetAsync("/api/auth/me"); // renova, mas só até ao limite absoluto
            var capped = await SessionOfAsync(auth.User.Id);
            await WithDbAsync(db => db.Sessions.Where(s => s.UserId == auth.User.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.AbsoluteExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));

            Assert.True((capped.ExpiresAt - absolute).Duration() < TimeSpan.FromSeconds(1), "A renovação passou do limite absoluto.");
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        }
        finally { api.Clock.Offset = TimeSpan.Zero; }
    }

    [Fact]
    public async Task Novo_login_apaga_as_sessoes_mortas_da_conta()
    {
        var email = $"limpeza-{Guid.NewGuid():N}@exemplo.pt";
        var client = api.CreateClient();
        var register = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Ana", email, Password));
        var userId = (await register.Content.ReadFromJsonAsync<AuthResponse>())!.User.Id;
        await client.PostAsync("/api/auth/logout", null); // fica uma sessão revogada

        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        var sessions = await WithDbAsync(db => db.Sessions.CountAsync(s => s.UserId == userId));

        Assert.Equal(1, sessions);
    }
}
