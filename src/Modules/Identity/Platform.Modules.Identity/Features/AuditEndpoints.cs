using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions;
using Platform.Application.Pagination;
using Platform.Application.Security;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Auditing;
using Platform.Modules.Identity.Infrastructure;
using Platform.Web.Endpoints;
using Platform.Web.Security;

namespace Platform.Modules.Identity.Features;

public sealed record AuditActorResponse(Guid Id, string Name, string Email);

public sealed record AuditTargetResponse(string Type, string Id, string Label);

public sealed record AuditEventResponse(Guid Id, string Action, string Summary, string Severity, AuditActorResponse? Actor, AuditTargetResponse? Target,
    string? Ip, string? UserAgent, JsonElement? Changes, DateTimeOffset CreatedAt);

public sealed record AuditQuery(int Page = 1, int PageSize = 20, string? Q = null, string? Sort = null, string? Dir = null,
    string? Severity = null, Guid? ActorId = null, DateTimeOffset? From = null);

/// <summary>The organisation's activity log (API contract §12). Append-only; read and export only.</summary>
public static class AuditEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapModuleGroup("audit-events", "Audit log");
        group.MapGet("/", List).RequirePermission(Permissions.Audit.View).WithSummary("Search the activity log");
        group.MapGet("/export", Export).RequirePermission(Permissions.Audit.View).WithSummary("Download the activity log as CSV");
    }

    private static IQueryable<AuditEvent> Filter(IdentityDbContext db, Guid tenantId, AuditQuery q)
    {
        var query = db.AuditEvents.AsNoTracking().Where(e => e.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = $"%{q.Q.Trim()}%";
            query = query.Where(e => EF.Functions.ILike(e.Summary, term) || EF.Functions.ILike(e.Action, term) ||
                                     EF.Functions.ILike(e.ActorEmail ?? "", term) || EF.Functions.ILike(e.TargetLabel ?? "", term));
        }

        if (Enum.TryParse<AuditSeverity>(q.Severity, true, out var severity)) query = query.Where(e => e.Severity == severity);
        if (q.ActorId is { } actor) query = query.Where(e => e.ActorId == actor);
        if (q.From is { } from) query = query.Where(e => e.CreatedAt >= from);
        return query;
    }

    private static async Task<IResult> List([AsParameters] AuditQuery q, ITenantContext tenant, IdentityDbContext db, CancellationToken ct)
    {
        var page = new PageRequest(q.Page, q.PageSize, q.Q, q.Sort, q.Dir);
        var query = Filter(db, tenant.RequiredTenantId, q);
        query = page.Sort == "createdAt" && !page.Descending ? query.OrderBy(e => e.CreatedAt) : query.OrderByDescending(e => e.CreatedAt);

        var total = await query.LongCountAsync(ct);
        var events = await query.Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        return Results.Ok(new PagedResult<AuditEventResponse>(events.Select(ToResponse).ToList(), page.SafePage, page.SafePageSize, total));
    }

    private static async Task<IResult> Export([AsParameters] AuditQuery q, ITenantContext tenant, IdentityDbContext db, IAuditLog audit, CancellationToken ct)
    {
        var events = await Filter(db, tenant.RequiredTenantId, q).OrderByDescending(e => e.CreatedAt).Take(50_000).ToListAsync(ct);
        audit.Record("audit.exported", $"Exported {events.Count} audit events", AuditSeverity.Warning);
        await audit.FlushAsync(ct);

        var rows = new List<IReadOnlyList<object?>> { new object?[] { "When", "Action", "Summary", "Severity", "By", "Email", "Target", "IP address" } };
        rows.AddRange(events.Select(e => (IReadOnlyList<object?>)[e.CreatedAt, e.Action, e.Summary, e.Severity.ToString().ToLowerInvariant(),
            e.ActorName ?? "System", e.ActorEmail, e.TargetLabel, e.IpAddress]));
        return new CsvResult($"audit-log-{DateTime.UtcNow:yyyyMMdd}.csv", rows);
    }

    private static AuditEventResponse ToResponse(AuditEvent e) => new(
        e.Id, e.Action, e.Summary, e.Severity.ToString().ToLowerInvariant(),
        e.ActorId is { } id ? new AuditActorResponse(id, e.ActorName ?? e.ActorEmail ?? "Unknown", e.ActorEmail ?? string.Empty)
            : e.ActorEmail is not null ? new AuditActorResponse(Guid.Empty, e.ActorName ?? e.ActorEmail, e.ActorEmail) : null,
        e.TargetType is null ? null : new AuditTargetResponse(e.TargetType, e.TargetId ?? string.Empty, e.TargetLabel ?? string.Empty),
        e.IpAddress, e.UserAgent, e.Changes is null ? null : JsonDocument.Parse(e.Changes).RootElement.Clone(), e.CreatedAt);
}
