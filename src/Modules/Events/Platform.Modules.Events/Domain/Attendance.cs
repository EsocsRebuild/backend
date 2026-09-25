using System.Security.Cryptography;
using Platform.Modules.Events.Contracts;
using Platform.SharedKernel.Domain;

namespace Platform.Modules.Events.Domain;

public enum RegistrationStatus
{
    Confirmed,
    Waitlisted,
    Cancelled,
    CheckedIn,
}

/// <summary>A booking for an event occurrence, by a known person or an anonymous guest from the website.</summary>
public sealed class Registration : TenantAggregateRoot
{
    private Registration() { }

    public Guid EventId { get; private set; }
    public Guid OccurrenceId { get; private set; }
    public Guid? PersonId { get; private set; }
    public Guid? UserId { get; private set; }
    public string FullName { get; private set; } = null!;
    public string? Email { get; private set; }
    public string? PhoneNumber { get; private set; }
    public int Guests { get; private set; }
    public RegistrationStatus Status { get; private set; }

    /// <summary>Unguessable code rendered as a QR ticket; scanned at the door.</summary>
    public string TicketCode { get; private set; } = null!;

    public DateTimeOffset? CheckedInAt { get; private set; }
    public string? Notes { get; private set; }

    public int Seats => 1 + Guests;

    public static Registration Create(
        Guid eventId, Guid occurrenceId, Guid? personId, Guid? userId, string fullName, string? email, string? phone, int guests,
        RegistrationStatus status, string? notes) => new()
    {
        EventId = eventId,
        OccurrenceId = occurrenceId,
        PersonId = personId,
        UserId = userId,
        FullName = fullName.Trim(),
        Email = email?.Trim().ToLowerInvariant(),
        PhoneNumber = phone,
        Guests = guests,
        Status = status,
        Notes = notes,
        TicketCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)),
    };

    public void Cancel() => Status = RegistrationStatus.Cancelled;

    public void Confirm() => Status = RegistrationStatus.Confirmed;

    public void CheckIn(DateTimeOffset at)
    {
        if (Status == RegistrationStatus.Cancelled)
        {
            throw new DomainException("A cancelled registration cannot be checked in.");
        }

        Status = RegistrationStatus.CheckedIn;
        CheckedInAt ??= at;
    }
}

public enum CheckInMethod
{
    Manual,
    QrTicket,
    SelfCheckIn,
    Kiosk,
    Import,
}

/// <summary>A person's presence at an occurrence. One row per person per occurrence.</summary>
public sealed class AttendanceRecord : TenantAggregateRoot
{
    private AttendanceRecord() { }

    public Guid EventId { get; private set; }
    public Guid OccurrenceId { get; private set; }
    public Guid PersonId { get; private set; }
    public DateTimeOffset CheckedInAt { get; private set; }
    public DateTimeOffset? CheckedOutAt { get; private set; }
    public CheckInMethod Method { get; private set; }
    public bool IsFirstVisit { get; private set; }

    /// <summary>For children's check-in: the adult who dropped off / may collect.</summary>
    public Guid? GuardianPersonId { get; private set; }

    public string? Notes { get; private set; }

    public static AttendanceRecord CheckIn(
        Guid tenantId, Guid eventId, Guid occurrenceId, Guid personId, string eventTitle, DateTimeOffset at, CheckInMethod method,
        bool isFirstVisit, Guid? guardianPersonId, string? notes)
    {
        var record = new AttendanceRecord
        {
            EventId = eventId,
            OccurrenceId = occurrenceId,
            PersonId = personId,
            CheckedInAt = at,
            Method = method,
            IsFirstVisit = isFirstVisit,
            GuardianPersonId = guardianPersonId,
            Notes = notes,
        };
        record.AssignTenant(tenantId);
        record.Raise(new AttendanceRecordedIntegrationEvent(tenantId, eventId, occurrenceId, personId, eventTitle, at, isFirstVisit));
        return record;
    }

    public void CheckOut(DateTimeOffset at) => CheckedOutAt ??= at;
}

/// <summary>
/// Aggregate head count for an occurrence — how most churches count a large service.
/// Complements (does not replace) individual attendance records.
/// </summary>
public sealed class HeadCount : TenantEntity
{
    private HeadCount() { }

    public Guid OccurrenceId { get; private set; }
    public int Men { get; private set; }
    public int Women { get; private set; }
    public int Children { get; private set; }
    public int FirstTimers { get; private set; }
    public int Online { get; private set; }
    public int Total { get; private set; }
    public string? Notes { get; private set; }

    public static HeadCount Create(Guid occurrenceId) => new() { OccurrenceId = occurrenceId };

    public void Record(int men, int women, int children, int firstTimers, int online, string? notes)
    {
        if (men < 0 || women < 0 || children < 0 || firstTimers < 0 || online < 0)
        {
            throw new DomainException("Counts cannot be negative.");
        }

        Men = men;
        Women = women;
        Children = children;
        FirstTimers = firstTimers;
        Online = online;
        Total = men + women + children;
        Notes = notes;
    }
}
