using Platform.SharedKernel.Domain;

namespace Platform.Modules.People.Contracts;

/// <summary>A person profile was linked to a login account (Identity stores the link on the membership).</summary>
public sealed record PersonLinkedToAccountIntegrationEvent(Guid TenantId, Guid PersonId, Guid UserId, Guid MembershipId)
    : IntegrationEvent(TenantId);

public sealed record PersonCreatedIntegrationEvent(Guid TenantId, Guid PersonId, string FullName, string MembershipStatus, Guid? BranchId)
    : IntegrationEvent(TenantId);

public sealed record PersonMembershipStatusChangedIntegrationEvent(Guid TenantId, Guid PersonId, string From, string To)
    : IntegrationEvent(TenantId);

public sealed record PersonSummary(Guid Id, string MemberNumber, string FullName, string? Email, string? PhoneNumber, string? PhotoUrl, Guid? BranchId, Guid? UserId);

/// <summary>Audience selection for communications. All criteria combine with AND; empty lists mean "any".</summary>
public sealed record AudienceFilter(
    IReadOnlyList<string>? MembershipStatuses = null,
    IReadOnlyList<string>? Tags = null,
    Guid? BranchId = null,
    IReadOnlyList<Guid>? PersonIds = null,
    bool RequireConsent = true);

public sealed record ContactInfo(Guid PersonId, string FirstName, string FullName, string? Email, string? PhoneNumber, Guid? UserId);

/// <summary>Read-only public API of the People module (e.g. donor names in Giving, rosters in Groups).</summary>
public interface IPeopleDirectory
{
    Task<IReadOnlyDictionary<Guid, PersonSummary>> GetSummariesAsync(IEnumerable<Guid> personIds, CancellationToken cancellationToken);

    Task<Guid?> FindPersonIdByUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Contactable people matching the filter (respects communication consent by default).</summary>
    Task<IReadOnlyList<ContactInfo>> FindContactsAsync(AudienceFilter filter, CancellationToken cancellationToken);
}
