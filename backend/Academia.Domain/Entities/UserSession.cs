namespace Academia.Domain.Entities;

/// <summary>
/// Sessão de login guardada no servidor: o JWT do cookie leva o Id (claim "sid") e só vale enquanto esta linha estiver
/// ativa. Permite terminar sessões (logout, "todos os dispositivos") antes de o token expirar.
/// </summary>
public sealed class UserSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Expiração por inatividade: avança a cada renovação, sem passar AbsoluteExpiresAt.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
    /// <summary>Limite máximo desde o login, mesmo com uso contínuo.</summary>
    public DateTimeOffset AbsoluteExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public UserAccount? User { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now && AbsoluteExpiresAt > now;
}
