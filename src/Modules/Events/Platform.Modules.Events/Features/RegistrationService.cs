using Microsoft.EntityFrameworkCore;
using Platform.Modules.Events.Domain;
using Platform.Modules.Events.Infrastructure;
using Platform.SharedKernel.Results;

namespace Platform.Modules.Events.Features;

public sealed record RegistrationRequest(string FullName, string? Email, string? PhoneNumber, int Guests = 0, string? Notes = null);

public sealed record RegistrationResponse(
    Guid Id, Guid EventId, Guid OccurrenceId, string EventTitle, DateTimeOffset StartsAt, Guid? PersonId, string FullName, string? Email,
    string? PhoneNumber, int Guests, string Status, string TicketCode, DateTimeOffset? CheckedInAt, DateTimeOffset CreatedAt);

internal static class RegistrationErrors
{
    public static readonly Error OccurrenceNotFound = Error.NotFound("occurrence.not_found", "The event date was not found.");
    public static readonly Error Closed = Error.Conflict("registration.closed", "Registration is not open for this event.");
    public static readonly Error Duplicate = Error.Conflict("registration.duplicate", "You are already registered for this event.");
    public static readonly Error TooManyGuests = Error.Validation("registration.too_many_guests", "Too many guests for one registration.");
    public static readonly Error NotFound = Error.NotFound("registration.not_found", "The registration was not found.");
}

/// <summary>
/// Books seats on an occurrence. A PostgreSQL advisory lock per occurrence serialises concurrent
/// bookings so capacity can never be oversold; overflow goes to the waitlist.
/// </summary>
internal sealed class RegistrationService(EventsDbContext db, TimeProvider clock)
{
    public async Task<Result<RegistrationResponse>> RegisterAsync(
        Guid occurrenceId, RegistrationRequest request, Guid? personId, Guid? userId, bool bypassWindow, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({occurrenceId.ToString()}, 0))", ct);

            var row = await (from o in db.Occurrences
                             join e in db.Events on o.EventId equals e.Id
                             where o.Id == occurrenceId
                             select new { o, e }).FirstOrDefaultAsync(ct);
            if (row is null)
            {
                return Result.Failure<RegistrationResponse>(RegistrationErrors.OccurrenceNotFound);
            }

            var (occurrence, evt) = (row.o, row.e);
            var now = clock.GetUtcNow();
            var open = evt.RegistrationEnabled && evt.Status == EventStatus.Published && occurrence.Status == OccurrenceStatus.Scheduled
                       && occurrence.EndsAt > now && (evt.RegistrationClosesAt is null || evt.RegistrationClosesAt > now);
            if (!open && !bypassWindow)
            {
                return Result.Failure<RegistrationResponse>(RegistrationErrors.Closed);
            }

            if (request.Guests > evt.MaxGuestsPerRegistration && !bypassWindow)
            {
                return Result.Failure<RegistrationResponse>(RegistrationErrors.TooManyGuests);
            }

            var email = request.Email?.Trim().ToLowerInvariant();
            var duplicate = await db.Registrations.AnyAsync(r =>
                r.OccurrenceId == occurrenceId && r.Status != RegistrationStatus.Cancelled &&
                ((personId != null && r.PersonId == personId) || (email != null && r.Email == email)), ct);
            if (duplicate)
            {
                return Result.Failure<RegistrationResponse>(RegistrationErrors.Duplicate);
            }

            var status = RegistrationStatus.Confirmed;
            if (evt.Capacity is { } capacity)
            {
                var taken = await db.Registrations
                    .Where(r => r.OccurrenceId == occurrenceId && (r.Status == RegistrationStatus.Confirmed || r.Status == RegistrationStatus.CheckedIn))
                    .SumAsync(r => 1 + r.Guests, ct);
                if (taken + 1 + request.Guests > capacity)
                {
                    status = RegistrationStatus.Waitlisted;
                }
            }

            var registration = Registration.Create(evt.Id, occurrenceId, personId, userId, request.FullName, email, request.PhoneNumber,
                request.Guests, status, request.Notes);
            db.Registrations.Add(registration);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Result.Success(ToResponse(registration, evt.Title, occurrence.StartsAt));
        });
    }

    /// <summary>Cancels and promotes the earliest waitlisted registrations that now fit.</summary>
    public async Task<Result> CancelAsync(Guid registrationId, Guid? requiredUserId, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            var registration = await db.Registrations.FirstOrDefaultAsync(r => r.Id == registrationId, ct);
            if (registration is null || (requiredUserId is not null && registration.UserId != requiredUserId))
            {
                return Result.Failure(RegistrationErrors.NotFound);
            }

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({registration.OccurrenceId.ToString()}, 0))", ct);

            registration.Cancel();
            await db.SaveChangesAsync(ct);

            var capacity = await db.Events.Where(e => e.Id == registration.EventId).Select(e => e.Capacity).FirstAsync(ct);
            if (capacity is { } cap)
            {
                var taken = await db.Registrations
                    .Where(r => r.OccurrenceId == registration.OccurrenceId && (r.Status == RegistrationStatus.Confirmed || r.Status == RegistrationStatus.CheckedIn))
                    .SumAsync(r => 1 + r.Guests, ct);
                var waitlist = await db.Registrations
                    .Where(r => r.OccurrenceId == registration.OccurrenceId && r.Status == RegistrationStatus.Waitlisted)
                    .OrderBy(r => r.CreatedAt).ToListAsync(ct);
                foreach (var next in waitlist.TakeWhile(w => (taken += w.Seats) <= cap))
                {
                    next.Confirm();
                }

                await db.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);
            return Result.Success();
        });
    }

    public static RegistrationResponse ToResponse(Registration r, string eventTitle, DateTimeOffset startsAt) => new(
        r.Id, r.EventId, r.OccurrenceId, eventTitle, startsAt, r.PersonId, r.FullName, r.Email, r.PhoneNumber, r.Guests, r.Status.ToString(),
        r.TicketCode, r.CheckedInAt, r.CreatedAt);
}
