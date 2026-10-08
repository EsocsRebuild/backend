using Platform.SharedKernel.Domain;

namespace Platform.Modules.Identity.Domain;

public enum ClientType
{
    Admin,
    Web,
    Mobile,
    Other,
}

/// <summary>
/// A signed-in device. Backs the refresh token (rotated on every use; replaying a rotated token revokes the
/// session), enforces the idle timeout, and holds the short-lived sudo token for dangerous operations.
/// </summary>
public sealed class UserSession : Entity
{
    private UserSession() { }

    public Guid UserId { get; private set; }

    /// <summary>Null for platform-operator sessions that are not bound to an organisation.</summary>
    public Guid? TenantId { get; private set; }

    public Guid? MembershipId { get; private set; }
    public ClientType ClientType { get; private set; }

    /// <summary>"Keep me signed in": longer idle timeout and lifetime.</summary>
    public bool Remember { get; private set; }

    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }

    [AuditIgnore] public string TokenHash { get; private set; } = string.Empty;
    [AuditIgnore] public string? PreviousTokenHash { get; private set; }
    [AuditIgnore] public string? SudoTokenHash { get; private set; }
    public DateTimeOffset? SudoExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastUsedAt { get; private set; }
    public DateTimeOffset? RotatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokedReason { get; private set; }

    public bool IsActive(DateTimeOffset now, TimeSpan idleTimeout) =>
        RevokedAt is null && ExpiresAt > now && LastUsedAt.Add(idleTimeout) > now;

    public static UserSession Start(
        Guid userId, Guid? tenantId, Guid? membershipId, ClientType clientType, bool remember, DateTimeOffset now, string? ipAddress, string? userAgent) => new()
        {
            UserId = userId,
            TenantId = tenantId,
            MembershipId = membershipId,
            ClientType = clientType,
            Remember = remember,
            CreatedAt = now,
            LastUsedAt = now,
            IpAddress = ipAddress,
            UserAgent = userAgent is { Length: > 512 } ua ? ua[..512] : userAgent,
        };

    public void Rotate(string newTokenHash, DateTimeOffset now, DateTimeOffset expiresAt, string? ipAddress)
    {
        PreviousTokenHash = TokenHash.Length == 0 ? null : TokenHash;
        TokenHash = newTokenHash;
        RotatedAt = now;
        LastUsedAt = now;
        IpAddress = ipAddress ?? IpAddress;
        ExpiresAt = expiresAt;
    }

    public void Touch(DateTimeOffset now) => LastUsedAt = now;

    public void GrantSudo(string tokenHash, DateTimeOffset expiresAt)
    {
        SudoTokenHash = tokenHash;
        SudoExpiresAt = expiresAt;
    }

    public bool HasSudo(string tokenHash, DateTimeOffset now) =>
        SudoTokenHash is not null && SudoExpiresAt > now && SudoTokenHash == tokenHash;

    public void Revoke(DateTimeOffset now, string reason)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevokedReason = reason;
        SudoTokenHash = null;
    }
}
