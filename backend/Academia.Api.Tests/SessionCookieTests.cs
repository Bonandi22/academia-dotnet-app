using System.Net;
using System.Net.Http.Json;
using Academia.Api.Auth;
using Academia.Application.Contracts;
using Microsoft.AspNetCore.Http;

namespace Academia.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class SessionCookieTests(ApiFactory api)
{
    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient client) =>
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Ana", $"cookie-{Guid.NewGuid():N}@exemplo.pt", "password-de-teste-123"));

    [Fact]
    public async Task Sessao_vai_num_cookie_httponly_samesite_strict_e_o_token_nao_aparece_no_corpo()
    {
        var response = await RegisterAsync(api.CreateClient());

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("academia_session=")).ToLowerInvariant();
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=strict", cookie);
        Assert.Contains("path=/", cookie);
        Assert.Contains("expires=", cookie);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("eyJ", body); // início de qualquer JWT em base64url
    }

    [Fact]
    public void Fora_de_development_o_cookie_e_secure_com_prefixo_host()
    {
        var options = SessionCookie.Options(secure: true, DateTimeOffset.UtcNow.AddHours(1));

        Assert.Equal("__Host-academia_session", SessionCookie.Name(secure: true));
        Assert.True(options.Secure);
        Assert.True(options.HttpOnly);
        Assert.Equal(SameSiteMode.Strict, options.SameSite);
        Assert.Equal("/", options.Path);
        Assert.Null(options.Domain); // o prefixo __Host- exige Secure, Path=/ e nenhum Domain
    }

    [Fact]
    public async Task Logout_apaga_o_cookie_e_a_sessao_deixa_de_valer()
    {
        var (client, _) = await api.CreateUserAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        var logout = await client.PostAsync("/api/auth/logout", null);
        var me = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"), c => c.StartsWith("academia_session=;"));
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Theory]
    [InlineData("POST", "/api/learning/sync")]
    [InlineData("PUT", "/api/learning/lessons")]
    [InlineData("DELETE", "/api/learning/reviews/quiz:q1")]
    [InlineData("POST", "/api/auth/login")]
    public async Task Pedido_que_altera_dados_sem_header_anti_csrf_devolve_403(string method, string url)
    {
        var (client, _) = await api.CreateUserAsync(); // sessão válida: o que falta é só o header
        client.DefaultRequestHeaders.Remove("X-Requested-With");

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = JsonContent.Create(new { }) });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("X-Requested-With", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Leitura_nao_precisa_do_header_anti_csrf()
    {
        var (client, _) = await api.CreateUserAsync();
        client.DefaultRequestHeaders.Remove("X-Requested-With");

        var response = await client.GetAsync("/api/learning/snapshot");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
