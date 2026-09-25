using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Pagination;
using Platform.Application.Security;
using Platform.Modules.Communications.Domain;
using Platform.Modules.Communications.Infrastructure;
using Platform.Modules.Groups.Contracts;
using Platform.Modules.People.Contracts;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;
using Platform.Web.Security;

namespace Platform.Modules.Communications.Features;

public sealed record SaveAnnouncementRequest(string Title, string Body, string? ImageUrl, string? LinkUrl, AnnouncementAudience Audience,
    Guid? GroupId, Guid? BranchId, DateTimeOffset? PublishAt, DateTimeOffset? ExpiresAt, bool IsPinned);

public sealed record AnnouncementResponse(Guid Id, string Title, string Body, string? ImageUrl, string? LinkUrl, string Audience, Guid? GroupId,
    Guid? BranchId, DateTimeOffset PublishAt, DateTimeOffset? ExpiresAt, bool IsPinned, string Status);

public sealed record SubmitPrayerRequest(string Name, string? Email, string? PhoneNumber, string Request, bool IsAnonymous, bool ShareOnPrayerWall);

public sealed record ManagePrayerRequest(PrayerStatus Status, Guid? AssignedToUserId, bool ApprovedForWall, string? AnswerNote);

public sealed record PrayerResponse(Guid Id, string Name, string? Email, string? PhoneNumber, string Request, bool IsAnonymous, bool ShareOnPrayerWall,
    bool ApprovedForWall, string Status, Guid? AssignedToUserId, int PrayedCount, string? AnswerNote, DateTimeOffset CreatedAt);

public sealed record PrayerWallItem(Guid Id, string Name, string Request, string Status, int PrayedCount, string? AnswerNote, DateTimeOffset CreatedAt);

public sealed record SaveTemplateRequest(string Name, Channel Channel, string? Subject, string Body);

public sealed record SaveBroadcastRequest(Channel Channel, string? Subject, string Body, AudienceSpec Audience);

public sealed record SendBroadcastRequest(DateTimeOffset? ScheduledFor);

public sealed record BroadcastResponse(Guid Id, string Channel, string? Subject, string Body, AudienceSpec Audience, string Status,
    DateTimeOffset? ScheduledFor, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, int RecipientCount, int SentCount, int FailedCount,
    int SkippedCount, DateTimeOffset CreatedAt);

public sealed record RegisterDeviceRequest(DevicePlatform Platform, string Token, string? AppVersion);

internal sealed class SaveAnnouncementValidator : AbstractValidator<SaveAnnouncementRequest>
{
    public SaveAnnouncementValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(10_000);
        RuleFor(x => x.Audience).IsInEnum();
        RuleFor(x => x.GroupId).NotNull().When(x => x.Audience == AnnouncementAudience.Group).WithMessage("Choose a group.");
    }
}

internal sealed class SubmitPrayerValidator : AbstractValidator<SubmitPrayerRequest>
{
    public SubmitPrayerValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256);
        RuleFor(x => x.PhoneNumber).MaximumLength(32);
        RuleFor(x => x.Request).NotEmpty().MaximumLength(4000);
    }
}

internal sealed class SaveBroadcastValidator : AbstractValidator<SaveBroadcastRequest>
{
    public SaveBroadcastValidator()
    {
        RuleFor(x => x.Channel).IsInEnum();
        RuleFor(x => x.Subject).NotEmpty().When(x => x.Channel is Channel.Email or Channel.Push or Channel.InApp).MaximumLength(200);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(50_000);
        RuleFor(x => x.Body).MaximumLength(1600).When(x => x.Channel == Channel.Sms).WithMessage("SMS messages are limited to 1600 characters.");
        RuleFor(x => x.Audience).NotNull();
    }
}

internal sealed class SaveTemplateValidator : AbstractValidator<SaveTemplateRequest>
{
    public SaveTemplateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Channel).IsInEnum();
        RuleFor(x => x.Body).NotEmpty().MaximumLength(50_000);
    }
}

/// <summary>Announcements, prayer ministry, bulk messaging, templates, devices and the in-app inbox.</summary>
public static class CommunicationsEndpoints
{
    private static readonly Error NotFound = Error.NotFound("comms.not_found", "The item was not found.");

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var ann = endpoints.MapModuleGroup("announcements", "Communications");
        ann.MapGet("/", ListAnnouncements).RequirePermission(Permissions.Communications.Read).WithSummary("List announcements");
        ann.MapPost("/", (SaveAnnouncementRequest r, CommunicationsDbContext db, TimeProvider clock, CancellationToken ct) => SaveAnnouncement(null, r, db, clock, ct))
            .WithValidation<SaveAnnouncementRequest>().RequirePermission(Permissions.Communications.AnnouncementsManage).WithSummary("Create an announcement (draft)");
        ann.MapPut("/{id:guid}", (Guid id, SaveAnnouncementRequest r, CommunicationsDbContext db, TimeProvider clock, CancellationToken ct) => SaveAnnouncement(id, r, db, clock, ct))
            .WithValidation<SaveAnnouncementRequest>().RequirePermission(Permissions.Communications.AnnouncementsManage).WithSummary("Update an announcement");
        ann.MapPost("/{id:guid}/publish", (Guid id, CommunicationsDbContext db, CancellationToken ct) => AnnouncementAction(id, db, a => a.Publish(), ct))
            .RequirePermission(Permissions.Communications.AnnouncementsManage).WithSummary("Publish");
        ann.MapPost("/{id:guid}/archive", (Guid id, CommunicationsDbContext db, CancellationToken ct) => AnnouncementAction(id, db, a => a.Archive(), ct))
            .RequirePermission(Permissions.Communications.AnnouncementsManage).WithSummary("Archive");

        var prayer = endpoints.MapModuleGroup("prayer-requests", "Prayer ministry");
        prayer.MapGet("/", ListPrayer).RequirePermission(Permissions.Communications.PrayerRequestsRead).WithSummary("Prayer requests (confidential)");
        prayer.MapPut("/{id:guid}", ManagePrayer).RequirePermission(Permissions.Communications.PrayerRequestsManage).WithSummary("Update status, assignee, prayer-wall approval");

        var templates = endpoints.MapModuleGroup("message-templates", "Communications");
        templates.MapGet("/", async (CommunicationsDbContext db, CancellationToken ct) => Results.Ok(await db.Templates.AsNoTracking().OrderBy(t => t.Name)
                .Select(t => new { t.Id, t.Name, Channel = t.Channel.ToString(), t.Subject, t.Body }).ToListAsync(ct)))
            .RequirePermission(Permissions.Communications.Read).WithSummary("List templates");
        templates.MapPost("/", SaveTemplate).WithValidation<SaveTemplateRequest>().RequirePermission(Permissions.Communications.TemplatesManage).WithSummary("Create a template");

        var broadcasts = endpoints.MapModuleGroup("broadcasts", "Communications");
        broadcasts.MapGet("/", ListBroadcasts).RequirePermission(Permissions.Communications.Read).WithSummary("Email / SMS / push broadcasts");
        broadcasts.MapPost("/", (SaveBroadcastRequest r, CommunicationsDbContext db, CancellationToken ct) => SaveBroadcast(null, r, db, ct))
            .WithValidation<SaveBroadcastRequest>().RequirePermission(Permissions.Communications.Send).WithSummary("Create a draft broadcast");
        broadcasts.MapPut("/{id:guid}", (Guid id, SaveBroadcastRequest r, CommunicationsDbContext db, CancellationToken ct) => SaveBroadcast(id, r, db, ct))
            .WithValidation<SaveBroadcastRequest>().RequirePermission(Permissions.Communications.Send).WithSummary("Edit a draft broadcast");
        broadcasts.MapGet("/{id:guid}/audience-preview", PreviewAudience).RequirePermission(Permissions.Communications.Send).WithSummary("Count reachable recipients before sending");
        broadcasts.MapPost("/{id:guid}/send", SendBroadcast).RequirePermission(Permissions.Communications.Send).WithSummary("Send now or schedule");
        broadcasts.MapPost("/{id:guid}/cancel", (Guid id, CommunicationsDbContext db, CancellationToken ct) => BroadcastAction(id, db, b => b.Cancel(), ct))
            .RequirePermission(Permissions.Communications.Send).WithSummary("Cancel a scheduled broadcast");
        broadcasts.MapGet("/{id:guid}/deliveries", ListDeliveries).RequirePermission(Permissions.Communications.Read).WithSummary("Per-recipient delivery status");

        var pub = endpoints.MapPublicGroup("", "Public");
        pub.MapGet("/announcements", PublicAnnouncements).WithSummary("Current public announcements");
        pub.MapPost("/prayer-requests", SubmitPrayer).WithValidation<SubmitPrayerRequest>().WithSummary("Submit a prayer request");
        pub.MapGet("/prayer-wall", PrayerWall).WithSummary("Approved public prayer requests");
        pub.MapPost("/prayer-wall/{id:guid}/pray", PrayFor).WithSummary("\"I prayed\" counter");

        var me = endpoints.MapGroup($"{EndpointExtensions.ApiPrefix}/me").WithTags("My account").RequireAuthorization();
        me.MapGet("/announcements", MyAnnouncements).WithSummary("Announcements for me (public, members and my groups)");
        me.MapGet("/prayer-requests", MyPrayerRequests).WithSummary("My prayer requests");
        me.MapPost("/devices", RegisterDevice).WithSummary("Register a push notification token");
        me.MapDelete("/devices/{token}", UnregisterDevice).WithSummary("Unregister a push token (on sign-out)");
        me.MapGet("/notifications", MyNotifications).WithSummary("My in-app notifications");
        me.MapPost("/notifications/{id:guid}/read", MarkRead).WithSummary("Mark a notification read");
        me.MapPost("/notifications/read-all", MarkAllRead).WithSummary("Mark all notifications read");
    }

    // ---- Announcements ---------------------------------------------------------------------

    private static AnnouncementResponse ToResponse(Announcement a) => new(a.Id, a.Title, a.Body, a.ImageUrl, a.LinkUrl, a.Audience.ToString(),
        a.GroupId, a.BranchId, a.PublishAt, a.ExpiresAt, a.IsPinned, a.Status.ToString());

    private static async Task<IResult> ListAnnouncements([AsParameters] PageRequest page, AnnouncementStatus? status, CommunicationsDbContext db, CancellationToken ct)
    {
        var query = db.Announcements.AsNoTracking();
        if (status is { } s) query = query.Where(a => a.Status == s);
        var total = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(a => a.PublishAt).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        return Results.Ok(new PagedResult<AnnouncementResponse>(items.Select(ToResponse).ToList(), page.SafePage, page.SafePageSize, total));
    }

    private static async Task<IResult> SaveAnnouncement(Guid? id, SaveAnnouncementRequest r, CommunicationsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var item = id is null ? Announcement.Create(r.Title, r.Body) : await db.Announcements.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (item is null)
        {
            return NotFound.ToProblem();
        }

        if (id is null)
        {
            db.Announcements.Add(item);
        }

        item.Update(r.Title, r.Body, r.ImageUrl, r.LinkUrl, r.Audience, r.GroupId, r.BranchId, r.PublishAt ?? clock.GetUtcNow(), r.ExpiresAt, r.IsPinned);
        await db.SaveChangesAsync(ct);
        return id is null ? Results.Created($"/api/v1/announcements/{item.Id}", ToResponse(item)) : Results.Ok(ToResponse(item));
    }

    private static async Task<IResult> AnnouncementAction(Guid id, CommunicationsDbContext db, Action<Announcement> action, CancellationToken ct)
    {
        var item = await db.Announcements.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (item is null)
        {
            return NotFound.ToProblem();
        }

        action(item);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(item));
    }

    private static IQueryable<Announcement> Current(CommunicationsDbContext db, DateTimeOffset now) =>
        db.Announcements.AsNoTracking().Where(a => a.Status == AnnouncementStatus.Published && a.PublishAt <= now && (a.ExpiresAt == null || a.ExpiresAt > now));

    private static async Task<IResult> PublicAnnouncements(HttpContext http, CommunicationsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "public, max-age=60";
        return Results.Ok((await Current(db, clock.GetUtcNow()).Where(a => a.Audience == AnnouncementAudience.Public)
            .OrderByDescending(a => a.IsPinned).ThenByDescending(a => a.PublishAt).Take(50).ToListAsync(ct)).Select(ToResponse));
    }

    private static async Task<IResult> MyAnnouncements(ICurrentUser user, IPeopleDirectory people, IGroupDirectory groups, CommunicationsDbContext db,
        TimeProvider clock, CancellationToken ct)
    {
        IReadOnlyList<Guid> myGroups = [];
        if (await people.FindPersonIdByUserAsync(user.RequiredUserId, ct) is { } personId)
        {
            myGroups = await groups.GetGroupIdsForPersonAsync(personId, ct);
        }

        return Results.Ok((await Current(db, clock.GetUtcNow())
            .Where(a => a.Audience != AnnouncementAudience.Group || (a.GroupId != null && myGroups.Contains(a.GroupId.Value)))
            .OrderByDescending(a => a.IsPinned).ThenByDescending(a => a.PublishAt).Take(100).ToListAsync(ct)).Select(ToResponse));
    }

    // ---- Prayer ----------------------------------------------------------------------------

    private static PrayerResponse ToResponse(PrayerRequest p) => new(p.Id, p.Name, p.Email, p.PhoneNumber, p.Request, p.IsAnonymous, p.ShareOnPrayerWall,
        p.ApprovedForWall, p.Status.ToString(), p.AssignedToUserId, p.PrayedCount, p.AnswerNote, p.CreatedAt);

    private static async Task<IResult> SubmitPrayer(SubmitPrayerRequest r, ICurrentUser user, IPeopleDirectory people, CommunicationsDbContext db, CancellationToken ct)
    {
        Guid? personId = user.UserId is { } uid ? await people.FindPersonIdByUserAsync(uid, ct) : null;
        var request = PrayerRequest.Submit(personId, user.UserId, r.Name, r.Email, r.PhoneNumber, r.Request, r.IsAnonymous, r.ShareOnPrayerWall);
        db.PrayerRequests.Add(request);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/me/prayer-requests/{request.Id}", new { request.Id, Status = request.Status.ToString() });
    }

    private static async Task<IResult> ListPrayer([AsParameters] PageRequest page, PrayerStatus? status, bool? mine, ICurrentUser user,
        CommunicationsDbContext db, CancellationToken ct)
    {
        var query = db.PrayerRequests.AsNoTracking();
        if (status is { } s) query = query.Where(p => p.Status == s);
        if (mine == true) query = query.Where(p => p.AssignedToUserId == user.UserId);
        var total = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(p => p.CreatedAt).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        return Results.Ok(new PagedResult<PrayerResponse>(items.Select(ToResponse).ToList(), page.SafePage, page.SafePageSize, total));
    }

    private static async Task<IResult> ManagePrayer(Guid id, ManagePrayerRequest r, CommunicationsDbContext db, CancellationToken ct)
    {
        var request = await db.PrayerRequests.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (request is null)
        {
            return NotFound.ToProblem();
        }

        request.Manage(r.Status, r.AssignedToUserId, r.ApprovedForWall, r.AnswerNote);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(request));
    }

    private static async Task<IResult> PrayerWall(HttpContext http, CommunicationsDbContext db, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "public, max-age=60";
        var items = await db.PrayerRequests.AsNoTracking()
            .Where(p => p.ApprovedForWall && p.Status != PrayerStatus.Archived)
            .OrderByDescending(p => p.CreatedAt).Take(100).ToListAsync(ct);
        return Results.Ok(items.Select(p => new PrayerWallItem(p.Id, p.DisplayName, p.Request, p.Status.ToString(), p.PrayedCount, p.AnswerNote, p.CreatedAt)));
    }

    private static async Task<IResult> PrayFor(Guid id, CommunicationsDbContext db, CancellationToken ct)
    {
        var updated = await db.PrayerRequests.Where(p => p.Id == id && p.ApprovedForWall)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PrayedCount, p => p.PrayedCount + 1), ct);
        return updated == 0 ? NotFound.ToProblem() : Results.NoContent();
    }

    private static async Task<IResult> MyPrayerRequests(ICurrentUser user, CommunicationsDbContext db, CancellationToken ct) =>
        Results.Ok((await db.PrayerRequests.AsNoTracking().Where(p => p.UserId == user.UserId).OrderByDescending(p => p.CreatedAt).Take(100).ToListAsync(ct))
            .Select(ToResponse));

    // ---- Templates & broadcasts ------------------------------------------------------------

    private static async Task<IResult> SaveTemplate(SaveTemplateRequest r, CommunicationsDbContext db, CancellationToken ct)
    {
        var template = MessageTemplate.Create(r.Name, r.Channel, r.Subject, r.Body);
        db.Templates.Add(template);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/message-templates/{template.Id}", new { template.Id });
    }

    private static BroadcastResponse ToResponse(Broadcast b) => new(b.Id, b.Channel.ToString(), b.Subject, b.Body,
        JsonSerializer.Deserialize<AudienceSpec>(b.Audience, JsonSerializerOptions.Web) ?? new AudienceSpec(), b.Status.ToString(), b.ScheduledFor,
        b.StartedAt, b.CompletedAt, b.RecipientCount, b.SentCount, b.FailedCount, b.SkippedCount, b.CreatedAt);

    private static async Task<IResult> ListBroadcasts([AsParameters] PageRequest page, CommunicationsDbContext db, CancellationToken ct)
    {
        var total = await db.Broadcasts.LongCountAsync(ct);
        var items = await db.Broadcasts.AsNoTracking().OrderByDescending(b => b.CreatedAt).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        return Results.Ok(new PagedResult<BroadcastResponse>(items.Select(ToResponse).ToList(), page.SafePage, page.SafePageSize, total));
    }

    private static async Task<IResult> SaveBroadcast(Guid? id, SaveBroadcastRequest r, CommunicationsDbContext db, CancellationToken ct)
    {
        var audience = JsonSerializer.Serialize(r.Audience, JsonSerializerOptions.Web);
        Broadcast? broadcast;
        if (id is null)
        {
            broadcast = Broadcast.Create(r.Channel, r.Subject, r.Body, audience);
            db.Broadcasts.Add(broadcast);
        }
        else
        {
            broadcast = await db.Broadcasts.FirstOrDefaultAsync(b => b.Id == id, ct);
            if (broadcast is null)
            {
                return NotFound.ToProblem();
            }

            broadcast.Update(r.Channel, r.Subject, r.Body, audience);
        }

        await db.SaveChangesAsync(ct);
        return id is null ? Results.Created($"/api/v1/broadcasts/{broadcast.Id}", ToResponse(broadcast)) : Results.Ok(ToResponse(broadcast));
    }

    private static async Task<IResult> PreviewAudience(Guid id, CommunicationsDbContext db, IPeopleDirectory people, IGroupDirectory groups, CancellationToken ct)
    {
        var broadcast = await db.Broadcasts.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (broadcast is null)
        {
            return NotFound.ToProblem();
        }

        var spec = JsonSerializer.Deserialize<AudienceSpec>(broadcast.Audience, JsonSerializerOptions.Web) ?? new AudienceSpec();
        var personIds = spec.PersonIds?.ToList();
        if (spec.GroupId is { } groupId)
        {
            var members = await groups.GetMemberPersonIdsAsync(groupId, true, ct);
            personIds = personIds is { Count: > 0 } ? personIds.Intersect(members).ToList() : [.. members];
            if (personIds.Count == 0)
            {
                return Results.Ok(new { total = 0, reachable = 0 });
            }
        }

        var contacts = await people.FindContactsAsync(new AudienceFilter(spec.MembershipStatuses, spec.Tags, spec.BranchId, personIds), ct);
        var reachable = broadcast.Channel switch
        {
            Channel.Email => contacts.Count(c => !string.IsNullOrWhiteSpace(c.Email)),
            Channel.Sms => contacts.Count(c => !string.IsNullOrWhiteSpace(c.PhoneNumber)),
            _ => contacts.Count(c => c.UserId is not null),
        };
        return Results.Ok(new { total = contacts.Count, reachable });
    }

    private static async Task<IResult> SendBroadcast(Guid id, SendBroadcastRequest? r, CommunicationsDbContext db, TimeProvider clock, CancellationToken ct) =>
        await BroadcastAction(id, db, b => b.Schedule(r?.ScheduledFor ?? clock.GetUtcNow()), ct);

    private static async Task<IResult> BroadcastAction(Guid id, CommunicationsDbContext db, Action<Broadcast> action, CancellationToken ct)
    {
        var broadcast = await db.Broadcasts.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (broadcast is null)
        {
            return NotFound.ToProblem();
        }

        action(broadcast);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(broadcast));
    }

    private static async Task<IResult> ListDeliveries(Guid id, [AsParameters] PageRequest page, DeliveryStatus? status, CommunicationsDbContext db, CancellationToken ct)
    {
        var query = db.Deliveries.AsNoTracking().Where(d => d.BroadcastId == id);
        if (status is { } s) query = query.Where(d => d.Status == s);
        var total = await query.LongCountAsync(ct);
        var items = await query.OrderBy(d => d.RecipientName).Skip(page.Skip).Take(page.SafePageSize)
            .Select(d => new { d.PersonId, d.RecipientName, d.Destination, Status = d.Status.ToString(), d.Attempts, d.Error, d.SentAt })
            .ToListAsync(ct);
        return Results.Ok(new { items, page = page.SafePage, pageSize = page.SafePageSize, totalCount = total });
    }

    // ---- Devices & notifications -----------------------------------------------------------

    private static async Task<IResult> RegisterDevice(RegisterDeviceRequest r, ICurrentUser user, CommunicationsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Token) || r.Token.Length > 512)
        {
            return Error.Validation("device.invalid_token", "A valid push token is required.").ToProblem();
        }

        var now = clock.GetUtcNow();
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Token == r.Token, ct);
        if (device is null)
        {
            db.Devices.Add(DeviceRegistration.Register(user.RequiredUserId, r.Platform, r.Token, r.AppVersion, now));
        }
        else
        {
            device.Refresh(user.RequiredUserId, r.Platform, r.AppVersion, now);
        }

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UnregisterDevice(string token, ICurrentUser user, CommunicationsDbContext db, CancellationToken ct)
    {
        await db.Devices.Where(d => d.Token == token && d.UserId == user.UserId).ExecuteDeleteAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> MyNotifications([AsParameters] PageRequest page, bool? unreadOnly, ICurrentUser user, CommunicationsDbContext db, CancellationToken ct)
    {
        var query = db.Notifications.AsNoTracking().Where(n => n.UserId == user.UserId);
        var unread = await query.CountAsync(n => n.ReadAt == null, ct);
        if (unreadOnly == true) query = query.Where(n => n.ReadAt == null);
        var items = await query.OrderByDescending(n => n.CreatedAt).Skip(page.Skip).Take(page.SafePageSize)
            .Select(n => new { n.Id, n.Category, n.Title, n.Body, n.Link, n.ReadAt, n.CreatedAt }).ToListAsync(ct);
        return Results.Ok(new { unreadCount = unread, items });
    }

    private static async Task<IResult> MarkRead(Guid id, ICurrentUser user, CommunicationsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await db.Notifications.Where(n => n.Id == id && n.UserId == user.UserId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> MarkAllRead(ICurrentUser user, CommunicationsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await db.Notifications.Where(n => n.UserId == user.UserId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
        return Results.NoContent();
    }
}
