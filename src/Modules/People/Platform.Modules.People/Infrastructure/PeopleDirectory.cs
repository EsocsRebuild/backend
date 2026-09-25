using Microsoft.EntityFrameworkCore;
using Platform.Modules.People.Contracts;
using Platform.Modules.People.Domain;

namespace Platform.Modules.People.Infrastructure;

internal sealed class PeopleDirectory(PeopleDbContext db) : IPeopleDirectory
{
    public async Task<IReadOnlyDictionary<Guid, PersonSummary>> GetSummariesAsync(IEnumerable<Guid> personIds, CancellationToken cancellationToken)
    {
        var ids = personIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, PersonSummary>();
        }

        return await db.People.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new PersonSummary(p.Id, p.MemberNumber, (p.PreferredName ?? p.FirstName) + " " + p.LastName, p.Email, p.PhoneNumber, p.PhotoUrl, p.BranchId, p.UserId))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
    }

    public async Task<Guid?> FindPersonIdByUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.People.AsNoTracking().Where(p => p.UserId == userId).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ContactInfo>> FindContactsAsync(AudienceFilter filter, CancellationToken cancellationToken)
    {
        var query = db.People.AsNoTracking().Where(p => p.MembershipStatus != MembershipStatus.Deceased);
        if (filter.RequireConsent) query = query.Where(p => p.ConsentToContact);
        if (filter.BranchId is { } branchId) query = query.Where(p => p.BranchId == branchId);
        if (filter.PersonIds is { Count: > 0 } ids) query = query.Where(p => ids.Contains(p.Id));
        if (filter.Tags is { Count: > 0 } tags)
        {
            var normalized = tags.Select(t => t.ToLowerInvariant()).ToList();
            query = query.Where(p => p.Tags.Any(t => normalized.Contains(t)));
        }

        if (filter.MembershipStatuses is { Count: > 0 } statuses)
        {
            var parsed = statuses.Select(s => Enum.TryParse<MembershipStatus>(s, true, out var v) ? v : (MembershipStatus?)null)
                .Where(v => v is not null).Select(v => v!.Value).ToList();
            query = query.Where(p => parsed.Contains(p.MembershipStatus));
        }

        return await query
            .Select(p => new ContactInfo(p.Id, p.PreferredName ?? p.FirstName, (p.PreferredName ?? p.FirstName) + " " + p.LastName, p.Email, p.PhoneNumber, p.UserId))
            .ToListAsync(cancellationToken);
    }
}
