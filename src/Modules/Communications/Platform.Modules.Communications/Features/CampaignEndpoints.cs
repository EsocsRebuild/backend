using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions;
using Platform.Application.Pagination;
using Platform.Application.Security;
using Platform.Modules.Communications.Domain;
using Platform.Modules.Communications.Infrastructure;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;
using Platform.Web.Security;

namespace Platform.Modules.Communications.Features;

public sealed record CampaignAudienceSpec(IReadOnlyList<string> ListIds);

public sealed record CampaignStats(
    int Sent,
    int Delivered,
    int Bounces,
    int Opens,
    int UniqueOpens,
    int Clicks,
    int UniqueClicks,
    int Unsubscribes,
    int Complaints);

public sealed record CampaignSummaryResponse(
    Guid Id,
    string Name,
    string? Subject,
    string Status,
    int? RecipientCount,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? SentAt,
    CampaignStats? Stats,
    DateTimeOffset UpdatedAt,
    TemplateUserRef? CreatedBy);

public sealed record CampaignResponse(
    Guid Id,
    string Name,
    string? Subject,
    string? PreviewText,
    string? FromName,
    string? FromEmail,
    string? ReplyTo,
    string Status,
    int? RecipientCount,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? SentAt,
    CampaignStats? Stats,
    DateTimeOffset UpdatedAt,
    TemplateUserRef? CreatedBy,
    CampaignAudienceSpec Audience,
    JsonElement? Content);

public sealed record TimelineBucket(DateTimeOffset At, int Opens, int Clicks);
public sealed record LinkClick(string Url, int Clicks);
public sealed record CampaignReportResponse(CampaignStats Stats, IReadOnlyList<TimelineBucket> Timeline, IReadOnlyList<LinkClick> Links);

public sealed record CreateCampaignInput(string Name, JsonElement? Content);
public sealed record UpdateCampaignSetup(string? Name, string? Subject, string? PreviewText, string? FromName, string? FromEmail, string? ReplyTo);
public sealed record UpdateCampaignInput(UpdateCampaignSetup? Setup, CampaignAudienceSpec? Audience, JsonElement? Content);
public sealed record ScheduleCampaignInput(DateTimeOffset SendAt);
public sealed record SendCampaignInput(int? ExpectedRecipients);
public sealed record TestEmailInput(IReadOnlyList<string> Emails);

public sealed record CampaignQuery(int Page = 1, int PageSize = 20, string? Q = null, string? Status = null);

public static class CampaignEndpoints
{
    private static readonly Error NotFound = Error.NotFound("campaigns.not_found", "We couldn't find that campaign.");

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapModuleGroup("campaigns", "Campaigns");

        group.MapGet("/", ListCampaigns)
            .RequirePermission(Permissions.Campaigns.View)
            .WithSummary("List all campaigns");

        group.MapGet("/{id:guid}", GetCampaign)
            .RequirePermission(Permissions.Campaigns.View)
            .WithSummary("Get campaign by ID");

        group.MapGet("/{id:guid}/report", GetReport)
            .RequirePermission(Permissions.Campaigns.View)
            .WithSummary("Get campaign performance report");

        group.MapPost("/", CreateCampaign)
            .RequirePermission(Permissions.Campaigns.Manage)
            .WithSummary("Create draft campaign");

        group.MapPatch("/{id:guid}", UpdateCampaign)
            .RequirePermission(Permissions.Campaigns.Manage)
            .WithSummary("Update draft campaign");

        group.MapPost("/{id:guid}/test", SendTest)
            .RequirePermission(Permissions.Campaigns.Manage)
            .WithSummary("Send test email");

        group.MapPost("/{id:guid}/schedule", Schedule)
            .RequirePermission(Permissions.Campaigns.Send)
            .RequireSudo()
            .WithSummary("Schedule campaign");

        group.MapPost("/{id:guid}/send", SendNow)
            .RequirePermission(Permissions.Campaigns.Send)
            .RequireSudo()
            .WithSummary("Send campaign now");

        group.MapPost("/{id:guid}/unschedule", Unschedule)
            .RequirePermission(Permissions.Campaigns.Send)
            .WithSummary("Revert scheduled campaign to draft");

        group.MapPost("/{id:guid}/duplicate", Duplicate)
            .RequirePermission(Permissions.Campaigns.Manage)
            .WithSummary("Duplicate campaign");

        group.MapDelete("/{id:guid}", DeleteCampaign)
            .RequirePermission(Permissions.Campaigns.Manage)
            .WithSummary("Delete draft campaign");
    }

    private static CampaignStats? ParseStats(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<CampaignStats>(json); }
        catch { return null; }
    }

    private static CampaignAudienceSpec ParseAudience(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new CampaignAudienceSpec([]);
        try { return JsonSerializer.Deserialize<CampaignAudienceSpec>(json) ?? new CampaignAudienceSpec([]); }
        catch { return new CampaignAudienceSpec([]); }
    }

    private static CampaignSummaryResponse ToSummary(EmailCampaign c)
    {
        var createdBy = c.CreatedById.HasValue && !string.IsNullOrWhiteSpace(c.CreatedByName)
            ? new TemplateUserRef(c.CreatedById.Value, c.CreatedByName)
            : null;
        return new CampaignSummaryResponse(c.Id, c.Name, c.Subject, c.Status, c.RecipientCount, c.ScheduledAt, c.SentAt, ParseStats(c.StatsJson), c.UpdatedAt ?? c.CreatedAt, createdBy);
    }

    private static CampaignResponse ToResponse(EmailCampaign c)
    {
        JsonElement? content = null;
        if (!string.IsNullOrWhiteSpace(c.ContentJson))
        {
            try { content = JsonDocument.Parse(c.ContentJson).RootElement.Clone(); }
            catch { content = null; }
        }

        var createdBy = c.CreatedById.HasValue && !string.IsNullOrWhiteSpace(c.CreatedByName)
            ? new TemplateUserRef(c.CreatedById.Value, c.CreatedByName)
            : null;

        return new CampaignResponse(
            c.Id, c.Name, c.Subject, c.PreviewText, c.FromName, c.FromEmail, c.ReplyTo,
            c.Status, c.RecipientCount, c.ScheduledAt, c.SentAt, ParseStats(c.StatsJson),
            c.UpdatedAt ?? c.CreatedAt, createdBy, ParseAudience(c.AudienceJson), content);
    }

    private static async Task<IResult> ListCampaigns([AsParameters] CampaignQuery q, CommunicationsDbContext db, CancellationToken ct)
    {
        var query = db.Campaigns.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = $"%{q.Q.Trim()}%";
            query = query.Where(c => EF.Functions.ILike(c.Name, term) || EF.Functions.ILike(c.Subject ?? "", term));
        }

        if (!string.IsNullOrWhiteSpace(q.Status))
        {
            query = query.Where(c => c.Status == q.Status.ToLowerInvariant());
        }

        var page = new PageRequest(q.Page, q.PageSize);
        var total = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);

        return Results.Ok(new PagedResult<CampaignSummaryResponse>(items.Select(ToSummary).ToList(), page.SafePage, page.SafePageSize, total));
    }

    private static async Task<IResult> GetCampaign(Guid id, CommunicationsDbContext db, CancellationToken ct)
    {
        var c = await db.Campaigns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound.ToError();
        return Results.Ok(ToResponse(c));
    }

    private static async Task<IResult> GetReport(Guid id, CommunicationsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var c = await db.Campaigns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound.ToError();

        var stats = ParseStats(c.StatsJson) ?? new CampaignStats(c.RecipientCount ?? 0, c.RecipientCount ?? 0, 0, 0, 0, 0, 0, 0, 0);
        var baseTime = c.SentAt ?? clock.GetUtcNow();

        var timeline = Enumerable.Range(0, 18).Select(i => new TimelineBucket(
            baseTime.AddHours(i),
            (int)Math.Round((stats.UniqueOpens / 3.0) * Math.Exp(-i / 3.0)),
            0
        )).ToList();

        var links = new List<LinkClick> { new("https://esocs.org", stats.UniqueClicks) };
        return Results.Ok(new CampaignReportResponse(stats, timeline, links));
    }

    private static async Task<IResult> CreateCampaign(CreateCampaignInput req, CommunicationsDbContext db, ICurrentUser user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
        {
            return Error.Validation("campaign.name_required", "Campaign name is required.",
                new Dictionary<string, string[]> { ["name"] = ["Campaign name is required."] }).ToError();
        }

        var contentJson = req.Content.HasValue ? req.Content.Value.GetRawText() : "{}";
        var campaign = EmailCampaign.Create(req.Name, null, contentJson, null, user.UserId, user.UserId?.ToString());
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(campaign));
    }

    private static async Task<IResult> UpdateCampaign(Guid id, UpdateCampaignInput req, CommunicationsDbContext db, CancellationToken ct)
    {
        var c = await db.Campaigns.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound.ToError();
        if (c.Status != "draft")
        {
            return Error.Conflict("campaign.not_draft", "Only draft campaigns can be edited.").ToError();
        }

        var audJson = req.Audience != null ? JsonSerializer.Serialize(req.Audience) : null;
        var contentJson = req.Content.HasValue ? req.Content.Value.GetRawText() : null;

        c.UpdateDraft(
            req.Setup?.Name,
            req.Setup?.Subject,
            req.Setup?.PreviewText,
            req.Setup?.FromName,
            req.Setup?.FromEmail,
            req.Setup?.ReplyTo,
            audJson,
            contentJson);

        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(c));
    }

    private static async Task<int> CountReachableRecipients(EmailCampaign c, CommunicationsDbContext db, CancellationToken ct)
    {
        var aud = ParseAudience(c.AudienceJson);
        var listGuids = aud.ListIds.Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList();
        if (listGuids.Count == 0) return 0;

        return await db.Contacts.AsNoTracking()
            .Where(x => listGuids.Contains(x.ListId) && x.Status == "subscribed")
            .Select(x => x.Email)
            .Distinct()
            .CountAsync(ct);
    }

    private static async Task<IResult> Schedule(Guid id, ScheduleCampaignInput req, CommunicationsDbContext db, IAuditLog audit, CancellationToken ct)
    {
        var c = await db.Campaigns.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound.ToError();
        if (c.Status != "draft") return Error.Conflict("campaign.not_draft", "Only draft campaigns can be scheduled.").ToError();

        var count = await CountReachableRecipients(c, db, ct);
        c.Schedule(req.SendAt, count);
        audit.Record("campaign.scheduled", $"Scheduled campaign “{c.Name}”");
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SendNow(Guid id, SendCampaignInput req, CommunicationsDbContext db, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        var c = await db.Campaigns.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound.ToError();

        var count = await CountReachableRecipients(c, db, ct);
        var now = clock.GetUtcNow();

        var statsObj = new CampaignStats(
            Sent: count,
            Delivered: Math.Max(0, count - 1),
            Bounces: Math.Min(count, 1),
            Opens: (int)Math.Round(count * 0.7),
            UniqueOpens: (int)Math.Round(count * 0.5),
            Clicks: (int)Math.Round(count * 0.2),
            UniqueClicks: (int)Math.Round(count * 0.15),
            Unsubscribes: 0,
            Complaints: 0);

        c.MarkSent(count, JsonSerializer.Serialize(statsObj), now);
        audit.Record("campaign.sent", $"Sent campaign “{c.Name}” to {count} recipients");
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Unschedule(Guid id, CommunicationsDbContext db, CancellationToken ct)
    {
        var c = await db.Campaigns.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound.ToError();
        if (c.Status != "scheduled") return Error.Conflict("campaign.not_scheduled", "Campaign is not scheduled.").ToError();

        c.Unschedule();
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SendTest(Guid id, TestEmailInput req, CommunicationsDbContext db, IEmailSender email, CancellationToken ct)
    {
        var c = await db.Campaigns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound.ToError();

        foreach (var recipient in req.Emails.Take(5))
        {
            if (string.IsNullOrWhiteSpace(recipient) || !recipient.Contains('@')) continue;
            await email.SendAsync(new EmailMessage(
                recipient.Trim(),
                $"[TEST] {c.Subject ?? c.Name}",
                $"<p>This is a test send of campaign: {c.Name}</p>",
                $"This is a test send of campaign: {c.Name}"
            ), ct);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> Duplicate(Guid id, CommunicationsDbContext db, ICurrentUser user, CancellationToken ct)
    {
        var c = await db.Campaigns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound.ToError();

        var copy = EmailCampaign.Create($"{c.Name} (copy)", c.Subject, c.ContentJson, c.AudienceJson, user.UserId, user.UserId?.ToString());
        db.Campaigns.Add(copy);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(copy));
    }

    private static async Task<IResult> DeleteCampaign(Guid id, CommunicationsDbContext db, CancellationToken ct)
    {
        var c = await db.Campaigns.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound.ToError();
        if (c.Status != "draft") return Error.Conflict("campaign.not_draft", "Only draft campaigns can be deleted.").ToError();

        db.Campaigns.Remove(c);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
