using Platform.Modules.Events.Contracts;
using Platform.SharedKernel.Domain;

namespace Platform.Modules.Events.Domain;

public enum EventType
{
    Service,
    Meeting,
    Conference,
    Outreach,
    Class,
    Rehearsal,
    Social,
    Other,
}

public enum EventStatus
{
    Draft,
    Published,
    Cancelled,
}

public enum EventVisibility
{
    /// <summary>Shown on the public website and app.</summary>
    Public,

    /// <summary>Signed-in members only.</summary>
    Members,

    /// <summary>Staff calendar only.</summary>
    Internal,
}

/// <summary>
/// A calendar item: a Sunday service, midweek meeting, conference, class… One-off events have a
/// single occurrence; recurring events expand <see cref="RecurrenceRule"/> into
/// <see cref="EventOccurrence"/> rows, which is where registrations and attendance attach.
/// </summary>
public sealed class Event : TenantAggregateRoot
{
    private readonly List<EventOccurrence> _occurrences = [];

    private Event() { }

    public string Title { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public EventType Type { get; private set; }
    public EventStatus Status { get; private set; }
    public EventVisibility Visibility { get; private set; }
    public string? Summary { get; private set; }
    public string? Description { get; private set; }
    public string? CoverImageUrl { get; private set; }

    public Guid? BranchId { get; private set; }

    /// <summary>Owning ministry/group (Groups module id), e.g. a youth meeting.</summary>
    public Guid? GroupId { get; private set; }

    public string? Location { get; private set; }
    public bool IsOnline { get; private set; }
    public string? OnlineUrl { get; private set; }

    /// <summary>First occurrence start (UTC).</summary>
    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    /// <summary>IANA zone used to expand recurrences so "Sundays 9am" survives DST changes.</summary>
    public string TimeZone { get; private set; } = "UTC";

    public bool AllDay { get; private set; }

    /// <summary>iCalendar RRULE subset, e.g. <c>FREQ=WEEKLY;BYDAY=SU</c>. Null for one-off events.</summary>
    public string? RecurrenceRule { get; private set; }

    public bool RegistrationEnabled { get; private set; }
    public int? Capacity { get; private set; }
    public DateTimeOffset? RegistrationClosesAt { get; private set; }
    public int MaxGuestsPerRegistration { get; private set; }

    public IReadOnlyCollection<EventOccurrence> Occurrences => _occurrences.AsReadOnly();

    public TimeSpan Duration => EndsAt - StartsAt;

    public static Event Create(string title, string slug, EventType type) =>
        new() { Title = title.Trim(), Slug = slug, Type = type, Status = EventStatus.Draft, Visibility = EventVisibility.Public };

    public void Update(EventDetails d)
    {
        if (d.EndsAt <= d.StartsAt)
        {
            throw new DomainException("An event must end after it starts.");
        }

        Title = d.Title.Trim();
        Slug = d.Slug;
        Type = d.Type;
        Visibility = d.Visibility;
        Summary = d.Summary;
        Description = d.Description;
        CoverImageUrl = d.CoverImageUrl;
        BranchId = d.BranchId;
        GroupId = d.GroupId;
        Location = d.Location;
        IsOnline = d.IsOnline;
        OnlineUrl = d.OnlineUrl;
        StartsAt = d.StartsAt;
        EndsAt = d.EndsAt;
        TimeZone = d.TimeZone;
        AllDay = d.AllDay;
        RecurrenceRule = string.IsNullOrWhiteSpace(d.RecurrenceRule) ? null : d.RecurrenceRule.Trim().ToUpperInvariant();
        RegistrationEnabled = d.RegistrationEnabled;
        Capacity = d.Capacity;
        RegistrationClosesAt = d.RegistrationClosesAt;
        MaxGuestsPerRegistration = d.MaxGuestsPerRegistration;
    }

    public void Publish()
    {
        if (Status == EventStatus.Published)
        {
            return;
        }

        Status = EventStatus.Published;
        Raise(new EventPublishedIntegrationEvent(TenantId, Id, Title, StartsAt, Visibility.ToString()));
    }

    public void Cancel()
    {
        Status = EventStatus.Cancelled;
        foreach (var occurrence in _occurrences.Where(o => o.Status == OccurrenceStatus.Scheduled))
        {
            occurrence.Cancel();
        }

        Raise(new EventCancelledIntegrationEvent(TenantId, Id, Title));
    }

    /// <summary>
    /// Materialises occurrences from now until <paramref name="horizon"/>. Existing future occurrences that
    /// no longer match the schedule are removed if they have no attendance; past ones are never touched.
    /// </summary>
    public void SyncOccurrences(DateTimeOffset now, DateTimeOffset horizon, ISet<Guid> occurrencesWithActivity)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
        var starts = RecurrenceRule is null
            ? [StartsAt]
            : Recurrence.Parse(RecurrenceRule).Expand(StartsAt, tz, horizon).ToList();

        var wanted = starts.Where(s => s + Duration >= now).ToHashSet();

        foreach (var stale in _occurrences.Where(o => o.StartsAt >= now && !wanted.Contains(o.StartsAt) && !occurrencesWithActivity.Contains(o.Id)).ToList())
        {
            _occurrences.Remove(stale);
        }

        foreach (var start in wanted.Where(s => _occurrences.All(o => o.StartsAt != s)))
        {
            _occurrences.Add(new EventOccurrence(Id, start, start + Duration));
        }
    }
}

public sealed record EventDetails(
    string Title, string Slug, EventType Type, EventVisibility Visibility, string? Summary, string? Description, string? CoverImageUrl,
    Guid? BranchId, Guid? GroupId, string? Location, bool IsOnline, string? OnlineUrl, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    string TimeZone, bool AllDay, string? RecurrenceRule, bool RegistrationEnabled, int? Capacity, DateTimeOffset? RegistrationClosesAt,
    int MaxGuestsPerRegistration);

public enum OccurrenceStatus
{
    Scheduled,
    Cancelled,
    Completed,
}

/// <summary>A concrete instance of an event (e.g. "Sunday Service, 5 Oct 2026 09:00").</summary>
public sealed class EventOccurrence : TenantEntity
{
    private EventOccurrence() { }

    internal EventOccurrence(Guid eventId, DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        EventId = eventId;
        StartsAt = startsAt;
        EndsAt = endsAt;
        Status = OccurrenceStatus.Scheduled;
        CheckInCode = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4));
    }

    public Guid EventId { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public OccurrenceStatus Status { get; private set; }

    /// <summary>Short code encoded in the venue QR poster for mobile self check-in.</summary>
    public string CheckInCode { get; private set; } = null!;

    public string? Notes { get; private set; }

    public void Cancel() => Status = OccurrenceStatus.Cancelled;

    public void Complete(string? notes)
    {
        Status = OccurrenceStatus.Completed;
        Notes = notes;
    }
}
