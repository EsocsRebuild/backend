using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions;
using Platform.Application.Security;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Identity.Infrastructure;
using Platform.Modules.Identity.Services;
using Platform.Modules.Tenancy.Contracts;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;

namespace Platform.Modules.Identity.Features;

/// <summary>Response for one of the user's tenant memberships.</summary>
public sealed record MyTenantResponse(
    Guid TenantId, string TenantName, string TenantSlug,
    Guid MembershipId, string RoleName, string Status, bool IsCurrent);

/// <summary>Switches the active tenant context by issuing fresh tokens for another membership.</summary>
public sealed record SwitchTenantRequest(Guid TenantId);

/// <summary>
/// Multi-tenant token-switching: allows a user who belongs to several organisations to hop between
/// them without re-entering credentials.
/// </summary>
public static class SwitchTenantEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var me = endpoints.MapGroup("me").WithTags("My account").RequireAuthorization();

        // List all tenants the signed-in user belongs to (as Staff).
        me.MapGet("/tenants", MyTenants)
            .WithSummary("All organisations I belong to and my role in each");

        // Exchange for tokens scoped to another tenant (no password needed).
        var auth = endpoints.MapGroup("auth").WithTags("Authentication").RequireAuthorization();
        auth.MapPost("/switch-tenant", SwitchTenant)
            .WithSummary("Get tokens for a different organisation I belong to");
    }

    // GET /api/v1/me/tenants
    private static async Task<IResult> MyTenants(
        ICurrentUser currentUser, ITenantContext tenantContext, IdentityDbContext db, ITenantDirectory tenants, CancellationToken ct)
    {
        string[] tenantFilter = [QueryFilters.Tenant];
        var rolesQuery = db.Roles.IgnoreQueryFilters(tenantFilter);

        // Load all staff memberships for this user, ignoring the current-tenant filter
        // so we can see memberships across all tenants.
        var memberships = await (
            from m in db.Memberships.IgnoreQueryFilters(tenantFilter).AsNoTracking()
            where m.UserId == currentUser.RequiredUserId && m.Kind == MembershipKind.Staff
            let roleId = m.Roles.Select(r => r.RoleId).FirstOrDefault()
            select new
            {
                m.Id,
                m.TenantId,
                m.Status,
                RoleName = (string?)rolesQuery
                    .Where(r => r.Id == roleId).Select(r => r.Name).FirstOrDefault(),
            }
        ).ToListAsync(ct);

        if (memberships.Count == 0)
        {
            return Results.Ok(Array.Empty<MyTenantResponse>());
        }

        var currentTenantId = tenantContext.TenantId;

        // Resolve each tenant individually (ITenantDirectory is a single-lookup contract backed by HybridCache).
        var tenantIds = memberships.Select(m => m.TenantId).Distinct().ToList();
        var tenantSummaries = await Task.WhenAll(tenantIds.Select(id => tenants.GetAsync(id, ct)));
        var tenantMap = tenantIds
            .Zip(tenantSummaries, (id, summary) => (id, summary))
            .Where(x => x.summary is not null)
            .ToDictionary(x => x.id, x => x.summary!);

        var result = memberships
            .Where(m => tenantMap.ContainsKey(m.TenantId))
            .OrderByDescending(m => m.TenantId == currentTenantId)
            .ThenBy(m => tenantMap[m.TenantId].Name)
            .Select(m => new MyTenantResponse(
                m.TenantId,
                tenantMap[m.TenantId].Name,
                tenantMap[m.TenantId].Slug,
                m.Id,
                m.RoleName ?? "No role",
                m.Status.ToString().ToLowerInvariant(),
                m.TenantId == currentTenantId))
            .ToList();

        return Results.Ok(result);
    }

    // POST /api/v1/auth/switch-tenant
    private static async Task<IResult> SwitchTenant(
        SwitchTenantRequest? r, ICurrentUser currentUser, IdentityDbContext db,
        SessionService sessions, IAuditLog audit, CancellationToken ct)
    {
        if (r?.TenantId is not { } targetTenantId)
        {
            return Error.Validation("tenant.required", "Specify a tenantId.").ToError();
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == currentUser.RequiredUserId, ct);
        if (user is null)
        {
            return AuthErrors.SessionEnded.ToError();
        }

        // Find an active staff membership in the target tenant, ignoring the tenant query filter
        // because the current token may be scoped to a different tenant.
        var membership = await db.Memberships
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .FirstOrDefaultAsync(m =>
                m.UserId == user.Id &&
                m.TenantId == targetTenantId &&
                m.Kind == MembershipKind.Staff &&
                m.Status == MembershipStatus.Active, ct);

        if (membership is null)
        {
            return Error.Forbidden("tenant.no_access", "You don't have access to that organisation.").ToError();
        }

        audit.Record("auth.tenant_switched",
            $"{user.Name} switched to tenant {targetTenantId}",
            target: new AuditTarget("user", membership.Id.ToString(), user.Name));

        // Issue a brand-new session bound to the target tenant; the previous session stays valid
        // so switching back doesn't require a new sign-in.
        var tokens = await sessions.StartAsync(user, membership, ClientType.Admin, remember: true, ct);
        return Results.Ok(new { status = "authenticated", tokens });
    }
}
