using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Platform.Application.Abstractions;
using Platform.Application.Pagination;
using Platform.Application.Security;
using Platform.Application.Tenancy;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Identity.Infrastructure;
using Platform.Modules.Identity.Services;
using Platform.Modules.Tenancy.Contracts;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;
using Platform.Web.Security;

namespace Platform.Modules.Identity.Features;

public sealed record AdminUserResponse(Guid Id, string Name, string Email, string? AvatarUrl, RoleRef Role, string Status, bool MfaEnabled,
    DateTimeOffset? LastActiveAt, DateTimeOffset CreatedAt);

public sealed record InviteRequest(string Email, string? Name, Guid RoleId);

public sealed record ChangeRoleRequest(Guid RoleId);

public sealed record RoleResponse(Guid Id, string Name, string? Description, IReadOnlyList<string> Permissions, int UserCount, bool System, bool Locked);

public sealed record SaveRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);

public sealed record AccessRequestResponse(Guid Id, string Name, string Email, string? Phone, NamedRef? Parish, NamedRef? RequestedRole,
    bool EmailVerified, DateTimeOffset CreatedAt);

public sealed record NamedRef(Guid Id, string Name);

public sealed record ApproveRequest(Guid RoleId);

public sealed record RejectRequest(string? Reason);

public sealed record AdminUserQuery(int Page = 1, int PageSize = 20, string? Q = null, string? Sort = null, string? Dir = null, string? Status = null, Guid? RoleId = null);

internal sealed class InviteValidator : AbstractValidator<InviteRequest>
{
    public InviteValidator()
    {
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Name).MaximumLength(120);
        RuleFor(x => x.RoleId).NotEmpty().WithMessage("Choose a role.");
    }
}

internal sealed class SaveRoleValidator : AbstractValidator<SaveRoleRequest>
{
    public SaveRoleValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Give the role a name.").MinimumLength(2).MaximumLength(60);
        RuleFor(x => x.Description).MaximumLength(280);
        RuleFor(x => x.Permissions).NotNull();
        RuleForEach(x => x.Permissions).Must(Permissions.IsKnown).WithMessage("Unknown permission “{PropertyValue}”.");
    }
}

/// <summary>Administrators, roles and access requests (API contract §10–11).</summary>
public static class AdministrationEndpoints
{
    private static readonly Error UserNotFound = Error.NotFound("user.not_found", "We couldn’t find that administrator.");
    private static readonly Error RoleNotFound = Error.NotFound("role.not_found", "We couldn’t find that role.");
    private static readonly Error NotSelf = Error.Forbidden("user.not_self", "You can’t do that to your own account. Ask another administrator.");
    private static readonly Error LastOwner = Error.Conflict("user.last_owner", "The organisation must keep at least one active Owner.");
    private static readonly Error OwnerOnly = Error.Forbidden("role.owner_only", "Only an Owner can give someone permission to edit roles.");

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var users = endpoints.MapModuleGroup("admin-users", "Administrators");
        users.MapGet("/", List).RequirePermission(Permissions.Users.View).WithSummary("Administrators");
        users.MapPost("/invitations", Invite).WithValidation<InviteRequest>().RequirePermission(Permissions.Users.Manage).RequireSudo()
            .WithSummary("Invite an administrator");
        users.MapPost("/{id:guid}/invitation/resend", ResendInvitation).RequirePermission(Permissions.Users.Manage).WithSummary("Send the invitation again");
        users.MapDelete("/{id:guid}/invitation", CancelInvitation).RequirePermission(Permissions.Users.Manage).WithSummary("Cancel a pending invitation");
        users.MapPut("/{id:guid}/role", ChangeRole).RequirePermission(Permissions.Users.Manage).RequireSudo().WithSummary("Change an administrator's role");
        users.MapPost("/{id:guid}/suspend", Suspend).RequirePermission(Permissions.Users.Manage).RequireSudo().WithSummary("Suspend and sign out everywhere");
        users.MapPost("/{id:guid}/reactivate", Reactivate).RequirePermission(Permissions.Users.Manage).RequireSudo().WithSummary("Restore access");
        users.MapPost("/{id:guid}/mfa/reset", ResetMfa).RequirePermission(Permissions.Users.Manage).RequireSudo().WithSummary("Turn off someone's two-step verification");

        var roles = endpoints.MapModuleGroup("roles", "Administrators");
        roles.MapGet("/", ListRoles).RequirePermission(Permissions.Users.View).WithSummary("Roles");
        roles.MapGet("/{id:guid}", GetRole).RequirePermission(Permissions.Users.View).WithSummary("A role");
        roles.MapPost("/", CreateRole).WithValidation<SaveRoleRequest>().RequirePermission(Permissions.Roles.Manage).RequireSudo().WithSummary("Create a role");
        roles.MapPut("/{id:guid}", UpdateRole).WithValidation<SaveRoleRequest>().RequirePermission(Permissions.Roles.Manage).RequireSudo().WithSummary("Edit a role");
        roles.MapDelete("/{id:guid}", DeleteRole).RequirePermission(Permissions.Roles.Manage).RequireSudo().WithSummary("Delete an unused custom role");

        var requests = endpoints.MapModuleGroup("access-requests", "Administrators");
        requests.MapGet("/", ListRequests).RequirePermission(Permissions.Users.Manage).WithSummary("Access requests");
        requests.MapPost("/{id:guid}/approve", Approve).RequirePermission(Permissions.Users.Manage).RequireSudo().WithSummary("Approve with a role");
        requests.MapPost("/{id:guid}/reject", Reject).RequirePermission(Permissions.Users.Manage).WithSummary("Reject a request");
    }

    // ---- Administrators ----------------------------------------------------------------------------

    private static IQueryable<AdminRow> Rows(IdentityDbContext db) =>
        from m in db.Memberships.AsNoTracking()
        join u in db.Users.AsNoTracking() on m.UserId equals u.Id
        where m.Kind == MembershipKind.Staff
        let roleId = m.Roles.Select(r => r.RoleId).FirstOrDefault()
        select new AdminRow
        {
            Id = m.Id,
            Name = u.Name,
            Email = u.Email,
            AvatarUrl = u.AvatarUrl,
            RoleId = roleId,
            RoleName = db.Roles.Where(r => r.Id == roleId).Select(r => r.Name).FirstOrDefault(),
            Status = m.Status,
            MfaEnabled = u.TwoFactorEnabled,
            LastActiveAt = m.LastActiveAt,
            CreatedAt = m.CreatedAt,
        };

    /// <summary>Query row (member-initialised so EF can filter and sort on it).</summary>
    private sealed class AdminRow
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = null!;
        public string Email { get; init; } = null!;
        public string? AvatarUrl { get; init; }
        public Guid RoleId { get; init; }
        public string? RoleName { get; init; }
        public MembershipStatus Status { get; init; }
        public bool MfaEnabled { get; init; }
        public DateTimeOffset? LastActiveAt { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }

    private static AdminUserResponse ToResponse(AdminRow r) => new(r.Id, r.Name, r.Email, r.AvatarUrl, new RoleRef(r.RoleId, r.RoleName ?? "No role"),
        r.Status.ToString().ToLowerInvariant(), r.MfaEnabled, r.LastActiveAt, r.CreatedAt);

    private static async Task<IResult> List([AsParameters] AdminUserQuery q, IdentityDbContext db, CancellationToken ct)
    {
        var page = new PageRequest(q.Page, q.PageSize, q.Q, q.Sort, q.Dir);
        var query = Rows(db);
        if (page.Search is { } term)
        {
            query = query.Where(r => EF.Functions.ILike(r.Name, $"%{term}%") || EF.Functions.ILike(r.Email, $"%{term}%"));
        }

        if (Enum.TryParse<MembershipStatus>(q.Status, true, out var status)) query = query.Where(r => r.Status == status);
        if (q.RoleId is { } roleId) query = query.Where(r => r.RoleId == roleId);

        query = (page.Sort, page.Descending) switch
        {
            ("email", false) => query.OrderBy(r => r.Email),
            ("email", true) => query.OrderByDescending(r => r.Email),
            ("lastActiveAt", false) => query.OrderBy(r => r.LastActiveAt),
            ("lastActiveAt", true) => query.OrderByDescending(r => r.LastActiveAt),
            ("createdAt", false) => query.OrderBy(r => r.CreatedAt),
            ("createdAt", true) => query.OrderByDescending(r => r.CreatedAt),
            ("status", false) => query.OrderBy(r => r.Status),
            ("status", true) => query.OrderByDescending(r => r.Status),
            (_, true) => query.OrderByDescending(r => r.Name),
            _ => query.OrderBy(r => r.Name),
        };

        var total = await query.LongCountAsync(ct);
        var rows = await query.Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        return Results.Ok(new PagedResult<AdminUserResponse>(rows.Select(ToResponse).ToList(), page.SafePage, page.SafePageSize, total));
    }

    private static async Task<IResult> Invite(
        InviteRequest r, ITenantContext tenant, ICurrentUser caller, IdentityDbContext db, IEmailSender email, ITenantDirectory tenants,
        IPermissionService permissions, IAuditLog audit, IOptions<AuthOptions> options, TimeProvider clock, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(x => x.Id == r.RoleId, ct);
        if (role is null || role.SystemKey == SystemRoles.Member)
        {
            return RoleNotFound.ToError();
        }

        if (role.Permissions.Contains(Permissions.Roles.Manage) && !await IsOwnerAsync(db, caller.MembershipId, ct))
        {
            return OwnerOnly.ToError();
        }

        var now = clock.GetUtcNow();
        var normalized = r.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);
        if (user is not null && await db.Memberships.AnyAsync(m => m.UserId == user.Id && m.Kind == MembershipKind.Staff, ct))
        {
            return Error.Conflict("user.already_admin", "That person is already an administrator. Change their role instead.").ToError();
        }

        var isNew = user is null;
        user ??= User.Create(normalized, string.IsNullOrWhiteSpace(r.Name) ? normalized.Split('@')[0] : r.Name);
        if (isNew)
        {
            db.Users.Add(user);
        }

        var hasPassword = user.PasswordHash is not null;
        var membership = TenantMembership.Create(tenant.RequiredTenantId, user.Id, MembershipKind.Staff,
            hasPassword ? MembershipStatus.Active : MembershipStatus.Invited, caller.MembershipId, now);
        membership.AssignRole(role.Id);
        membership.AnnounceAccountCreated(user);
        db.Memberships.Add(membership);

        var organisation = (await tenants.GetAsync(tenant.RequiredTenantId, ct))?.Name ?? "the organisation";
        var inviter = caller.Email is null ? "An administrator" : (await db.Users.Where(u => u.Id == caller.UserId).Select(u => u.Name).FirstOrDefaultAsync(ct)) ?? "An administrator";
        EmailMessage message;
        if (hasPassword)
        {
            message = EmailTemplates.AddedToOrganisation(user.Email, user.Name, organisation, role.Name, $"{options.Value.AppBaseUrl}/login");
        }
        else
        {
            var token = SecretHasher.NewSecret();
            db.Invitations.Add(StaffInvitation.Issue(membership.Id, user.Email, SecretHasher.Hash(token), now.Add(options.Value.InvitationLifetime), inviter));
            message = EmailTemplates.Invitation(user.Email, string.IsNullOrWhiteSpace(r.Name) ? null : r.Name, organisation, inviter, role.Name,
                $"{options.Value.AppBaseUrl}/invite/{token}");
        }

        audit.Record("user.invited", $"Invited {user.Email} as {role.Name}", AuditSeverity.Warning, new AuditTarget("user", membership.Id.ToString(), user.Email));
        await db.SaveChangesAsync(ct);
        await permissions.InvalidateAsync(tenant.RequiredTenantId, ct);
        await email.SendAsync(message, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ResendInvitation(Guid id, IdentityDbContext db, ICurrentUser caller, IEmailSender email, ITenantDirectory tenants,
        ITenantContext tenant, IAuditLog audit, IOptions<AuthOptions> options, TimeProvider clock, CancellationToken ct)
    {
        var membership = await db.Memberships.Include(m => m.Roles).FirstOrDefaultAsync(m => m.Id == id && m.Kind == MembershipKind.Staff, ct);
        if (membership is null)
        {
            return UserNotFound.ToError();
        }

        if (membership.Status != MembershipStatus.Invited)
        {
            return Error.Conflict("user.not_invited", "This person has already accepted their invitation.").ToError();
        }

        var now = clock.GetUtcNow();
        var old = await db.Invitations.Where(i => i.MembershipId == id && i.AcceptedAt == null && i.RevokedAt == null).ToListAsync(ct);
        old.ForEach(i => i.Revoke(now));

        var user = await db.Users.FirstAsync(u => u.Id == membership.UserId, ct);
        var role = await db.Roles.FirstAsync(x => x.Id == membership.RoleId, ct);
        var inviter = await db.Users.Where(u => u.Id == caller.UserId).Select(u => u.Name).FirstOrDefaultAsync(ct) ?? "An administrator";
        var token = SecretHasher.NewSecret();
        db.Invitations.Add(StaffInvitation.Issue(id, user.Email, SecretHasher.Hash(token), now.Add(options.Value.InvitationLifetime), inviter));
        audit.Record("user.invitation_resent", $"Resent the invitation to {user.Email}", target: new AuditTarget("user", id.ToString(), user.Email));
        await db.SaveChangesAsync(ct);

        var organisation = (await tenants.GetAsync(tenant.RequiredTenantId, ct))?.Name ?? "the organisation";
        await email.SendAsync(EmailTemplates.Invitation(user.Email, null, organisation, inviter, role.Name, $"{options.Value.AppBaseUrl}/invite/{token}"), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CancelInvitation(Guid id, IdentityDbContext db, IAuditLog audit, IPermissionService permissions, ITenantContext tenant,
        TimeProvider clock, CancellationToken ct)
    {
        var membership = await db.Memberships.FirstOrDefaultAsync(m => m.Id == id && m.Kind == MembershipKind.Staff && m.Status == MembershipStatus.Invited, ct);
        if (membership is null)
        {
            return UserNotFound.ToError();
        }

        var now = clock.GetUtcNow();
        var invitations = await db.Invitations.Where(i => i.MembershipId == id && i.RevokedAt == null).ToListAsync(ct);
        invitations.ForEach(i => i.Revoke(now));
        var email = await db.Users.Where(u => u.Id == membership.UserId).Select(u => u.Email).FirstAsync(ct);
        db.Memberships.Remove(membership);
        audit.Record("user.invitation_cancelled", $"Cancelled the invitation for {email}", AuditSeverity.Warning, new AuditTarget("user", id.ToString(), email));
        await db.SaveChangesAsync(ct);
        await permissions.InvalidateAsync(tenant.RequiredTenantId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ChangeRole(Guid id, ChangeRoleRequest r, IdentityDbContext db, ICurrentUser caller, IPermissionService permissions,
        ITenantContext tenant, IAuditLog audit, CancellationToken ct)
    {
        if (id == caller.MembershipId)
        {
            return NotSelf.ToError();
        }

        var membership = await db.Memberships.Include(m => m.Roles).FirstOrDefaultAsync(m => m.Id == id && m.Kind == MembershipKind.Staff, ct);
        var role = await db.Roles.FirstOrDefaultAsync(x => x.Id == r.RoleId, ct);
        if (membership is null)
        {
            return UserNotFound.ToError();
        }

        if (role is null || role.SystemKey == SystemRoles.Member)
        {
            return RoleNotFound.ToError();
        }

        if (role.Permissions.Contains(Permissions.Roles.Manage) && !await IsOwnerAsync(db, caller.MembershipId, ct))
        {
            return OwnerOnly.ToError();
        }

        var previous = await db.Roles.FirstOrDefaultAsync(x => x.Id == membership.RoleId, ct);
        if (previous?.SystemKey == SystemRoles.Owner && role.SystemKey != SystemRoles.Owner && await IsLastActiveOwnerAsync(db, membership.Id, ct))
        {
            return LastOwner.ToError();
        }

        membership.AssignRole(role.Id);
        membership.SetScope(role.SystemKey is SystemRoles.Owner or SystemRoles.Administrator ? null : membership.ScopeUnitId);
        var name = await db.Users.Where(u => u.Id == membership.UserId).Select(u => u.Name).FirstAsync(ct);
        audit.Record("user.role_changed", $"Changed {name}’s role to {role.Name}", AuditSeverity.Warning, new AuditTarget("user", id.ToString(), name),
            AuditChanges.Diff(("role", previous?.Name, role.Name)));
        await db.SaveChangesAsync(ct);
        await permissions.InvalidateAsync(tenant.RequiredTenantId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Suspend(Guid id, IdentityDbContext db, ICurrentUser caller, SessionService sessions, IPermissionService permissions,
        ITenantContext tenant, IAuditLog audit, CancellationToken ct)
    {
        if (id == caller.MembershipId)
        {
            return NotSelf.ToError();
        }

        var membership = await db.Memberships.Include(m => m.Roles).FirstOrDefaultAsync(m => m.Id == id && m.Kind == MembershipKind.Staff, ct);
        if (membership is null)
        {
            return UserNotFound.ToError();
        }

        if (await IsLastActiveOwnerAsync(db, id, ct))
        {
            return LastOwner.ToError();
        }

        membership.Suspend();
        await sessions.RevokeAllAsync(membership.UserId, "suspended", exceptSessionId: null, ct, membershipId: id);
        var name = await db.Users.Where(u => u.Id == membership.UserId).Select(u => u.Name).FirstAsync(ct);
        audit.Record("user.suspended", $"Suspended {name}", AuditSeverity.Critical, new AuditTarget("user", id.ToString(), name));
        await db.SaveChangesAsync(ct);
        await permissions.InvalidateAsync(tenant.RequiredTenantId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Reactivate(Guid id, IdentityDbContext db, IPermissionService permissions, ITenantContext tenant, IAuditLog audit,
        TimeProvider clock, CancellationToken ct)
    {
        var membership = await db.Memberships.FirstOrDefaultAsync(m => m.Id == id && m.Kind == MembershipKind.Staff && m.Status == MembershipStatus.Suspended, ct);
        if (membership is null)
        {
            return UserNotFound.ToError();
        }

        membership.Activate(clock.GetUtcNow());
        var name = await db.Users.Where(u => u.Id == membership.UserId).Select(u => u.Name).FirstAsync(ct);
        audit.Record("user.reactivated", $"Reactivated {name}", AuditSeverity.Warning, new AuditTarget("user", id.ToString(), name));
        await db.SaveChangesAsync(ct);
        await permissions.InvalidateAsync(tenant.RequiredTenantId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ResetMfa(Guid id, IdentityDbContext db, ICurrentUser caller, SessionService sessions, IAuditLog audit, CancellationToken ct)
    {
        if (id == caller.MembershipId)
        {
            return NotSelf.ToError();
        }

        var membership = await db.Memberships.FirstOrDefaultAsync(m => m.Id == id && m.Kind == MembershipKind.Staff, ct);
        if (membership is null)
        {
            return UserNotFound.ToError();
        }

        var user = await db.Users.FirstAsync(u => u.Id == membership.UserId, ct);
        user.DisableTwoFactor();
        await sessions.RevokeAllAsync(user.Id, "mfa_reset", exceptSessionId: null, ct);
        audit.Record("user.mfa_reset", $"Turned off two-step verification for {user.Name}", AuditSeverity.Critical, new AuditTarget("user", id.ToString(), user.Name));
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ---- Roles -------------------------------------------------------------------------------------

    private static async Task<List<RoleResponse>> RolesAsync(IdentityDbContext db, Guid? id, CancellationToken ct)
    {
        var query = db.Roles.AsNoTracking().Where(r => r.SystemKey == null || r.SystemKey != SystemRoles.Member);
        if (id is not null) query = query.Where(r => r.Id == id);

        return (await query
                .Select(r => new
                {
                    r,
                    Users = db.MembershipRoles.Count(mr => mr.RoleId == r.Id &&
                        db.Memberships.Any(m => m.Id == mr.MembershipId && m.Kind == MembershipKind.Staff)),
                })
                .ToListAsync(ct))
            .OrderByDescending(x => x.r.IsLocked).ThenByDescending(x => x.r.IsSystem).ThenBy(x => x.r.Name)
            .Select(x => new RoleResponse(x.r.Id, x.r.Name, x.r.Description, x.r.Permissions, x.Users, x.r.IsSystem, x.r.IsLocked))
            .ToList();
    }

    private static async Task<IResult> ListRoles(IdentityDbContext db, CancellationToken ct) => Results.Ok(await RolesAsync(db, null, ct));

    private static async Task<IResult> GetRole(Guid id, IdentityDbContext db, CancellationToken ct) =>
        (await RolesAsync(db, id, ct)).FirstOrDefault() is { } role ? Results.Ok(role) : RoleNotFound.ToError();

    private static async Task<IResult> CreateRole(SaveRoleRequest r, IdentityDbContext db, ICurrentUser caller, IAuditLog audit, CancellationToken ct)
    {
        if (r.Permissions.Contains(Permissions.Roles.Manage) && !await IsOwnerAsync(db, caller.MembershipId, ct))
        {
            return OwnerOnly.ToError();
        }

        if (await db.Roles.AnyAsync(x => x.Name == r.Name.Trim(), ct))
        {
            return Error.Conflict("role.name_taken", "A role with that name already exists.",
                new Dictionary<string, string[]> { ["name"] = ["A role with that name already exists."] }).ToError();
        }

        var role = Role.Create(r.Name.Trim(), string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim(), r.Permissions);
        db.Roles.Add(role);
        audit.Record("role.created", $"Created the role {role.Name}", AuditSeverity.Warning, new AuditTarget("role", role.Id.ToString(), role.Name));
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/roles/{role.Id}", new RoleResponse(role.Id, role.Name, role.Description, role.Permissions, 0, false, false));
    }

    private static async Task<IResult> UpdateRole(Guid id, SaveRoleRequest r, IdentityDbContext db, ICurrentUser caller, IPermissionService permissions,
        ITenantContext tenant, IAuditLog audit, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (role is null)
        {
            return RoleNotFound.ToError();
        }

        if (role.IsLocked)
        {
            return Error.Forbidden("role.locked", "This role can’t be changed.").ToError();
        }

        var grantsRoles = r.Permissions.Contains(Permissions.Roles.Manage) && !role.Permissions.Contains(Permissions.Roles.Manage);
        if (grantsRoles && !await IsOwnerAsync(db, caller.MembershipId, ct))
        {
            return OwnerOnly.ToError();
        }

        if (await db.Roles.AnyAsync(x => x.Name == r.Name.Trim() && x.Id != id, ct))
        {
            return Error.Conflict("role.name_taken", "A role with that name already exists.",
                new Dictionary<string, string[]> { ["name"] = ["A role with that name already exists."] }).ToError();
        }

        var before = role.Permissions.ToHashSet();
        role.Update(r.Name, string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim(), r.Permissions);
        var added = role.Permissions.Where(p => !before.Contains(p)).ToList();
        var removed = before.Where(p => !role.Permissions.Contains(p)).ToList();
        audit.Record("role.updated", $"Changed permissions for {role.Name}", AuditSeverity.Warning, new AuditTarget("role", role.Id.ToString(), role.Name),
            new Dictionary<string, AuditChange> { ["permissions"] = new(removed, added) });
        await db.SaveChangesAsync(ct);
        await permissions.InvalidateAsync(tenant.RequiredTenantId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteRole(Guid id, IdentityDbContext db, IAuditLog audit, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (role is null)
        {
            return RoleNotFound.ToError();
        }

        if (role.IsSystem)
        {
            return Error.Conflict("role.system", "Built-in roles can’t be deleted.").ToError();
        }

        if (await db.MembershipRoles.AnyAsync(mr => mr.RoleId == id, ct))
        {
            return Error.Conflict("role.in_use", "People still have this role. Give them another role first.").ToError();
        }

        db.Roles.Remove(role);
        audit.Record("role.deleted", $"Deleted the role {role.Name}", AuditSeverity.Warning, new AuditTarget("role", role.Id.ToString(), role.Name));
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ---- Access requests ---------------------------------------------------------------------------

    private static async Task<IResult> ListRequests(string? status, IdentityDbContext db, IUnitDirectory units, CancellationToken ct)
    {
        var wanted = Enum.TryParse<AccessRequestStatus>(status, true, out var s) ? s : AccessRequestStatus.Pending;
        var requests = await db.AccessRequests.AsNoTracking().Where(a => a.Status == wanted).OrderByDescending(a => a.CreatedAt).Take(500).ToListAsync(ct);
        var parishes = await units.GetAsync(requests.Where(a => a.ParishId != null).Select(a => a.ParishId!.Value), ct);
        var roleIds = requests.Where(a => a.RequestedRoleId != null).Select(a => a.RequestedRoleId!.Value).Distinct().ToList();
        var roles = await db.Roles.AsNoTracking().Where(r => roleIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name, ct);

        return Results.Ok(requests.Select(a => new AccessRequestResponse(a.Id, a.Name, a.Email, a.Phone,
            a.ParishId is { } p && parishes.TryGetValue(p, out var parish) ? new NamedRef(parish.Id, parish.Name) : null,
            a.RequestedRoleId is { } rid && roles.TryGetValue(rid, out var roleName) ? new NamedRef(rid, roleName) : null,
            a.EmailVerifiedAt is not null, a.CreatedAt)));
    }

    private static async Task<IResult> Approve(Guid id, ApproveRequest r, IdentityDbContext db, ICurrentUser caller, ITenantContext tenant,
        IPermissionService permissions, IEmailSender email, ITenantDirectory tenants, IAuditLog audit, IOptions<AuthOptions> options,
        TimeProvider clock, CancellationToken ct)
    {
        var request = await db.AccessRequests.FirstOrDefaultAsync(a => a.Id == id, ct);
        var role = await db.Roles.FirstOrDefaultAsync(x => x.Id == r.RoleId, ct);
        if (request is null)
        {
            return Error.NotFound("access_request.not_found", "We couldn’t find that request.").ToError();
        }

        if (role is null || role.SystemKey == SystemRoles.Member)
        {
            return RoleNotFound.ToError();
        }

        if (request.EmailVerifiedAt is null)
        {
            return Error.Conflict("access_request.unverified", "This person hasn’t verified their email address yet.").ToError();
        }

        if (role.Permissions.Contains(Permissions.Roles.Manage) && !await IsOwnerAsync(db, caller.MembershipId, ct))
        {
            return OwnerOnly.ToError();
        }

        var now = clock.GetUtcNow();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == request.Email, ct);
        if (user is not null && await db.Memberships.AnyAsync(m => m.UserId == user.Id && m.Kind == MembershipKind.Staff, ct))
        {
            request.Reject("Already an administrator", caller.MembershipId, now);
            await db.SaveChangesAsync(ct);
            return Error.Conflict("user.already_admin", "That person is already an administrator.").ToError();
        }

        if (user is null)
        {
            user = User.Create(request.Email, request.Name, request.Phone);
            db.Users.Add(user);
        }

        if (user.PasswordHash is null)
        {
            user.AdoptPasswordHash(request.PasswordHash, now);
        }

        user.ConfirmEmail();
        var membership = TenantMembership.Create(tenant.RequiredTenantId, user.Id, MembershipKind.Staff, MembershipStatus.Active, caller.MembershipId, now);
        membership.AssignRole(role.Id);
        membership.SetScope(role.SystemKey is SystemRoles.Owner or SystemRoles.Administrator ? null : request.ParishId);
        membership.AnnounceAccountCreated(user);
        db.Memberships.Add(membership);
        request.Approve(membership.Id, caller.MembershipId, now);

        audit.Record("access.approved", $"Approved access for {request.Name} as {role.Name}", AuditSeverity.Warning,
            new AuditTarget("user", membership.Id.ToString(), request.Name));
        await db.SaveChangesAsync(ct);
        await permissions.InvalidateAsync(tenant.RequiredTenantId, ct);

        var organisation = (await tenants.GetAsync(tenant.RequiredTenantId, ct))?.Name ?? "the organisation";
        await email.SendAsync(EmailTemplates.AccessApproved(user.Email, user.Name, organisation, role.Name, $"{options.Value.AppBaseUrl}/login"), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Reject(Guid id, RejectRequest? r, IdentityDbContext db, ICurrentUser caller, ITenantContext tenant,
        IEmailSender email, ITenantDirectory tenants, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        var request = await db.AccessRequests.FirstOrDefaultAsync(a => a.Id == id && a.Status == AccessRequestStatus.Pending, ct);
        if (request is null)
        {
            return Error.NotFound("access_request.not_found", "We couldn’t find that request.").ToError();
        }

        var reason = string.IsNullOrWhiteSpace(r?.Reason) ? null : r.Reason.Trim()[..Math.Min(r.Reason.Trim().Length, 500)];
        request.Reject(reason, caller.MembershipId, clock.GetUtcNow());
        audit.Record("access.rejected", $"Rejected access for {request.Name}", target: new AuditTarget("access_request", id.ToString(), request.Name));
        await db.SaveChangesAsync(ct);

        if (request.EmailVerifiedAt is not null)
        {
            var organisation = (await tenants.GetAsync(tenant.RequiredTenantId, ct))?.Name ?? "the organisation";
            await email.SendAsync(EmailTemplates.AccessRejected(request.Email, request.Name, organisation, reason), ct);
        }

        return Results.NoContent();
    }

    // ---- Rules -------------------------------------------------------------------------------------

    private static async Task<bool> IsOwnerAsync(IdentityDbContext db, Guid? membershipId, CancellationToken ct) =>
        membershipId is { } mid && await db.MembershipRoles.AnyAsync(mr => mr.MembershipId == mid &&
            db.Roles.Any(r => r.Id == mr.RoleId && r.SystemKey == SystemRoles.Owner), ct);

    private static async Task<bool> IsLastActiveOwnerAsync(IdentityDbContext db, Guid membershipId, CancellationToken ct)
    {
        var ownerRoleId = await db.Roles.Where(r => r.SystemKey == SystemRoles.Owner).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
        if (ownerRoleId is null || !await db.MembershipRoles.AnyAsync(mr => mr.MembershipId == membershipId && mr.RoleId == ownerRoleId, ct))
        {
            return false;
        }

        return !await db.Memberships.AnyAsync(m => m.Id != membershipId && m.Status == MembershipStatus.Active && m.Kind == MembershipKind.Staff &&
                                                   m.Roles.Any(r => r.RoleId == ownerRoleId), ct);
    }
}
