using Microsoft.EntityFrameworkCore;
using Platform.Modules.People.Domain;

namespace Platform.Modules.People.Features;

public sealed record CelebrationItem(Guid PersonId, string FullName, DateOnly Date, string? PhotoUrl);

public sealed record MemberStatsResponse(
    long Total, IReadOnlyDictionary<string, long> ByStatus, IReadOnlyDictionary<string, long> ByStage, long NewThisMonth, long NewLastMonth,
    long PendingApproval, long WithAccounts, long WithEmailConsent, IReadOnlyList<long> MonthlyTotals,
    IReadOnlyList<CelebrationItem> UpcomingBirthdays, IReadOnlyList<CelebrationItem> UpcomingAnniversaries);

/// <summary>Membership figures for dashboards (already scoped by the caller's parish).</summary>
internal static class MemberStats
{
    public static async Task<MemberStatsResponse> ComputeAsync(IQueryable<Person> people, DateTimeOffset now, CancellationToken ct)
    {
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var lastMonthStart = monthStart.AddMonths(-1);

        var byStatus = await people.GroupBy(p => p.Status).Select(g => new { g.Key, Count = g.LongCount() }).ToListAsync(ct);
        var byStage = await people.GroupBy(p => p.MembershipStatus).Select(g => new { g.Key, Count = g.LongCount() }).ToListAsync(ct);
        var newThisMonth = await people.LongCountAsync(p => p.CreatedAt >= monthStart, ct);
        var newLastMonth = await people.LongCountAsync(p => p.CreatedAt >= lastMonthStart && p.CreatedAt < monthStart, ct);
        var withAccounts = await people.LongCountAsync(p => p.UserId != null, ct);
        var withConsent = await people.LongCountAsync(p => p.EmailConsent, ct);

        // Running total at the end of each of the last 10 months (for sparklines).
        var since = monthStart.AddMonths(-9);
        var before = await people.LongCountAsync(p => p.CreatedAt < since, ct);
        var created = await people.Where(p => p.CreatedAt >= since)
            .GroupBy(p => new { p.CreatedAt.Year, p.CreatedAt.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.LongCount() })
            .ToListAsync(ct);
        var trend = new List<long>();
        var running = before;
        for (var m = since; m <= monthStart; m = m.AddMonths(1))
        {
            running += created.Where(c => c.Year == m.Year && c.Month == m.Month).Sum(c => c.Count);
            trend.Add(running);
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var days = Enumerable.Range(0, 7).Select(i => today.AddDays(i)).Select(d => d.Month * 100 + d.Day).ToList();
        var birthdays = await people
            .Where(p => p.DateOfBirth != null && days.Contains(p.DateOfBirth.Value.Month * 100 + p.DateOfBirth.Value.Day))
            .Select(p => new { p.Id, Name = (p.PreferredName ?? p.FirstName) + " " + p.LastName, Date = p.DateOfBirth!.Value, p.PhotoUrl })
            .OrderBy(p => p.Id).Take(200).ToListAsync(ct);
        var anniversaries = await people
            .Where(p => p.WeddingAnniversary != null && days.Contains(p.WeddingAnniversary.Value.Month * 100 + p.WeddingAnniversary.Value.Day))
            .Select(p => new { p.Id, Name = (p.PreferredName ?? p.FirstName) + " " + p.LastName, Date = p.WeddingAnniversary!.Value, p.PhotoUrl })
            .OrderBy(p => p.Id).Take(200).ToListAsync(ct);

        IReadOnlyList<CelebrationItem> Order(IEnumerable<(Guid Id, string Name, DateOnly Date, string? PhotoUrl)> items) =>
            items.OrderBy(x => days.IndexOf(x.Date.Month * 100 + x.Date.Day)).Select(x => new CelebrationItem(x.Id, x.Name, x.Date, x.PhotoUrl)).ToList();

        return new MemberStatsResponse(
            byStatus.Sum(s => s.Count),
            byStatus.ToDictionary(s => s.Key.ToString().ToLowerInvariant(), s => s.Count),
            byStage.ToDictionary(s => s.Key.ToString().ToLowerInvariant(), s => s.Count),
            newThisMonth, newLastMonth, byStatus.Where(s => s.Key == RecordStatus.Pending).Sum(s => s.Count), withAccounts, withConsent, trend,
            Order(birthdays.Select(b => (b.Id, b.Name, b.Date, b.PhotoUrl))),
            Order(anniversaries.Select(a => (a.Id, a.Name, a.Date, a.PhotoUrl))));
    }
}
