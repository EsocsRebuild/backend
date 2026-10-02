using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Platform.Application.Abstractions;
using Platform.Application.Messaging;
using Platform.Application.Security;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Identity.Infrastructure;
using Platform.Modules.Identity.Services;
using Platform.Modules.Tenancy.Contracts;

namespace Platform.Modules.Identity.Features;

/// <summary>
/// Provisions a new organisation: the built-in roles and the Owner's administrator account (invited by
/// email unless the person already has a password). Idempotent — safe for at-least-once delivery and
/// for being invoked directly by the seeder.
/// </summary>
public sealed class TenantProvisioning(
    IdentityDbContext db,
    IEmailSender email,
    IOptions<AuthOptions> options,
    TimeProvider clock) : IEventHandler<TenantCreatedIntegrationEvent>
{
    public async Task Handle(TenantCreatedIntegrationEvent e, CancellationToken cancellationToken)
    {
        var roles = await EnsureSystemRolesAsync(e.TenantId, cancellationToken);
        var now = clock.GetUtcNow();

        var normalized = e.OwnerEmail.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, cancellationToken);
        if (user is null)
        {
            user = User.Create(normalized, $"{e.OwnerFirstName} {e.OwnerLastName}".Trim());
            db.Users.Add(user);
        }

        var membership = await db.Memberships.IgnoreQueryFilters([QueryFilters.Tenant])
            .Include(m => m.Roles)
            .FirstOrDefaultAsync(m => m.TenantId == e.TenantId && m.UserId == user.Id && m.Kind == MembershipKind.Staff, cancellationToken);

        string? inviteToken = null;
        if (membership is null)
        {
            var hasPassword = user.PasswordHash is not null;
            membership = TenantMembership.Create(e.TenantId, user.Id, MembershipKind.Staff,
                hasPassword ? MembershipStatus.Active : MembershipStatus.Invited, invitedBy: null, now);
            membership.AnnounceAccountCreated(user);
            db.Memberships.Add(membership);

            if (!hasPassword)
            {
                inviteToken = SecretHasher.NewSecret();
                var invitation = StaffInvitation.Issue(membership.Id, user.Email, SecretHasher.Hash(inviteToken), now.Add(options.Value.InvitationLifetime), "The platform team");
                invitation.AssignTenant(e.TenantId);
                db.Invitations.Add(invitation);
            }
        }

        membership.AssignRole(roles[SystemRoles.Owner].Id);
        await db.SaveChangesAsync(cancellationToken);

        if (inviteToken is not null)
        {
            await email.SendAsync(EmailTemplates.Invitation(user.Email, user.Name, e.Name, "The platform team", SystemRoles.Owner,
                $"{options.Value.AppBaseUrl}/invite/{inviteToken}"), cancellationToken);
        }
    }

    /// <summary>Creates any missing built-in roles; existing ones keep the organisation's customisations.</summary>
    public async Task<Dictionary<string, Role>> EnsureSystemRolesAsync(Guid tenantId, CancellationToken ct)
    {
        var existing = await db.Roles.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(r => r.TenantId == tenantId && r.SystemKey != null)
            .ToDictionaryAsync(r => r.SystemKey!, StringComparer.Ordinal, ct);

        foreach (var def in SystemRoles.Defaults.Where(d => !existing.ContainsKey(d.Key)))
        {
            var role = Role.Create(def.Name, def.Description, def.Permissions, def.Key, def.Locked, def.Requestable, def.Icon);
            role.AssignTenant(tenantId);
            db.Roles.Add(role);
            existing[def.Key] = role;
        }

        return existing;
    }
}
