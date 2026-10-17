namespace Academia.Api.Auth;

/// <summary>
/// O JWT viaja só neste cookie: HttpOnly (o JavaScript não o lê, por isso um XSS não o rouba), SameSite=Strict
/// (não vai em pedidos iniciados por outros sites) e, com Secure, o prefixo __Host- (só HTTPS, Path=/, sem Domain).
/// Secure vem de Auth:CookieSecure: ligado por omissão; desligado em Development e nos testes (HTTP sem localhost seguro).
/// </summary>
public static class SessionCookie
{
    public const string SecureName = "__Host-academia_session";
    public const string InsecureName = "academia_session";

    public static string Name(bool secure) => secure ? SecureName : InsecureName;

    public static CookieOptions Options(bool secure, DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = secure,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expires,
        IsEssential = true
    };

    public static void Write(HttpResponse response, bool secure, string token, DateTimeOffset expires) =>
        response.Cookies.Append(Name(secure), token, Options(secure, expires));

    public static void Delete(HttpResponse response, bool secure) =>
        response.Cookies.Delete(Name(secure), Options(secure, DateTimeOffset.UnixEpoch));
}
