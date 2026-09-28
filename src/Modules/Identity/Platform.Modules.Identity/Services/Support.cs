using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Platform.Application.Security;
using Platform.Infrastructure.Caching;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Identity.Infrastructure;
using Platform.SharedKernel.Results;

namespace Platform.Modules.Identity.Services;

/// <summary>
/// Effective access = the membership's role permissions plus its parish scope. Cached per user and tagged
/// per organisation; any role, membership or scope change invalidates the organisation's tag.
/// </summary>
internal sealed class PermissionService(IdentityDbContext db, HybridCache cache) : IPermissionService
{
    private sealed record CachedAccess(Guid MembershipId, string[] Permissions, Guid? ScopeUnitId, bool IsStaff);

    public async Task<AccessProfile?> GetAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        var cached = await cache.GetOrCreateAsync(
            CacheKeys.Permissions(tenantId, userId),
            async ct => await LoadAsync(userId, tenantId, ct),
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(10) },
            tags: [CacheKeys.PermissionsTag(tenantId)],
            cancellationToken: cancellationToken);

        return cached is null
            ? null
            : new AccessProfile(cached.MembershipId, cached.Permissions.ToHashSet(StringComparer.Ordinal), cached.ScopeUnitId, cached.IsStaff);
    }

    public async Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await cache.RemoveByTagAsync(CacheKeys.PermissionsTag(tenantId), cancellationToken);

    private async Task<CachedAccess?> LoadAsync(Guid userId, Guid tenantId, CancellationToken ct)
    {
        // Staff access wins over a member account in the same organisation.
        var membership = await db.Memberships.AsNoTracking()
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Include(m => m.Roles)
            .Where(m => m.TenantId == tenantId && m.UserId == userId && m.Status == MembershipStatus.Active)
            .OrderBy(m => m.Kind)
            .FirstOrDefaultAsync(ct);
        if (membership is null)
        {
            return null;
        }

        var roleIds = membership.Roles.Select(r => r.RoleId).ToList();
        var permissions = await db.Roles.AsNoTracking()
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(r => r.TenantId == tenantId && roleIds.Contains(r.Id))
            .Select(r => r.Permissions)
            .ToListAsync(ct);

        return new CachedAccess(membership.Id, permissions.SelectMany(p => p).Distinct(StringComparer.Ordinal).ToArray(),
            membership.ScopeUnitId, membership.Kind == MembershipKind.Staff);
    }
}

/// <summary>Issues and checks 6-digit one-time codes (email verification, member sign-in).</summary>
internal sealed class OneTimeCodeService(IdentityDbContext db, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ResendInterval = TimeSpan.FromSeconds(45);

    public static readonly Error TooSoon = Error.RateLimited("code.too_soon", "Please wait a moment before asking for another code.");

    /// <summary>Creates a new code (expiring earlier ones). Returns the plain code to send, or an error when asked too soon.</summary>
    public async Task<Result<string>> IssueAsync(Guid? tenantId, CodePurpose purpose, string destination, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var previous = await db.Codes
            .Where(c => c.TenantId == tenantId && c.Purpose == purpose && c.Destination == destination && c.ConsumedAt == null)
            .ToListAsync(ct);

        if (previous.Any(c => now - c.SentAt < ResendInterval))
        {
            return TooSoon;
        }

        previous.ForEach(c => c.Expire(now));
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        db.Codes.Add(OneTimeCode.Issue(tenantId, purpose, destination, SecretHasher.Hash(code), now, Lifetime));
        return code;
    }

    /// <summary>Checks a code; counts the attempt either way (caller saves).</summary>
    public async Task<bool> VerifyAsync(Guid? tenantId, CodePurpose purpose, string destination, string code, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var current = await db.Codes
            .Where(c => c.TenantId == tenantId && c.Purpose == purpose && c.Destination == destination && c.ConsumedAt == null && c.ExpiresAt > now)
            .OrderByDescending(c => c.SentAt)
            .FirstOrDefaultAsync(ct);

        return current is not null && current.TryConsume(SecretHasher.Hash(code.Trim()), now);
    }
}

/// <summary>Turns a user-agent string into "Chrome" / "macOS" for the sessions list. Deliberately simple.</summary>
internal static partial class UserAgentParser
{
    public static (string Browser, string Os) Parse(string? userAgent)
    {
        var ua = userAgent ?? string.Empty;
        var browser = ua switch
        {
            _ when ua.Contains("Edg/", StringComparison.Ordinal) => "Edge",
            _ when ua.Contains("OPR/", StringComparison.Ordinal) => "Opera",
            _ when ua.Contains("Firefox/", StringComparison.Ordinal) => "Firefox",
            _ when ua.Contains("Chrome/", StringComparison.Ordinal) => "Chrome",
            _ when ua.Contains("Safari/", StringComparison.Ordinal) => "Safari",
            _ when ua.Contains("okhttp", StringComparison.OrdinalIgnoreCase) || ua.Contains("Dart/", StringComparison.Ordinal) => "Mobile app",
            _ when ua.Contains("node", StringComparison.OrdinalIgnoreCase) => "Server",
            _ => "Unknown browser",
        };
        var os = ua switch
        {
            _ when IPhone().IsMatch(ua) => "iOS",
            _ when ua.Contains("Android", StringComparison.Ordinal) => "Android",
            _ when ua.Contains("Mac OS X", StringComparison.Ordinal) || ua.Contains("Macintosh", StringComparison.Ordinal) => "macOS",
            _ when ua.Contains("Windows", StringComparison.Ordinal) => "Windows",
            _ when ua.Contains("CrOS", StringComparison.Ordinal) => "ChromeOS",
            _ when ua.Contains("Linux", StringComparison.Ordinal) => "Linux",
            _ => "Unknown device",
        };
        return (browser, os);
    }

    [GeneratedRegex("iPhone|iPad|iPod")]
    private static partial Regex IPhone();
}
