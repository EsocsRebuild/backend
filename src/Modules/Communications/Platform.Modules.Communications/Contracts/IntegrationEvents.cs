using Platform.SharedKernel.Domain;

namespace Platform.Modules.Communications.Contracts;

/// <summary>
/// Published when a member or guest submits a prayer request for the prayer wall.
/// </summary>
public sealed record PrayerCreatedIntegrationEvent(
    Guid TenantId,
    Guid PrayerRequestId,
    string Title,
    string? Content,
    string AuthorName,
    DateTimeOffset CreatedAt) : IntegrationEvent(TenantId);
