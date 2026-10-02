using Platform.Modules.Identity.Contracts;
using Platform.SharedKernel.Domain;

namespace Platform.Modules.Identity.Domain;

/// <summary>A pending staff invitation: a single-use link (<c>APP_URL/invite/&lt;token&gt;</c>) valid for 7 days.</summary>
public sealed class StaffInvitation : TenantEntity
{
    private StaffInvitation() { }

    public Guid MembershipId { get; private set; }
    public string Email { get; private set; } = null!;
    [AuditIgnore] public string TokenHash { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public string? InvitedByName { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static StaffInvitation Issue(Guid membershipId, string email, string tokenHash, DateTimeOffset expiresAt, string? invitedByName) =>
        new() { MembershipId = membershipId, Email = email, TokenHash = tokenHash, ExpiresAt = expiresAt, InvitedByName = invitedByName };

    public bool IsUsable(DateTimeOffset now) => AcceptedAt is null && RevokedAt is null && ExpiresAt > now;

    public void Accept(DateTimeOffset now) => AcceptedAt = now;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}

public enum AccessRequestStatus
{
    Pending,
    Approved,
    Rejected,
}

/// <summary>
/// Someone asked for portal access from the sign-up page. It is not an account: an administrator must
/// approve it (and only once the email address is verified). The chosen password is kept hashed so the
/// person can sign in as soon as they are approved.
/// </summary>
public sealed class AccessRequest : TenantAggregateRoot
{
    private AccessRequest() { }

    public string Name { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string? Phone { get; private set; }
    public Guid? ParishId { get; private set; }
    public Guid? RequestedRoleId { get; private set; }
    [AuditIgnore] public string PasswordHash { get; private set; } = null!;
    public DateTimeOffset? EmailVerifiedAt { get; private set; }
    public AccessRequestStatus Status { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public string? RejectionReason { get; private set; }
    public Guid? MembershipId { get; private set; }

    public static AccessRequest Submit(string name, string email, string? phone, Guid? parishId, Guid? requestedRoleId, string passwordHash) => new()
    {
        Name = name.Trim(),
        Email = email.Trim().ToLowerInvariant(),
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
        ParishId = parishId,
        RequestedRoleId = requestedRoleId,
        PasswordHash = passwordHash,
        Status = AccessRequestStatus.Pending,
    };

    public void MarkEmailVerified(DateTimeOffset now) => EmailVerifiedAt ??= now;

    /// <summary>Announces the (now verified) request so administrators are notified.</summary>
    public void RaiseSubmitted() => Raise(new AccessRequestSubmittedIntegrationEvent(TenantId, Id, Name, Email));

    public void Approve(Guid membershipId, Guid? decidedBy, DateTimeOffset now)
    {
        EnsurePending();
        if (EmailVerifiedAt is null)
        {
            throw new DomainException("This person hasn’t verified their email address yet.");
        }

        Status = AccessRequestStatus.Approved;
        MembershipId = membershipId;
        DecidedBy = decidedBy;
        DecidedAt = now;
    }

    public void Reject(string? reason, Guid? decidedBy, DateTimeOffset now)
    {
        EnsurePending();
        Status = AccessRequestStatus.Rejected;
        RejectionReason = reason;
        DecidedBy = decidedBy;
        DecidedAt = now;
    }

    private void EnsurePending()
    {
        if (Status != AccessRequestStatus.Pending)
        {
            throw new DomainException("This request has already been decided.");
        }
    }
}

public enum CodePurpose
{
    /// <summary>Verifies the email on an admin access request.</summary>
    AccessRequestEmail,

    /// <summary>Passwordless sign-in for website / app members.</summary>
    MemberSignIn,
}

/// <summary>
/// A 6-digit one-time code sent by email or SMS: stored hashed, valid 15 minutes, 5 attempts,
/// resendable at most every 45 seconds.
/// </summary>
public sealed class OneTimeCode : Entity
{
    private OneTimeCode() { }

    public Guid? TenantId { get; private set; }
    public CodePurpose Purpose { get; private set; }

    /// <summary>Normalised email or E.164 phone number.</summary>
    public string Destination { get; private set; } = null!;

    [AuditIgnore] public string CodeHash { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset SentAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    public static OneTimeCode Issue(Guid? tenantId, CodePurpose purpose, string destination, string codeHash, DateTimeOffset now, TimeSpan lifetime) => new()
    {
        TenantId = tenantId,
        Purpose = purpose,
        Destination = destination,
        CodeHash = codeHash,
        SentAt = now,
        ExpiresAt = now.Add(lifetime),
    };

    public const int MaxAttempts = 5;

    public bool IsUsable(DateTimeOffset now) => ConsumedAt is null && ExpiresAt > now && Attempts < MaxAttempts;

    /// <summary>Counts the attempt; true (and consumed) when the hash matches a usable code.</summary>
    public bool TryConsume(string codeHash, DateTimeOffset now)
    {
        if (!IsUsable(now))
        {
            return false;
        }

        Attempts++;
        if (!string.Equals(CodeHash, codeHash, StringComparison.Ordinal))
        {
            return false;
        }

        ConsumedAt = now;
        return true;
    }

    public void Expire(DateTimeOffset now) => ConsumedAt ??= now;
}
