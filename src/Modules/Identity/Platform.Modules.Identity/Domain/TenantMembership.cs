using Platform.Modules.Identity.Contracts;
using Platform.SharedKernel.Domain;

namespace Platform.Modules.Identity.Domain;

public enum MembershipKind
{
    /// <summary>Administrator of the organisation: signs in to the admin portal.</summary>
    Staff,

    /// <summary>Website / mobile app account of a church member. Never has portal access.</summary>
    Member,
}

public enum MembershipStatus
{
    Invited,
    Active,
    Suspended,
}

/// <summary>What a staff member wants to be told about (<c>/me/notification-preferences</c>).</summary>
public sealed record NotificationPreferences
{
    public bool AccessRequests { get; init; } = true;
    public bool FormResponses { get; init; } = true;
    public bool CampaignReports { get; init; } = true;
    public bool WeeklySummary { get; init; }
}

/// <summary>
/// A <see cref="User"/>'s access to one organisation. Staff hold exactly one role and may be limited
/// to one unit (parish) and everything under it via <see cref="ScopeUnitId"/>.
/// </summary>
public sealed class TenantMembership : TenantAggregateRoot
{
    private readonly List<MembershipRole> _roles = [];

    private TenantMembership() { }

    public Guid UserId { get; private set; }
    public MembershipKind Kind { get; private set; }
    public MembershipStatus Status { get; private set; }

    /// <summary>Parish scope for parish-level administrators; null means organisation-wide.</summary>
    public Guid? ScopeUnitId { get; private set; }

    /// <summary>The person profile in the People module (reference by id only).</summary>
    public Guid? PersonId { get; private set; }

    public DateTimeOffset? JoinedAt { get; private set; }
    public Guid? InvitedBy { get; private set; }
    public DateTimeOffset? LastActiveAt { get; private set; }
    public NotificationPreferences NotificationPreferences { get; private set; } = new();

    public IReadOnlyCollection<MembershipRole> Roles => _roles.AsReadOnly();

    public Guid? RoleId => _roles.FirstOrDefault()?.RoleId;

    public static TenantMembership Create(Guid tenantId, Guid userId, MembershipKind kind, MembershipStatus status, Guid? invitedBy, DateTimeOffset now)
    {
        var membership = new TenantMembership
        {
            UserId = userId,
            Kind = kind,
            Status = status,
            InvitedBy = invitedBy,
            JoinedAt = status == MembershipStatus.Active ? now : null,
        };
        membership.AssignTenant(tenantId);
        return membership;
    }

    public void Activate(DateTimeOffset now)
    {
        Status = MembershipStatus.Active;
        JoinedAt ??= now;
    }

    public void Suspend() => Status = MembershipStatus.Suspended;

    public void LinkPerson(Guid personId) => PersonId = personId;

    public void SetScope(Guid? unitId) => ScopeUnitId = unitId;

    public void Touch(DateTimeOffset now) => LastActiveAt = now;

    public void SetNotificationPreferences(NotificationPreferences preferences) => NotificationPreferences = preferences;

    /// <summary>Staff have exactly one role; assigning replaces it.</summary>
    public void AssignRole(Guid roleId)
    {
        _roles.RemoveAll(r => r.RoleId != roleId);
        if (_roles.Count == 0)
        {
            _roles.Add(new MembershipRole(Id, roleId));
        }
    }

    /// <summary>Tells other modules (People) that an account now exists for this organisation.</summary>
    public void AnnounceAccountCreated(User user) =>
        Raise(new MemberAccountCreatedIntegrationEvent(TenantId, UserId, Id, user.Email, user.Name, user.Phone, Kind.ToString()));
}

public sealed class MembershipRole
{
    private MembershipRole() { }

    internal MembershipRole(Guid membershipId, Guid roleId)
    {
        MembershipId = membershipId;
        RoleId = roleId;
    }

    public Guid MembershipId { get; private set; }
    public Guid RoleId { get; private set; }
}
