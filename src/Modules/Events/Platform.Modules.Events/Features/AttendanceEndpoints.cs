using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Pagination;
using Platform.Application.Security;
using Platform.Application.Tenancy;
using Platform.Modules.Events.Domain;
using Platform.Modules.Events.Infrastructure;
using Platform.Modules.People.Contracts;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;
using Platform.Web.Security;

namespace Platform.Modules.Events.Features;

public sealed record CheckInRequest(IReadOnlyList<Guid> PersonIds, CheckInMethod Method = CheckInMethod.Manual, Guid? GuardianPersonId = null, string? Notes = null);

public sealed record CheckInResult(int CheckedIn, int AlreadyPresent, int FirstVisits);

public sealed record AttendeeResponse(Guid PersonId, string FullName, string? PhotoUrl, DateTimeOffset CheckedInAt, DateTimeOffset? CheckedOutAt,
    string Method, bool IsFirstVisit);

public sealed record HeadCountRequest(int Men, int Women, int Children, int FirstTimers, int Online, string? Notes);

public sealed record HeadCountResponse(int Men, int Women, int Children, int FirstTimers, int Online, int Total, string? Notes);

public sealed record OccurrenceAttendanceResponse(Guid OccurrenceId, string EventTitle, DateTimeOffset StartsAt, int RecordedCount,
    HeadCountResponse? HeadCount, IReadOnlyList<AttendeeResponse> Attendees);

public sealed record AttendanceSummaryRow(Guid OccurrenceId, Guid EventId, string EventTitle, string EventType, DateTimeOffset StartsAt,
    int RecordedCount, int FirstVisits, int? HeadCountTotal, int? Online);

public sealed record PersonAttendanceRow(Guid OccurrenceId, string EventTitle, DateTimeOffset StartsAt, DateTimeOffset CheckedInAt, string Method);

public sealed record SelfCheckInRequest(string Code);

internal sealed class CheckInValidator : AbstractValidator<CheckInRequest>
{
    public CheckInValidator()
    {
        RuleFor(x => x.PersonIds).NotEmpty().Must(p => p.Count <= 500).WithMessage("At most 500 people per request.");
        RuleFor(x => x.Method).IsInEnum();
    }
}

internal sealed class HeadCountValidator : AbstractValidator<HeadCountRequest>
{
    public HeadCountValidator()
    {
        RuleFor(x => x.Men).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Women).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Children).GreaterThanOrEqualTo(0);
        RuleFor(x => x.FirstTimers).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Online).GreaterThanOrEqualTo(0);
    }
}

internal sealed class RegistrationRequestValidator : AbstractValidator<RegistrationRequest>
{
    public RegistrationRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256);
        RuleFor(x => x.PhoneNumber).MaximumLength(32);
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.Email) || !string.IsNullOrWhiteSpace(x.PhoneNumber))
            .WithName("email").WithMessage("Provide an email address or phone number.");
        RuleFor(x => x.Guests).InclusiveBetween(0, 20);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

/// <summary>Registrations, check-in (manual, QR ticket, self), head counts and attendance reporting.</summary>
public static class AttendanceEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var reg = endpoints.MapModuleGroup("registrations", "Registrations");
        reg.MapGet("/occurrences/{occurrenceId:guid}", ListRegistrations).RequirePermission(Permissions.Events.Read).WithSummary("Registrations for an event date");
        reg.MapPost("/occurrences/{occurrenceId:guid}", AdminRegister).WithValidation<RegistrationRequest>()
            .RequirePermission(Permissions.Events.RegistrationsManage).WithSummary("Register someone (staff, bypasses the registration window)");
        reg.MapPost("/{id:guid}/cancel", AdminCancel).RequirePermission(Permissions.Events.RegistrationsManage).WithSummary("Cancel a registration (promotes the waitlist)");

        var att = endpoints.MapModuleGroup("attendance", "Attendance");
        att.MapGet("/occurrences/{occurrenceId:guid}", GetOccurrence).RequirePermission(Permissions.Events.AttendanceRead).WithSummary("Attendance and head count for an event date");
        att.MapPost("/occurrences/{occurrenceId:guid}/check-in", CheckIn).WithValidation<CheckInRequest>()
            .RequirePermission(Permissions.Events.AttendanceRecord).WithSummary("Check people in (bulk)");
        att.MapPost("/occurrences/{occurrenceId:guid}/check-out/{personId:guid}", CheckOut).RequirePermission(Permissions.Events.AttendanceRecord).WithSummary("Check a person out (children's ministry)");
        att.MapDelete("/occurrences/{occurrenceId:guid}/people/{personId:guid}", RemoveAttendance).RequirePermission(Permissions.Events.AttendanceRecord).WithSummary("Undo a check-in");
        att.MapPut("/occurrences/{occurrenceId:guid}/head-count", SaveHeadCount).WithValidation<HeadCountRequest>()
            .RequirePermission(Permissions.Events.AttendanceRecord).WithSummary("Record the head count");
        att.MapPost("/tickets/{ticketCode}/check-in", TicketCheckIn).RequirePermission(Permissions.Events.AttendanceRecord).WithSummary("Scan a QR ticket at the door");
        att.MapGet("/reports/summary", Summary).RequirePermission(Permissions.Events.AttendanceRead).WithSummary("Attendance per event date over a period");
        att.MapGet("/people/{personId:guid}", PersonHistory).RequirePermission(Permissions.Events.AttendanceRead).WithSummary("A person's attendance history");

        var me = endpoints.MapGroup($"{EndpointExtensions.ApiPrefix}/me").WithTags("My account").RequireAuthorization();
        me.MapGet("/registrations", MyRegistrations).WithSummary("My upcoming registrations and tickets");
        me.MapPost("/registrations/occurrences/{occurrenceId:guid}", MemberRegister).WithValidation<RegistrationRequest>().WithSummary("Register myself for an event");
        me.MapPost("/registrations/{id:guid}/cancel", MemberCancel).WithSummary("Cancel my registration");
        me.MapPost("/check-in", SelfCheckIn).WithSummary("Self check-in by scanning the venue QR code");
        me.MapGet("/attendance", MyAttendance).WithSummary("My attendance history");

        endpoints.MapPublicGroup("events", "Public")
            .MapPost("/occurrences/{occurrenceId:guid}/register", PublicRegister).WithValidation<RegistrationRequest>()
            .WithSummary("Register for a public event from the website");
    }

    // ---- Registrations ---------------------------------------------------------------------

    private static async Task<IResult> ListRegistrations(Guid occurrenceId, [AsParameters] PageRequest page, RegistrationStatus? status,
        EventsDbContext db, CancellationToken ct)
    {
        var query = from r in db.Registrations.AsNoTracking()
                    join o in db.Occurrences.AsNoTracking() on r.OccurrenceId equals o.Id
                    join e in db.Events.AsNoTracking() on r.EventId equals e.Id
                    where r.OccurrenceId == occurrenceId
                    select new { r, o.StartsAt, e.Title };
        if (status is { } s) query = query.Where(x => x.r.Status == s);

        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderBy(x => x.r.CreatedAt).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        return Results.Ok(new PagedResult<RegistrationResponse>(
            rows.Select(x => RegistrationService.ToResponse(x.r, x.Title, x.StartsAt)).ToList(), page.SafePage, page.SafePageSize, total));
    }

    private static async Task<IResult> AdminRegister(Guid occurrenceId, RegistrationRequest request, Guid? personId, RegistrationService service, CancellationToken ct) =>
        (await service.RegisterAsync(occurrenceId, request, personId, null, bypassWindow: true, ct)).ToCreated(r => $"/api/v1/registrations/{r.Id}");

    private static async Task<IResult> AdminCancel(Guid id, RegistrationService service, CancellationToken ct) =>
        (await service.CancelAsync(id, requiredUserId: null, ct)).ToHttp();

    private static async Task<IResult> PublicRegister(Guid occurrenceId, RegistrationRequest request, ICurrentUser user, IPeopleDirectory people,
        RegistrationService service, CancellationToken ct)
    {
        Guid? personId = user.UserId is { } userId ? await people.FindPersonIdByUserAsync(userId, ct) : null;
        return (await service.RegisterAsync(occurrenceId, request, personId, user.UserId, bypassWindow: false, ct))
            .ToCreated(r => $"/api/v1/me/registrations/{r.Id}");
    }

    private static Task<IResult> MemberRegister(Guid occurrenceId, RegistrationRequest request, ICurrentUser user, IPeopleDirectory people,
        RegistrationService service, CancellationToken ct) =>
        PublicRegister(occurrenceId, request, user, people, service, ct);

    private static async Task<IResult> MemberCancel(Guid id, ICurrentUser user, RegistrationService service, CancellationToken ct) =>
        (await service.CancelAsync(id, user.RequiredUserId, ct)).ToHttp();

    private static async Task<IResult> MyRegistrations(ICurrentUser user, EventsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var rows = await (from r in db.Registrations.AsNoTracking()
                          join o in db.Occurrences.AsNoTracking() on r.OccurrenceId equals o.Id
                          join e in db.Events.AsNoTracking() on r.EventId equals e.Id
                          where r.UserId == user.UserId && o.EndsAt >= now && r.Status != RegistrationStatus.Cancelled
                          orderby o.StartsAt
                          select new { r, o.StartsAt, e.Title }).ToListAsync(ct);
        return Results.Ok(rows.Select(x => RegistrationService.ToResponse(x.r, x.Title, x.StartsAt)));
    }

    // ---- Attendance ------------------------------------------------------------------------

    private static async Task<IResult> GetOccurrence(Guid occurrenceId, EventsDbContext db, IPeopleDirectory people, CancellationToken ct)
    {
        var row = await (from o in db.Occurrences.AsNoTracking()
                         join e in db.Events.AsNoTracking() on o.EventId equals e.Id
                         where o.Id == occurrenceId
                         select new { o, e.Title }).FirstOrDefaultAsync(ct);
        if (row is null)
        {
            return RegistrationErrors.OccurrenceNotFound.ToProblem();
        }

        var records = await db.Attendance.AsNoTracking().Where(a => a.OccurrenceId == occurrenceId).ToListAsync(ct);
        var names = await people.GetSummariesAsync(records.Select(r => r.PersonId), ct);
        var headCount = await db.HeadCounts.AsNoTracking().FirstOrDefaultAsync(h => h.OccurrenceId == occurrenceId, ct);

        var attendees = records
            .Select(r => new AttendeeResponse(r.PersonId, names.TryGetValue(r.PersonId, out var p) ? p.FullName : "(removed)", p?.PhotoUrl,
                r.CheckedInAt, r.CheckedOutAt, r.Method.ToString(), r.IsFirstVisit))
            .OrderBy(a => a.FullName).ToList();

        return Results.Ok(new OccurrenceAttendanceResponse(occurrenceId, row.Title, row.o.StartsAt, records.Count,
            headCount is null ? null : ToResponse(headCount), attendees));
    }

    private static async Task<IResult> CheckIn(Guid occurrenceId, CheckInRequest request, AttendanceService attendance, CancellationToken ct) =>
        (await attendance.CheckInAsync(occurrenceId, request.PersonIds, request.Method, request.GuardianPersonId, request.Notes, ct)).ToHttp();

    private static async Task<IResult> CheckOut(Guid occurrenceId, Guid personId, EventsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var record = await db.Attendance.FirstOrDefaultAsync(a => a.OccurrenceId == occurrenceId && a.PersonId == personId, ct);
        if (record is null)
        {
            return Error.NotFound("attendance.not_found", "The person is not checked in.").ToProblem();
        }

        record.CheckOut(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RemoveAttendance(Guid occurrenceId, Guid personId, EventsDbContext db, CancellationToken ct)
    {
        var record = await db.Attendance.FirstOrDefaultAsync(a => a.OccurrenceId == occurrenceId && a.PersonId == personId, ct);
        if (record is null)
        {
            return Error.NotFound("attendance.not_found", "The person is not checked in.").ToProblem();
        }

        db.Attendance.Remove(record);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SaveHeadCount(Guid occurrenceId, HeadCountRequest r, EventsDbContext db, CancellationToken ct)
    {
        if (!await db.Occurrences.AnyAsync(o => o.Id == occurrenceId, ct))
        {
            return RegistrationErrors.OccurrenceNotFound.ToProblem();
        }

        var headCount = await db.HeadCounts.FirstOrDefaultAsync(h => h.OccurrenceId == occurrenceId, ct);
        if (headCount is null)
        {
            headCount = HeadCount.Create(occurrenceId);
            db.HeadCounts.Add(headCount);
        }

        headCount.Record(r.Men, r.Women, r.Children, r.FirstTimers, r.Online, r.Notes);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(headCount));
    }

    private static async Task<IResult> TicketCheckIn(string ticketCode, EventsDbContext db, AttendanceService attendance, TimeProvider clock, CancellationToken ct)
    {
        var registration = await db.Registrations.FirstOrDefaultAsync(r => r.TicketCode == ticketCode.ToUpperInvariant(), ct);
        if (registration is null)
        {
            return RegistrationErrors.NotFound.ToProblem();
        }

        if (registration.Status is RegistrationStatus.Cancelled or RegistrationStatus.Waitlisted)
        {
            return Error.Conflict("registration.not_confirmed", $"This ticket is {registration.Status.ToString().ToLowerInvariant()}.").ToProblem();
        }

        registration.CheckIn(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);

        if (registration.PersonId is { } personId)
        {
            await attendance.CheckInAsync(registration.OccurrenceId, [personId], CheckInMethod.QrTicket, null, null, ct);
        }

        return Results.Ok(new { registration.FullName, registration.Guests, Status = registration.Status.ToString() });
    }

    private static async Task<IResult> SelfCheckIn(SelfCheckInRequest request, ICurrentUser user, IPeopleDirectory people, EventsDbContext db,
        AttendanceService attendance, TimeProvider clock, CancellationToken ct)
    {
        if (await people.FindPersonIdByUserAsync(user.RequiredUserId, ct) is not { } personId)
        {
            return Error.NotFound("person.no_profile", "No member profile is linked to your account yet.").ToProblem();
        }

        var now = clock.GetUtcNow();
        var occurrence = await db.Occurrences.AsNoTracking()
            .Where(o => o.CheckInCode == (request.Code ?? string.Empty).Trim().ToLowerInvariant() && o.Status == OccurrenceStatus.Scheduled)
            .Where(o => o.StartsAt <= now.AddHours(2) && o.EndsAt >= now.AddHours(-1))
            .FirstOrDefaultAsync(ct);
        if (occurrence is null)
        {
            return Error.Validation("checkin.invalid_code", "This code is not valid right now.").ToProblem();
        }

        return (await attendance.CheckInAsync(occurrence.Id, [personId], CheckInMethod.SelfCheckIn, null, null, ct)).ToHttp();
    }

    private static async Task<IResult> Summary(DateTimeOffset from, DateTimeOffset to, Guid? eventId, EventType? type, EventsDbContext db, CancellationToken ct)
    {
        if (to <= from || to - from > TimeSpan.FromDays(366))
        {
            return Error.Validation("report.range", "Use a range of at most one year.").ToProblem();
        }

        var query = from o in db.Occurrences.AsNoTracking()
                    join e in db.Events.AsNoTracking() on o.EventId equals e.Id
                    where o.StartsAt >= @from && o.StartsAt < to && o.Status != OccurrenceStatus.Cancelled
                    select new { o, e };
        if (eventId is { } id) query = query.Where(x => x.e.Id == id);
        if (type is { } t) query = query.Where(x => x.e.Type == t);

        var rows = await query.OrderBy(x => x.o.StartsAt)
            .Select(x => new AttendanceSummaryRow(
                x.o.Id, x.e.Id, x.e.Title, x.e.Type.ToString(), x.o.StartsAt,
                db.Attendance.Count(a => a.OccurrenceId == x.o.Id),
                db.Attendance.Count(a => a.OccurrenceId == x.o.Id && a.IsFirstVisit),
                db.HeadCounts.Where(h => h.OccurrenceId == x.o.Id).Max(h => (int?)h.Total),
                db.HeadCounts.Where(h => h.OccurrenceId == x.o.Id).Max(h => (int?)h.Online)))
            .ToListAsync(ct);

        return Results.Ok(rows);
    }

    private static async Task<IResult> PersonHistory(Guid personId, [AsParameters] PageRequest page, EventsDbContext db, CancellationToken ct) =>
        Results.Ok(await HistoryQuery(db, personId).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct));

    private static async Task<IResult> MyAttendance(ICurrentUser user, IPeopleDirectory people, EventsDbContext db, CancellationToken ct) =>
        await people.FindPersonIdByUserAsync(user.RequiredUserId, ct) is { } personId
            ? Results.Ok(await HistoryQuery(db, personId).Take(100).ToListAsync(ct))
            : Results.Ok(Array.Empty<PersonAttendanceRow>());

    private static IQueryable<PersonAttendanceRow> HistoryQuery(EventsDbContext db, Guid personId) =>
        from a in db.Attendance.AsNoTracking()
        join o in db.Occurrences.AsNoTracking() on a.OccurrenceId equals o.Id
        join e in db.Events.AsNoTracking() on a.EventId equals e.Id
        where a.PersonId == personId
        orderby o.StartsAt descending
        select new PersonAttendanceRow(o.Id, e.Title, o.StartsAt, a.CheckedInAt, a.Method.ToString());

    private static HeadCountResponse ToResponse(HeadCount h) => new(h.Men, h.Women, h.Children, h.FirstTimers, h.Online, h.Total, h.Notes);
}

/// <summary>Idempotent check-in: people already present are skipped; first-ever visits are flagged.</summary>
internal sealed class AttendanceService(EventsDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public async Task<Result<CheckInResult>> CheckInAsync(
        Guid occurrenceId, IReadOnlyCollection<Guid> personIds, CheckInMethod method, Guid? guardianPersonId, string? notes, CancellationToken ct)
    {
        var row = await (from o in db.Occurrences
                         join e in db.Events on o.EventId equals e.Id
                         where o.Id == occurrenceId
                         select new { o.Id, o.Status, EventId = e.Id, e.Title }).FirstOrDefaultAsync(ct);
        if (row is null)
        {
            return RegistrationErrors.OccurrenceNotFound;
        }

        if (row.Status == OccurrenceStatus.Cancelled)
        {
            return Error.Conflict("occurrence.cancelled", "This event date was cancelled.");
        }

        var ids = personIds.Distinct().ToList();
        var present = await db.Attendance.Where(a => a.OccurrenceId == occurrenceId && ids.Contains(a.PersonId)).Select(a => a.PersonId).ToListAsync(ct);
        var returning = await db.Attendance.Where(a => ids.Contains(a.PersonId)).Select(a => a.PersonId).Distinct().ToListAsync(ct);

        var now = clock.GetUtcNow();
        var added = 0;
        var firstVisits = 0;
        foreach (var personId in ids.Except(present))
        {
            var isFirst = !returning.Contains(personId);
            db.Attendance.Add(AttendanceRecord.CheckIn(tenant.RequiredTenantId, row.EventId, occurrenceId, personId, row.Title, now, method, isFirst, guardianPersonId, notes));
            added++;
            firstVisits += isFirst ? 1 : 0;
        }

        await db.SaveChangesAsync(ct);
        return new CheckInResult(added, present.Count, firstVisits);
    }
}
