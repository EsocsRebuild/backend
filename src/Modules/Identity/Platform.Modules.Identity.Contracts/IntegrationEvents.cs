using Platform.SharedKernel.Domain;

namespace Platform.Modules.Identity.Contracts;

/// <summary>
/// An account now exists in an organisation (staff invited/approved, or a website member signed up).
/// The People module links it to a matching person profile or creates one.
/// </summary>
public sealed record MemberAccountCreatedIntegrationEvent(
    Guid TenantId,
    Guid UserId,
    Guid MembershipId,
    string Email,
    string Name,
    string? Phone,
    string Kind) : IntegrationEvent(TenantId);

/// <summary>A new admin access request is waiting for review (notifies administrators).</summary>
public sealed record AccessRequestSubmittedIntegrationEvent(Guid TenantId, Guid RequestId, string Name, string Email)
    : IntegrationEvent(TenantId);
