using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Academia.Application.Contracts;
using Academia.Domain.Entities;
using Academia.Infrastructure.Persistence;
using Academia.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Academia.Api.Auth;

/// <summary>Configuração "Auth": tempos de vida da sessão e flag Secure do cookie.</summary>
public sealed class SessionSettings
{
    /// <summary>Sem pedidos durante este tempo, a sessão expira.</summary>
    public int IdleDays { get; set; } = 7;
    /// <summary>Nenhuma sessão passa disto desde o login, mesmo em uso contínuo.</summary>
    public int AbsoluteDays { get; set; } = 30;
    /// <summary>Um pedido com um token mais antigo do que isto recebe um cookie novo e empurra a expiração por inatividade.</summary>
    public int RenewAfterMinutes { get; set; } = 60;
    public bool CookieSecure { get; set; } = true;
}

/// <summary>
/// Sessões no servidor (tabela Sessions). O JWT do cookie leva o id da sessão; cada pedido confirma que ela continua
/// ativa, por isso logout e "terminar em todos os dispositivos" valem logo, mesmo para um cookie copiado antes.
/// </summary>
public static class Sessions
{
    // "sid" é mapeado para ClaimTypes.Sid à entrada pelo JwtBearer; lê-se pelos dois nomes.
    private const string SessionIdClaim = "sid";

    public static bool TryGetSessionId(ClaimsPrincipal principal, out Guid sessionId) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.Sid) ?? principal.FindFirstValue(SessionIdClaim), out sessionId);

    /// <summary>Cria a sessão de um login/registo, grava-a e escreve o cookie. Aproveita para apagar as sessões mortas da conta.</summary>
    public static async Task<AuthResponse> StartAsync(AcademiaDbContext db, HttpResponse response, UserAccount user,
        TokenSettings jwt, SessionSettings settings, DateTimeOffset now)
    {
        await db.Sessions
            .Where(s => s.UserId == user.Id && (s.RevokedAt != null || s.ExpiresAt <= now || s.AbsoluteExpiresAt <= now))
            .ExecuteDeleteAsync();
        var session = new UserSession { UserId = user.Id, CreatedAt = now, AbsoluteExpiresAt = now.AddDays(settings.AbsoluteDays) };
        session.ExpiresAt = Min(now.AddDays(settings.IdleDays), session.AbsoluteExpiresAt);
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        WriteCookie(response, session, user.Id, user.Email, user.DisplayName, jwt, settings, now);
        // O cliente só precisa do limite que nunca muda; a inatividade descobre-a por 401.
        return new AuthResponse(session.AbsoluteExpiresAt, new UserResponse(user.Id, user.DisplayName, user.Email));
    }

    /// <summary>
    /// Chamado em cada pedido com token válido: devolve false se a sessão foi revogada ou expirou; se o token já tem
    /// RenewAfterMinutes, estende a sessão e reescreve o cookie (quem a guarda é o chamador, com SaveChanges).
    /// </summary>
    public static async Task<bool> ValidateAndRenewAsync(AcademiaDbContext db, HttpResponse response, ClaimsPrincipal principal,
        DateTime tokenIssuedAt, TokenSettings jwt, SessionSettings settings, DateTimeOffset now, CancellationToken ct)
    {
        if (!TryGetSessionId(principal, out var sessionId) || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return false;
        var session = await db.Sessions.SingleOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session is null || !session.IsActive(now)) return false;

        if (now.UtcDateTime - tokenIssuedAt >= TimeSpan.FromMinutes(settings.RenewAfterMinutes))
        {
            session.ExpiresAt = Min(now.AddDays(settings.IdleDays), session.AbsoluteExpiresAt);
            WriteCookie(response, session, userId, principal.FindFirstValue(ClaimTypes.Email) ?? "",
                principal.FindFirstValue(ClaimTypes.Name) ?? "", jwt, settings, now);
            await db.SaveChangesAsync(ct);
        }
        return true;
    }

    private static void WriteCookie(HttpResponse response, UserSession session, Guid userId, string email, string displayName,
        TokenSettings jwt, SessionSettings settings, DateTimeOffset now)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, displayName), new Claim(SessionIdClaim, session.Id.ToString())
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret));
        // O token vale tanto quanto a sessão; quem manda é a linha na base, consultada em cada pedido.
        var token = new JwtSecurityToken(jwt.Issuer, jwt.Audience, claims, now.UtcDateTime, session.ExpiresAt.UtcDateTime,
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        SessionCookie.Write(response, settings.CookieSecure, new JwtSecurityTokenHandler().WriteToken(token), session.ExpiresAt);
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
