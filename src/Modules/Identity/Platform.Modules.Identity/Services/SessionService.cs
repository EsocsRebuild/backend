using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Platform.Application.Abstractions;
using Platform.Application.Security;
using Platform.Infrastructure.Auditing;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Identity.Infrastructure;
using Platform.SharedKernel.Results;

namespace Platform.Modules.Identity.Services;

internal static class AuthErrors
{
    public static readonly Error InvalidCredentials = Error.Unauthorized("auth.invalid_credentials", "That email and password don’t match.");
    public static readonly Error LockedOut = Error.RateLimited("auth.locked_out", "Too many attempts. Please wait a few minutes and try again.");
    public static readonly Error SessionEnded = Error.Unauthorized("auth.session_ended", "Your session has ended. Please sign in again.");
    public static readonly Error ChallengeExpired = Error.Unauthorized("auth.challenge_expired", "That sign-in attempt expired. Please start again.");
    public static readonly Error WrongCode = Error.Validation("auth.wrong_code", "That code didn’t work. Check it and try again.",
        new Dictionary<string, string[]> { ["code"] = ["That code didn’t work."] });
    public static readonly Error WrongPassword = Error.Validation("auth.wrong_password", "That password isn’t right.",
        new Dictionary<string, string[]> { ["password"] = ["That password isn’t right."] });
    public static readonly Error AwaitingApproval = Error.Forbidden("auth.awaiting_approval",
        "Your access request is waiting for an administrator to approve it. We’ll email you when it’s done.");
    public static readonly Error InvalidLink = Error.Validation("auth.invalid_link", "This link is invalid or has expired. Please request a new one.");

    public static Error WeakPassword(string message, string field = "password") =>
        Error.Validation("auth.weak_password", message, new Dictionary<string, string[]> { [field] = [message] });
}

/// <summary>Password verification with lockout and audit of failures.</summary>
internal sealed class CredentialService(
    IdentityDbContext db,
    IPasswordHasher<User> hasher,
    IOptions<AuthOptions> options,
    IAuditLog audit,
    TimeProvider clock)
{
    /// <summary>A real Argon2id hash used when the email is unknown, so both paths take the same time.</summary>
    private static readonly Lazy<string> DummyHash = new(() => new Argon2PasswordHasher().HashPassword(Guid.NewGuid().ToString()));

    public async Task<Result<User>> VerifyAsync(string email, string password, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var normalized = email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);

        if (user?.PasswordHash is null || user.Status == UserStatus.Disabled)
        {
            hasher.VerifyHashedPassword(null!, DummyHash.Value, password);
            audit.Record("auth.login_failed", $"Failed sign-in for {normalized}", AuditSeverity.Warning,
                actor: new AuditActor(null, null, normalized));
            await audit.FlushAsync(ct);
            return AuthErrors.InvalidCredentials;
        }

        if (user.IsLockedOut(now))
        {
            audit.Record("auth.login_locked", $"Sign-in blocked while locked: {user.Email}", AuditSeverity.Warning, Target(user), actor: Actor(user));
            await audit.FlushAsync(ct);
            return AuthErrors.LockedOut;
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            user.RegisterFailedLogin(now, options.Value.MaxFailedAccessAttempts, options.Value.LockoutDuration);
            audit.Record("auth.login_failed", $"Failed sign-in for {user.Email}",
                user.IsLockedOut(now) ? AuditSeverity.Critical : AuditSeverity.Warning, Target(user), actor: Actor(user));
            await db.SaveChangesAsync(ct);
            return user.IsLockedOut(now) ? AuthErrors.LockedOut : AuthErrors.InvalidCredentials;
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.UpgradePasswordHash(hasher.HashPassword(user, password));
        }

        return user;
    }

    public static AuditActor Actor(User user) => new(null, user.Name, user.Email);

    public static AuditTarget Target(User user) => new("user", user.Id.ToString(), user.Name);
}

/// <summary>Creates, refreshes, validates and revokes sessions (refresh-token rotation with theft detection).</summary>
internal sealed class SessionService(
    IdentityDbContext db,
    TokenService tokens,
    IOptions<AuthOptions> options,
    IRequestInfo request,
    TimeProvider clock)
{
    public TimeSpan IdleTimeout(UserSession session) => session.Remember ? options.Value.RememberedIdleTimeout : options.Value.IdleTimeout;

    public async Task<AuthTokens> StartAsync(User user, TenantMembership? membership, ClientType client, bool remember, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        user.RegisterSuccessfulLogin(now);
        membership?.Touch(now);

        var session = UserSession.Start(user.Id, membership?.TenantId, membership?.Id, client, remember, now, request.IpAddress, request.UserAgent);
        var (refreshToken, hash) = RefreshTokenFormat.Create(session.Id);
        session.Rotate(hash, now, Expiry(session, now), null);
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);

        return Build(user, membership, session, refreshToken, now);
    }

    public async Task<Result<AuthTokens>> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        if (!RefreshTokenFormat.TryParse(refreshToken, out var sessionId, out var hash))
        {
            return AuthErrors.SessionEnded;
        }

        var now = clock.GetUtcNow();
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null || !session.IsActive(now, IdleTimeout(session)))
        {
            return AuthErrors.SessionEnded;
        }

        if (!SecretHasher.FixedTimeEquals(session.TokenHash, hash))
        {
            var replayed = session.PreviousTokenHash is not null && SecretHasher.FixedTimeEquals(session.PreviousTokenHash, hash);
            if (replayed && !(session.RotatedAt is { } rotated && now - rotated <= options.Value.RefreshReuseGracePeriod))
            {
                // A rotated token came back after the grace window: assume theft and end the session.
                session.Revoke(now, "refresh_token_reuse");
                await db.SaveChangesAsync(ct);
            }

            return AuthErrors.SessionEnded;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == session.UserId, ct);
        if (user is null || user.Status != UserStatus.Active)
        {
            session.Revoke(now, "user_inactive");
            await db.SaveChangesAsync(ct);
            return AuthErrors.SessionEnded;
        }

        TenantMembership? membership = null;
        if (session.MembershipId is { } membershipId)
        {
            membership = await db.Memberships.IgnoreQueryFilters([QueryFilters.Tenant])
                .FirstOrDefaultAsync(m => m.Id == membershipId && m.Status == MembershipStatus.Active, ct);
            if (membership is null)
            {
                session.Revoke(now, "membership_inactive");
                await db.SaveChangesAsync(ct);
                return AuthErrors.SessionEnded;
            }

            membership.Touch(now);
        }

        var (newToken, newHash) = RefreshTokenFormat.Create(session.Id);
        session.Rotate(newHash, now, Expiry(session, now), request.IpAddress);
        await db.SaveChangesAsync(ct);
        return Build(user, membership, session, newToken, now);
    }

    /// <summary>Marks sessions revoked (caller saves).</summary>
    public async Task<int> RevokeAllAsync(Guid userId, string reason, Guid? exceptSessionId, CancellationToken ct, Guid? membershipId = null)
    {
        var now = clock.GetUtcNow();
        var query = db.Sessions.Where(s => s.UserId == userId && s.RevokedAt == null && s.Id != exceptSessionId);
        if (membershipId is not null)
        {
            query = query.Where(s => s.MembershipId == membershipId);
        }

        var sessions = await query.ToListAsync(ct);
        sessions.ForEach(s => s.Revoke(now, reason));
        return sessions.Count;
    }

    private DateTimeOffset Expiry(UserSession session, DateTimeOffset now)
    {
        var o = options.Value;
        if (!session.Remember)
        {
            return session.CreatedAt.Add(o.SessionLifetime);
        }

        var sliding = now.Add(o.RememberedSessionLifetime);
        var absolute = session.CreatedAt.Add(o.RememberedAbsoluteLifetime);
        return sliding < absolute ? sliding : absolute;
    }

    private AuthTokens Build(User user, TenantMembership? membership, UserSession session, string refreshToken, DateTimeOffset now)
    {
        var (access, expiresIn) = tokens.CreateAccessToken(user, membership, session);
        return new AuthTokens(access, expiresIn, refreshToken, (int)Math.Max(0, (session.ExpiresAt - now).TotalSeconds));
    }
}

/// <summary>
/// Runs on every authenticated request (JWT <c>OnTokenValidated</c>): the session behind the token must still
/// be live, so revocation, suspension and the idle timeout take effect immediately. Activity is recorded at
/// most once a minute to keep writes cheap.
/// </summary>
internal sealed class SessionValidator(IdentityDbContext db, IOptions<AuthOptions> options, TimeProvider clock)
{
    public async Task<bool> ValidateAsync(Guid sessionId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var session = await db.Sessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => new { s.RevokedAt, s.ExpiresAt, s.LastUsedAt, s.Remember, s.MembershipId })
            .FirstOrDefaultAsync(ct);

        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= now)
        {
            return false;
        }

        var idle = session.Remember ? options.Value.RememberedIdleTimeout : options.Value.IdleTimeout;
        if (session.LastUsedAt.Add(idle) <= now)
        {
            return false;
        }

        if (now - session.LastUsedAt > TimeSpan.FromMinutes(1))
        {
            await db.Sessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastUsedAt, now), ct);
            if (session.MembershipId is { } membershipId)
            {
                await db.Memberships.IgnoreQueryFilters([QueryFilters.Tenant]).Where(m => m.Id == membershipId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastActiveAt, now), ct);
            }
        }

        return true;
    }
}

internal sealed class SudoVerifier(IdentityDbContext db, ICurrentUser user, TimeProvider clock) : ISudoVerifier
{
    public async Task<bool> IsValidAsync(string token, CancellationToken cancellationToken)
    {
        if (user.SessionId is not { } sessionId)
        {
            return false;
        }

        var hash = SecretHasher.Hash(token);
        var session = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        return session is not null && session.HasSudo(hash, clock.GetUtcNow());
    }
}

/// <summary>Writes audit events that have no accompanying data change.</summary>
internal sealed class AuditEventStore(IdentityDbContext db) : IAuditEventStore
{
    public async Task SaveAsync(IReadOnlyCollection<AuditEvent> events, CancellationToken cancellationToken)
    {
        db.AuditEvents.AddRange(events);
        await db.SaveChangesAsync(cancellationToken);
    }
}
