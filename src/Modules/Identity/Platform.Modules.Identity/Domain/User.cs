using Platform.SharedKernel.Domain;

namespace Platform.Modules.Identity.Domain;

public enum UserStatus
{
    /// <summary>Invited or created without a password yet.</summary>
    Pending,
    Active,
    Disabled,
}

/// <summary>
/// A global login identity. One person has one account across organisations; what they may do in an
/// organisation comes from their <see cref="TenantMembership"/> there.
/// </summary>
public sealed class User : AuditableAggregateRoot
{
    private User() { }

    public string Email { get; private set; } = null!;
    public bool EmailConfirmed { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Phone { get; private set; }
    public string? AvatarUrl { get; private set; }
    public UserStatus Status { get; private set; }

    /// <summary>SaaS operator staff. Grants the platform console only — never an organisation's data.</summary>
    public bool IsPlatformAdmin { get; private set; }

    [AuditIgnore] public string? PasswordHash { get; private set; }

    /// <summary>Rotated on credential changes; invalidates outstanding reset links.</summary>
    [AuditIgnore] public string SecurityStamp { get; private set; } = NewStamp();

    public int AccessFailedCount { get; private set; }
    public DateTimeOffset? LockoutEndsAt { get; private set; }

    public bool TwoFactorEnabled { get; private set; }

    /// <summary>TOTP shared secret, encrypted at rest with ASP.NET Data Protection.</summary>
    [AuditIgnore] public string? TwoFactorSecret { get; private set; }

    /// <summary>Last accepted TOTP time step — a code can never be used twice.</summary>
    [AuditIgnore] public long LastTotpStep { get; private set; }

    /// <summary>SHA-256 hashes of unused recovery codes.</summary>
    [AuditIgnore] public List<string> RecoveryCodeHashes { get; private set; } = [];

    public DateTimeOffset? LastLoginAt { get; private set; }
    public DateTimeOffset? PasswordChangedAt { get; private set; }

    public static User Create(string email, string name, string? phone = null) => new()
    {
        Email = email.Trim().ToLowerInvariant(),
        Name = name.Trim(),
        Phone = phone,
        Status = UserStatus.Pending,
    };

    public void UpdateProfile(string name, string? phone)
    {
        Name = name.Trim();
        Phone = phone;
    }

    public void SetAvatar(string? url) => AvatarUrl = url;

    public void SetPassword(string passwordHash, DateTimeOffset now)
    {
        PasswordHash = passwordHash;
        PasswordChangedAt = now;
        SecurityStamp = NewStamp();
        if (Status == UserStatus.Pending)
        {
            Status = UserStatus.Active;
        }
    }

    /// <summary>Carries over a hash already computed for an approved access request.</summary>
    public void AdoptPasswordHash(string passwordHash, DateTimeOffset now) => SetPassword(passwordHash, now);

    /// <summary>Transparent re-hash when the hashing algorithm or its parameters are upgraded.</summary>
    public void UpgradePasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public void ConfirmEmail() => EmailConfirmed = true;

    public bool IsLockedOut(DateTimeOffset now) => LockoutEndsAt is { } end && end > now;

    /// <summary>Progressive lockout: each lock after <paramref name="maxAttempts"/> failures doubles, up to a day.</summary>
    public void RegisterFailedLogin(DateTimeOffset now, int maxAttempts, TimeSpan lockoutDuration)
    {
        AccessFailedCount++;
        if (AccessFailedCount >= maxAttempts && AccessFailedCount % maxAttempts == 0)
        {
            var strikes = AccessFailedCount / maxAttempts;
            var duration = TimeSpan.FromTicks(Math.Min(lockoutDuration.Ticks * (1L << Math.Min(strikes - 1, 8)), TimeSpan.FromDays(1).Ticks));
            LockoutEndsAt = now.Add(duration);
        }
    }

    public void RegisterSuccessfulLogin(DateTimeOffset now)
    {
        AccessFailedCount = 0;
        LockoutEndsAt = null;
        LastLoginAt = now;
    }

    public void BeginTwoFactorSetup(string protectedSecret) => TwoFactorSecret = protectedSecret;

    /// <summary>Records a used TOTP step; false when the step (or an earlier one) was already used.</summary>
    public bool ConsumeTotpStep(long step)
    {
        if (step <= LastTotpStep)
        {
            return false;
        }

        LastTotpStep = step;
        return true;
    }

    public void EnableTwoFactor(IEnumerable<string> recoveryCodeHashes)
    {
        if (TwoFactorSecret is null)
        {
            throw new DomainException("Two-step verification setup hasn’t been started.");
        }

        TwoFactorEnabled = true;
        RecoveryCodeHashes = recoveryCodeHashes.ToList();
        SecurityStamp = NewStamp();
    }

    public void ReplaceRecoveryCodes(IEnumerable<string> recoveryCodeHashes) => RecoveryCodeHashes = recoveryCodeHashes.ToList();

    public void DisableTwoFactor()
    {
        TwoFactorEnabled = false;
        TwoFactorSecret = null;
        LastTotpStep = 0;
        RecoveryCodeHashes = [];
        SecurityStamp = NewStamp();
    }

    public bool RedeemRecoveryCode(string codeHash) => RecoveryCodeHashes.Remove(codeHash);

    public void Disable()
    {
        Status = UserStatus.Disabled;
        SecurityStamp = NewStamp();
    }

    public void Enable() => Status = PasswordHash is null ? UserStatus.Pending : UserStatus.Active;

    public void GrantPlatformAdmin(bool value) => IsPlatformAdmin = value;

    private static string NewStamp() => Guid.NewGuid().ToString("N");
}
