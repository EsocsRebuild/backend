using Platform.SharedKernel.Domain;

namespace Platform.Modules.Events.Domain;

public enum Frequency
{
    Daily,
    Weekly,
    Monthly,
}

/// <summary>
/// A pragmatic subset of RFC 5545 RRULE covering church calendars:
/// FREQ=DAILY|WEEKLY|MONTHLY; INTERVAL=n; BYDAY=SU,WE (weekly); BYSETPOS=1..4,-1 with BYDAY (monthly,
/// e.g. "first Sunday"); COUNT=n; UNTIL=yyyyMMdd. Expansion happens in the event's local time zone.
/// </summary>
public sealed record Recurrence(Frequency Frequency, int Interval, IReadOnlyList<DayOfWeek> ByDay, int? BySetPos, int? Count, DateOnly? Until)
{
    private const int MaxOccurrences = 1000;

    private static readonly Dictionary<string, DayOfWeek> Days = new(StringComparer.Ordinal)
    {
        ["SU"] = DayOfWeek.Sunday, ["MO"] = DayOfWeek.Monday, ["TU"] = DayOfWeek.Tuesday, ["WE"] = DayOfWeek.Wednesday,
        ["TH"] = DayOfWeek.Thursday, ["FR"] = DayOfWeek.Friday, ["SA"] = DayOfWeek.Saturday,
    };

    public static bool TryParse(string rule, out Recurrence? recurrence, out string? error)
    {
        try
        {
            recurrence = Parse(rule);
            error = null;
            return true;
        }
        catch (DomainException ex)
        {
            recurrence = null;
            error = ex.Message;
            return false;
        }
    }

    public static Recurrence Parse(string rule)
    {
        var parts = rule.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0].ToUpperInvariant(), p => p.Length > 1 ? p[1].ToUpperInvariant() : string.Empty);

        if (!parts.TryGetValue("FREQ", out var freqText) || !Enum.TryParse<Frequency>(freqText, ignoreCase: true, out var freq))
        {
            throw new DomainException("RRULE must specify FREQ=DAILY, WEEKLY or MONTHLY.");
        }

        var interval = parts.TryGetValue("INTERVAL", out var i) ? int.Parse(i, System.Globalization.CultureInfo.InvariantCulture) : 1;
        if (interval is < 1 or > 52)
        {
            throw new DomainException("INTERVAL must be between 1 and 52.");
        }

        var byDay = parts.TryGetValue("BYDAY", out var d)
            ? d.Split(',').Select(x => Days.TryGetValue(x, out var day) ? day : throw new DomainException($"Unknown BYDAY value '{x}'.")).ToList()
            : [];

        int? setPos = parts.TryGetValue("BYSETPOS", out var sp) ? int.Parse(sp, System.Globalization.CultureInfo.InvariantCulture) : null;
        if (setPos is not null and not (>= 1 and <= 4 or -1))
        {
            throw new DomainException("BYSETPOS must be 1–4 or -1.");
        }

        int? count = parts.TryGetValue("COUNT", out var c) ? int.Parse(c, System.Globalization.CultureInfo.InvariantCulture) : null;
        DateOnly? until = parts.TryGetValue("UNTIL", out var u)
            ? DateOnly.ParseExact(u[..8], "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)
            : null;

        return new Recurrence(freq, interval, byDay, setPos, count, until);
    }

    /// <summary>Yields occurrence start instants (UTC offsets resolved per date, DST-correct).</summary>
    public IEnumerable<DateTimeOffset> Expand(DateTimeOffset firstStart, TimeZoneInfo zone, DateTimeOffset horizon)
    {
        var local = TimeZoneInfo.ConvertTime(firstStart, zone);
        var timeOfDay = local.TimeOfDay;
        var startDate = DateOnly.FromDateTime(local.DateTime);
        var produced = 0;

        foreach (var date in Dates(startDate))
        {
            if (date < startDate)
            {
                continue;
            }

            if ((Until is { } until && date > until) || (Count is { } count && produced >= count) || produced >= MaxOccurrences)
            {
                yield break;
            }

            var localDateTime = date.ToDateTime(TimeOnly.FromTimeSpan(timeOfDay), DateTimeKind.Unspecified);
            var instant = new DateTimeOffset(localDateTime, zone.GetUtcOffset(localDateTime));
            if (instant > horizon)
            {
                yield break;
            }

            produced++;
            yield return instant.ToUniversalTime();
        }
    }

    private IEnumerable<DateOnly> Dates(DateOnly start)
    {
        switch (Frequency)
        {
            case Frequency.Daily:
                for (var d = start; ; d = d.AddDays(Interval))
                {
                    yield return d;
                }

            case Frequency.Weekly:
                var days = ByDay.Count > 0 ? ByDay : [start.DayOfWeek];
                var weekStart = start.AddDays(-(int)start.DayOfWeek);
                for (var w = weekStart; ; w = w.AddDays(7 * Interval))
                {
                    foreach (var day in days.OrderBy(x => x))
                    {
                        yield return w.AddDays((int)day);
                    }
                }

            case Frequency.Monthly:
                for (var m = new DateOnly(start.Year, start.Month, 1); ; m = m.AddMonths(Interval))
                {
                    if (BySetPos is { } pos && ByDay.Count > 0)
                    {
                        var candidates = Enumerable.Range(0, DateTime.DaysInMonth(m.Year, m.Month))
                            .Select(m.AddDays).Where(x => ByDay.Contains(x.DayOfWeek)).ToList();
                        var index = pos == -1 ? candidates.Count - 1 : pos - 1;
                        if (index < candidates.Count)
                        {
                            yield return candidates[index];
                        }
                    }
                    else if (start.Day <= DateTime.DaysInMonth(m.Year, m.Month))
                    {
                        yield return new DateOnly(m.Year, m.Month, start.Day);
                    }
                }

            default:
                throw new DomainException("Unsupported frequency.");
        }
    }
}
