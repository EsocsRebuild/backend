using Microsoft.EntityFrameworkCore;
using Platform.Modules.Groups.Contracts;
using Platform.Modules.Groups.Domain;

namespace Platform.Modules.Groups.Infrastructure;

internal sealed class GroupDirectory(GroupsDbContext db) : IGroupDirectory
{
    public async Task<IReadOnlyList<Guid>> GetMemberPersonIdsAsync(Guid groupId, bool includeSubGroups, CancellationToken cancellationToken)
    {
        var groupIds = new List<Guid> { groupId };
        if (includeSubGroups)
        {
            var frontier = new List<Guid> { groupId };
            for (var depth = 0; frontier.Count > 0 && depth < 10; depth++)
            {
                frontier = await db.Groups.Where(g => g.ParentGroupId != null && frontier.Contains(g.ParentGroupId.Value)).Select(g => g.Id).ToListAsync(cancellationToken);
                groupIds.AddRange(frontier);
            }
        }

        return await db.Members.AsNoTracking()
            .Where(m => groupIds.Contains(m.GroupId) && m.Status == GroupMemberStatus.Active)
            .Select(m => m.PersonId).Distinct().ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetGroupIdsForPersonAsync(Guid personId, CancellationToken cancellationToken) =>
        await db.Members.AsNoTracking()
            .Where(m => m.PersonId == personId && m.Status == GroupMemberStatus.Active)
            .Select(m => m.GroupId).ToListAsync(cancellationToken);
}
