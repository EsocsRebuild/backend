using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Pagination;
using Platform.Application.Security;
using Platform.Modules.Events.Domain;
using Platform.Modules.Events.Infrastructure;
using Platform.SharedKernel.Domain;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;
using Platform.Web.Security;

namespace Platform.Modules.Events.Features;

public sealed record EventResponse(
    Guid Id, string Title, string Slug, string Type, string Status, string Visibility, string? Summary, string? Description,
    string? CoverImageUrl, Guid? BranchId, Guid? GroupId, string? Location, bool IsOnline, string? OnlineUrl,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, string TimeZone, bool AllDay, string? RecurrenceRule,
    bool RegistrationEnabled, int? Capacity, DateTimeOffset? RegistrationClosesAt, int MaxGuestsPerRegistration);

public sealed record OccurrenceResponse(Guid Id, Guid EventId, string EventTitle, string EventType, DateTimeOffset StartsAt,
    DateTimeOffset EndsAt, string Status, string? Location, Guid? BranchId);

public sealed record SaveEventRequest(
    string Title, string? Slug, EventType Type, EventVisibility Visibility, string? Summary, string? Description, string? CoverImageUrl,
    Guid? BranchId, Guid? GroupId, string? Location, bool IsOnline, string? OnlineUrl, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    string TimeZone, bool AllDay, string? RecurrenceRule, bool RegistrationEnabled, int? Capacity, DateTimeOffset? RegistrationClosesAt,
    int MaxGuestsPerRegistration = 0);

public sealed record EventQuery(int Page = 1, int PageSize = 25, string? Search = null, EventType? Type = null, EventStatus? Status = null,
    Guid? BranchId = null, bool Upcoming = false);

public sealed record CalendarQuery(DateTimeOffset From, DateTimeOffset To, Guid? BranchId = null, EventType? Type = null);

public sealed record PublicEventResponse(
    Guid Id, string Title, string Slug, string Type, string? Summary, string? Description, string? CoverImageUrl, string? Location,
    bool IsOnline, string? OnlineUrl, string TimeZone, bool AllDay, bool RegistrationEnabled, IReadOnlyList<PublicOccurrenceResponse> Upcoming);

public sealed record PublicOccurrenceResponse(Guid Id, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int? SeatsRemaining);

internal sealed class SaveEventValidator : AbstractValidator<SaveEventRequest>
{
    public SaveEventValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).Must(s => s is null || Slug.IsValid(s)).WithMessage("Use lowercase letters, digits and hyphens.");
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Visibility).IsInEnum();
        RuleFor(x => x.Summary).MaximumLength(500);
        RuleFor(x => x.Description).MaximumLength(20_000);
        RuleFor(x => x.Location).MaximumLength(300);
        RuleFor(x => x.EndsAt).GreaterThan(x => x.StartsAt).WithMessage("The event must end after it starts.");
        RuleFor(x => x.TimeZone).NotEmpty().Must(tz => TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _)).WithMessage("Unknown IANA time zone.");
        RuleFor(x => x.RecurrenceRule).Must(r => r is null || Recurrence.TryParse(r, out _, out _)).WithMessage("Invalid recurrence rule.");
        RuleFor(x => x.Capacity).GreaterThan(0).When(x => x.Capacity.HasValue);
        RuleFor(x => x.MaxGuestsPerRegistration).InclusiveBetween(0, 20);
    }
}

/// <summary>Calendar management: services, meetings, conferences and their occurrences.</summary>
public static class EventEndpoints
{
    internal static readonly Error NotFound = Error.NotFound("event.not_found", "The event was not found.");
    private static readonly Error SlugTaken = Error.Conflict("event.slug_taken", "Another event already uses this URL.");

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapModuleGroup("events", "Events & services");
        group.MapGet("/", List).RequirePermission(Permissions.Events.Read).WithSummary("List events");
        group.MapGet("/calendar", Calendar).RequirePermission(Permissions.Events.Read).WithSummary("Occurrences in a date range (calendar view)");
        group.MapGet("/{id:guid}", Get).RequirePermission(Permissions.Events.Read).WithSummary("Get an event");
        group.MapPost("/", Create).WithValidation<SaveEventRequest>().RequirePermission(Permissions.Events.Write).WithSummary("Create an event (draft)");
        group.MapPut("/{id:guid}", Update).WithValidation<SaveEventRequest>().RequirePermission(Permissions.Events.Write).WithSummary("Update an event");
        group.MapPost("/{id:guid}/publish", Publish).RequirePermission(Permissions.Events.Write).WithSummary("Publish an event");
        group.MapPost("/{id:guid}/cancel", Cancel).RequirePermission(Permissions.Events.Write).WithSummary("Cancel an event and its upcoming occurrences");
        group.MapDelete("/{id:guid}", Delete).RequirePermission(Permissions.Events.Delete).WithSummary("Archive an event");
        group.MapGet("/{id:guid}/occurrences", Occurrences).RequirePermission(Permissions.Events.Read).WithSummary("Occurrences of an event");

        var pub = endpoints.MapPublicGroup("events", "Public");
        pub.MapGet("/", PublicList).WithSummary("Upcoming public events for the website / app");
        pub.MapGet("/{slug}", PublicGet).WithSummary("Public event detail by slug");
    }

    internal static EventResponse ToResponse(this Event e) => new(
        e.Id, e.Title, e.Slug, e.Type.ToString(), e.Status.ToString(), e.Visibility.ToString(), e.Summary, e.Description, e.CoverImageUrl,
        e.BranchId, e.GroupId, e.Location, e.IsOnline, e.OnlineUrl, e.StartsAt, e.EndsAt, e.TimeZone, e.AllDay, e.RecurrenceRule,
        e.RegistrationEnabled, e.Capacity, e.RegistrationClosesAt, e.MaxGuestsPerRegistration);

    private static EventDetails ToDetails(SaveEventRequest r, string slug) => new(
        r.Title, slug, r.Type, r.Visibility, r.Summary, r.Description, r.CoverImageUrl, r.BranchId, r.GroupId, r.Location, r.IsOnline,
        r.OnlineUrl, r.StartsAt.ToUniversalTime(), r.EndsAt.ToUniversalTime(), r.TimeZone, r.AllDay, r.RecurrenceRule, r.RegistrationEnabled,
        r.Capacity, r.RegistrationClosesAt, r.MaxGuestsPerRegistration);

    private static async Task<IResult> List([AsParameters] EventQuery q, EventsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var page = new PageRequest(q.Page, q.PageSize);
        var query = db.Events.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Search)) query = query.Where(e => EF.Functions.ILike(e.Title, $"%{q.Search.Trim()}%"));
        if (q.Type is { } type) query = query.Where(e => e.Type == type);
        if (q.Status is { } status) query = query.Where(e => e.Status == status);
        if (q.BranchId is { } branchId) query = query.Where(e => e.BranchId == branchId);
        if (q.Upcoming)
        {
            var now = clock.GetUtcNow();
            query = query.Where(e => e.RecurrenceRule != null || e.EndsAt >= now);
        }

        var total = await query.LongCountAsync(ct);
        var items = await query.OrderBy(e => e.StartsAt).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        return Results.Ok(new PagedResult<EventResponse>(items.Select(e => e.ToResponse()).ToList(), page.SafePage, page.SafePageSize, total));
    }

    private static async Task<IResult> Calendar([AsParameters] CalendarQuery q, EventsDbContext db, CancellationToken ct)
    {
        if (q.To <= q.From || q.To - q.From > TimeSpan.FromDays(92))
        {
            return Error.Validation("calendar.range", "Use a range of at most 92 days.").ToProblem();
        }

        var query = from o in db.Occurrences.AsNoTracking()
                    join e in db.Events.AsNoTracking() on o.EventId equals e.Id
                    where o.StartsAt < q.To && o.EndsAt > q.From && e.Status != EventStatus.Draft
                    select new { o, e };
        if (q.BranchId is { } branchId) query = query.Where(x => x.e.BranchId == branchId);
        if (q.Type is { } type) query = query.Where(x => x.e.Type == type);

        return Results.Ok(await query.OrderBy(x => x.o.StartsAt)
            .Select(x => new OccurrenceResponse(x.o.Id, x.e.Id, x.e.Title, x.e.Type.ToString(), x.o.StartsAt, x.o.EndsAt, x.o.Status.ToString(), x.e.Location, x.e.BranchId))
            .ToListAsync(ct));
    }

    private static async Task<IResult> Get(Guid id, EventsDbContext db, CancellationToken ct) =>
        await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct) is { } e ? Results.Ok(e.ToResponse()) : NotFound.ToProblem();

    private static async Task<IResult> Create(SaveEventRequest r, EventsDbContext db, OccurrenceSync sync, CancellationToken ct)
    {
        var slug = r.Slug ?? Slug.From(r.Title);
        if (await db.Events.AnyAsync(e => e.Slug == slug, ct))
        {
            slug = $"{slug}-{r.StartsAt:yyyyMMdd}";
            if (await db.Events.AnyAsync(e => e.Slug == slug, ct))
            {
                return SlugTaken.ToProblem();
            }
        }

        var entity = Event.Create(r.Title, slug, r.Type);
        entity.Update(ToDetails(r, slug));
        db.Events.Add(entity);
        await sync.SyncAsync(entity, ct);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/events/{entity.Id}", entity.ToResponse());
    }

    private static async Task<IResult> Update(Guid id, SaveEventRequest r, EventsDbContext db, OccurrenceSync sync, CancellationToken ct)
    {
        var entity = await db.Events.Include(e => e.Occurrences).FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null)
        {
            return NotFound.ToProblem();
        }

        var slug = r.Slug ?? entity.Slug;
        if (await db.Events.AnyAsync(e => e.Slug == slug && e.Id != id, ct))
        {
            return SlugTaken.ToProblem();
        }

        entity.Update(ToDetails(r, slug));
        await sync.SyncAsync(entity, ct);
        await db.SaveChangesAsync(ct);
        return Results.Ok(entity.ToResponse());
    }

    private static async Task<IResult> Publish(Guid id, EventsDbContext db, CancellationToken ct) =>
        await Transition(id, db, e => e.Publish(), ct);

    private static async Task<IResult> Cancel(Guid id, EventsDbContext db, CancellationToken ct) =>
        await Transition(id, db, e => e.Cancel(), ct);

    private static async Task<IResult> Transition(Guid id, EventsDbContext db, Action<Event> action, CancellationToken ct)
    {
        var entity = await db.Events.Include(e => e.Occurrences).FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null)
        {
            return NotFound.ToProblem();
        }

        action(entity);
        await db.SaveChangesAsync(ct);
        return Results.Ok(entity.ToResponse());
    }

    private static async Task<IResult> Delete(Guid id, EventsDbContext db, CancellationToken ct)
    {
        var entity = await db.Events.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null)
        {
            return NotFound.ToProblem();
        }

        db.Events.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Occurrences(Guid id, DateTimeOffset? from, DateTimeOffset? to, EventsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var start = from ?? clock.GetUtcNow().AddDays(-30);
        var end = to ?? clock.GetUtcNow().Add(OccurrenceScheduler.Horizon);
        var e = await db.Events.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null)
        {
            return NotFound.ToProblem();
        }

        return Results.Ok(await db.Occurrences.AsNoTracking()
            .Where(o => o.EventId == id && o.StartsAt >= start && o.StartsAt <= end)
            .OrderBy(o => o.StartsAt)
            .Select(o => new OccurrenceResponse(o.Id, e.Id, e.Title, e.Type.ToString(), o.StartsAt, o.EndsAt, o.Status.ToString(), e.Location, e.BranchId))
            .ToListAsync(ct));
    }

    private static async Task<IResult> PublicList(HttpContext http, int? days, EventType? type, EventsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var until = now.AddDays(Math.Clamp(days ?? 60, 1, 180));
        var query = from o in db.Occurrences.AsNoTracking()
                    join e in db.Events.AsNoTracking() on o.EventId equals e.Id
                    where e.Status == EventStatus.Published && e.Visibility == EventVisibility.Public
                          && o.Status == OccurrenceStatus.Scheduled && o.EndsAt >= now && o.StartsAt <= until
                    select new { o, e };
        if (type is { } t) query = query.Where(x => x.e.Type == t);

        http.Response.Headers.CacheControl = "public, max-age=60";
        return Results.Ok(await query.OrderBy(x => x.o.StartsAt).Take(200)
            .Select(x => new OccurrenceResponse(x.o.Id, x.e.Id, x.e.Title, x.e.Type.ToString(), x.o.StartsAt, x.o.EndsAt, x.o.Status.ToString(), x.e.Location, x.e.BranchId))
            .ToListAsync(ct));
    }

    private static async Task<IResult> PublicGet(string slug, HttpContext http, EventsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var e = await db.Events.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Slug == slug && x.Status == EventStatus.Published && x.Visibility == EventVisibility.Public, ct);
        if (e is null)
        {
            return NotFound.ToProblem();
        }

        var now = clock.GetUtcNow();
        var occurrences = await db.Occurrences.AsNoTracking()
            .Where(o => o.EventId == e.Id && o.EndsAt >= now && o.Status == OccurrenceStatus.Scheduled)
            .OrderBy(o => o.StartsAt).Take(12)
            .Select(o => new
            {
                o.Id, o.StartsAt, o.EndsAt,
                Taken = db.Registrations.Where(r => r.OccurrenceId == o.Id && (r.Status == RegistrationStatus.Confirmed || r.Status == RegistrationStatus.CheckedIn))
                    .Sum(r => 1 + r.Guests),
            })
            .ToListAsync(ct);

        http.Response.Headers.CacheControl = "public, max-age=60";
        return Results.Ok(new PublicEventResponse(
            e.Id, e.Title, e.Slug, e.Type.ToString(), e.Summary, e.Description, e.CoverImageUrl, e.Location, e.IsOnline, e.OnlineUrl,
            e.TimeZone, e.AllDay, e.RegistrationEnabled,
            occurrences.Select(o => new PublicOccurrenceResponse(o.Id, o.StartsAt, o.EndsAt, e.Capacity is { } cap ? Math.Max(0, cap - o.Taken) : null)).ToList()));
    }
}
