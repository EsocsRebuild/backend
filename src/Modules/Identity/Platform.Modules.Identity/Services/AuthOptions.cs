using System.ComponentModel.DataAnnotations;

namespace Platform.Modules.Identity.Services;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    [Required] public string Issuer { get; set; } = "platform";
    [Required] public string Audience { get; set; } = "platform-clients";

    /// <summary>HMAC-SHA256 key, at least 32 bytes. Supply via secret store / environment in production.</summary>
    [Required, MinLength(32)] public string SigningKey { get; set; } = null!;

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Session length without "keep me signed in".</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(12);

    /// <summary>Session length with "keep me signed in" (sliding, capped by <see cref="RememberedAbsoluteLifetime"/>).</summary>
    public TimeSpan RememberedSessionLifetime { get; set; } = TimeSpan.FromDays(30);

    public TimeSpan RememberedAbsoluteLifetime { get; set; } = TimeSpan.FromDays(90);

    /// <summary>Sessions with no activity for this long end (API contract: about 30 minutes).</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    public TimeSpan RememberedIdleTimeout { get; set; } = TimeSpan.FromDays(14);

    /// <summary>Concurrent refreshes (e.g. two tabs) within this window are not treated as token theft.</summary>
    public TimeSpan RefreshReuseGracePeriod { get; set; } = TimeSpan.FromSeconds(20);

    public TimeSpan SudoLifetime { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan MfaChallengeLifetime { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan PasswordResetLifetime { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan InvitationLifetime { get; set; } = TimeSpan.FromDays(7);

    public int MaxFailedAccessAttempts { get; set; } = 5;
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Reject passwords found in breach corpora (Have I Been Pwned range API, k-anonymity).</summary>
    public bool CheckBreachedPasswords { get; set; } = true;

    /// <summary>Base URL of the admin portal, used in email links (invitations, password resets).</summary>
    public string AppBaseUrl { get; set; } = "http://localhost:3001";

    /// <summary>Name shown in authenticator apps.</summary>
    public string TotpIssuer { get; set; } = "Admin Portal";
}
