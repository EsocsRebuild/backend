using Platform.Modules.Events.Domain;
using Platform.SharedKernel.Domain;

namespace Platform.UnitTests;

public class RecurrenceTests
{
    private static readonly TimeZoneInfo Lagos = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    [Fact]
    public void Weekly_sunday_service_expands_every_sunday_at_the_same_local_time()
    {
        var first = new DateTimeOffset(2026, 9, 27, 9, 0, 0, TimeSpan.FromHours(1)); // Sunday 09:00 WAT
        var dates = Recurrence.Parse("FREQ=WEEKLY;BYDAY=SU").Expand(first, Lagos, first.AddDays(28)).ToList();

        Assert.Equal(5, dates.Count);
        Assert.All(dates, d => Assert.Equal(DayOfWeek.Sunday, TimeZoneInfo.ConvertTime(d, Lagos).DayOfWeek));
        Assert.All(dates, d => Assert.Equal(9, TimeZoneInfo.ConvertTime(d, Lagos).Hour));
    }

    [Fact]
    public void Expansion_keeps_local_time_across_daylight_saving_changes()
    {
        // US DST ends 1 Nov 2026: UTC offset moves from -4 to -5, but the service stays at 10:00 local.
        var first = new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.FromHours(-4));
        var dates = Recurrence.Parse("FREQ=WEEKLY;BYDAY=SU").Expand(first, NewYork, first.AddDays(15)).ToList();

        Assert.Equal([14, 15, 15], dates.Select(d => d.UtcDateTime.Hour).ToArray());
        Assert.All(dates, d => Assert.Equal(10, TimeZoneInfo.ConvertTime(d, NewYork).Hour));
    }

    [Fact]
    public void Monthly_first_sunday_uses_bysetpos()
    {
        var first = new DateTimeOffset(2026, 10, 4, 8, 0, 0, TimeSpan.FromHours(1));
        var dates = Recurrence.Parse("FREQ=MONTHLY;BYDAY=SU;BYSETPOS=1").Expand(first, Lagos, first.AddMonths(3)).ToList();

        Assert.Equal([new DateOnly(2026, 10, 4), new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 6), new DateOnly(2027, 1, 3)],
            dates.Select(d => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(d, Lagos).DateTime)).ToArray());
    }

    [Fact]
    public void Count_and_until_limit_the_series()
    {
        var first = new DateTimeOffset(2026, 9, 30, 18, 0, 0, TimeSpan.Zero);
        Assert.Equal(3, Recurrence.Parse("FREQ=WEEKLY;COUNT=3").Expand(first, TimeZoneInfo.Utc, first.AddYears(1)).Count());
        Assert.Equal(2, Recurrence.Parse("FREQ=WEEKLY;UNTIL=20261007").Expand(first, TimeZoneInfo.Utc, first.AddYears(1)).Count());
    }

    [Theory]
    [InlineData("FREQ=HOURLY")]
    [InlineData("BYDAY=SU")]
    [InlineData("FREQ=WEEKLY;BYDAY=XX")]
    [InlineData("FREQ=WEEKLY;INTERVAL=0")]
    public void Invalid_rules_are_rejected(string rule) =>
        Assert.False(Recurrence.TryParse(rule, out _, out _));

    [Fact]
    public void Syncing_occurrences_keeps_ones_with_activity()
    {
        var evt = Event.Create("Midweek", "midweek", EventType.Meeting);
        var start = new DateTimeOffset(2026, 10, 7, 18, 0, 0, TimeSpan.Zero);
        evt.Update(Details(start, "FREQ=WEEKLY;BYDAY=WE"));
        var now = start.AddDays(-1);
        evt.SyncOccurrences(now, now.AddDays(30), new HashSet<Guid>());
        var withAttendance = evt.Occurrences.First();

        // Move the meeting to Thursdays: the Wednesday with attendance must survive.
        evt.Update(Details(start.AddDays(1), "FREQ=WEEKLY;BYDAY=TH"));
        evt.SyncOccurrences(now, now.AddDays(30), new HashSet<Guid> { withAttendance.Id });

        Assert.Contains(evt.Occurrences, o => o.Id == withAttendance.Id);
        Assert.All(evt.Occurrences.Where(o => o.Id != withAttendance.Id), o => Assert.Equal(DayOfWeek.Thursday, o.StartsAt.DayOfWeek));
    }

    [Fact]
    public void Event_must_end_after_it_starts()
    {
        var evt = Event.Create("Bad", "bad", EventType.Other);
        var start = DateTimeOffset.UtcNow;
        Assert.Throws<DomainException>(() => evt.Update(Details(start, null) with { EndsAt = start }));
    }

    private static EventDetails Details(DateTimeOffset start, string? rule) => new(
        "Midweek", "midweek", EventType.Meeting, EventVisibility.Members, null, null, null, null, null, null, false, null,
        start, start.AddHours(2), "UTC", false, rule, false, null, null, 0);
}
