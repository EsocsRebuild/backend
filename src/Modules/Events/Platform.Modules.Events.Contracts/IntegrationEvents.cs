using Platform.SharedKernel.Domain;

namespace Platform.Modules.Events.Contracts;

/// <summary>A person was checked in. <see cref="IsFirstVisit"/> lets People open a first-timer follow-up.</summary>
public sealed record AttendanceRecordedIntegrationEvent(
    Guid TenantId, Guid CalendarEventId, Guid OccurrenceId, Guid PersonId, string EventTitle, DateTimeOffset CheckedInAt, bool IsFirstVisit)
    : IntegrationEvent(TenantId);

public sealed record EventPublishedIntegrationEvent(Guid TenantId, Guid CalendarEventId, string Title, DateTimeOffset StartsAt, string Visibility)
    : IntegrationEvent(TenantId);

public sealed record EventCancelledIntegrationEvent(Guid TenantId, Guid CalendarEventId, string Title) : IntegrationEvent(TenantId);
