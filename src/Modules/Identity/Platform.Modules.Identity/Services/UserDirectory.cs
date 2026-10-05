using Microsoft.EntityFrameworkCore;
using Platform.Modules.Identity.Contracts;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Identity.Infrastructure;

namespace Platform.Modules.Identity.Services;

internal sealed class UserDirectory(IdentityDbContext db) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<Guid, UserSummary>> GetAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.Distinct().ToList();
        if (wanted.Count == 0)
        {
            return new Dictionary<Guid, UserSummary>();
        }

        var byUser = await db.Users.AsNoTracking().Where(u => wanted.Contains(u.Id))
            .Select(u => new { Key = u.Id, u.Id, u.Name, u.Email }).ToListAsync(cancellationToken);
        var byMembership = await (from m in db.Memberships.AsNoTracking()
                                  join u in db.Users.AsNoTracking() on m.UserId equals u.Id
                                  where wanted.Contains(m.Id)
                                  select new { Key = m.Id, u.Id, u.Name, u.Email }).ToListAsync(cancellationToken);

        return byUser.Concat(byMembership).DistinctBy(x => x.Key).ToDictionary(x => x.Key, x => new UserSummary(x.Id, x.Name, x.Email));
    }

    public async Task<IReadOnlyList<Guid>> GetStaffUserIdsWithPermissionAsync(string permission, string? preference, CancellationToken cancellationToken)
    {
        var roleIds = db.Roles.Where(r => r.Permissions.Contains(permission)).Select(r => r.Id);
        var staff = await db.Memberships.AsNoTracking()
            .Where(m => m.Kind == MembershipKind.Staff && m.Status == MembershipStatus.Active && m.Roles.Any(r => roleIds.Contains(r.RoleId)))
            .Select(m => new { m.UserId, m.NotificationPreferences })
            .ToListAsync(cancellationToken);

        return staff.Where(s => preference switch
            {
                "accessRequests" => s.NotificationPreferences.AccessRequests,
                "formResponses" => s.NotificationPreferences.FormResponses,
                "campaignReports" => s.NotificationPreferences.CampaignReports,
                "weeklySummary" => s.NotificationPreferences.WeeklySummary,
                _ => true,
            })
            .Select(s => s.UserId).Distinct().ToList();
    }

    public Task<int> CountPendingAccessRequestsAsync(CancellationToken cancellationToken) =>
        db.AccessRequests.CountAsync(a => a.Status == AccessRequestStatus.Pending && a.EmailVerifiedAt != null, cancellationToken);
}
