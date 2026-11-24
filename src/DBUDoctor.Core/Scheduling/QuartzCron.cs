using System.Globalization;

namespace DBUDoctor.Core.Scheduling;

/// <summary>
/// A Quartz cron expression (the syntax Databricks job schedules use): seconds, minutes, hours,
/// day of month, month, day of week and an optional year. Supports <c>*</c>, <c>?</c>, lists, ranges,
/// steps, month and day names, <c>L</c>, <c>W</c>, <c>LW</c> and <c>#</c>. Day of week runs from 1
/// (Sunday) to 7 (Saturday).
/// </summary>
/// <remarks>
/// Written here rather than taken from Quartz.NET: the package brings a scheduler, logging and
/// reflection-based configuration that Native AOT would have to trim, for one parser. Daylight
/// saving time: a local time that does not exist (spring forward) does not fire, and a local time
/// that occurs twice (fall back) fires once, at its first occurrence.
/// </remarks>
public sealed class QuartzCron
{
    private static readonly string[] MonthNames = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];
    private static readonly string[] DayNames = ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"];

    private readonly int[] _seconds;
    private readonly int[] _minutes;
    private readonly int[] _hours;
    private readonly bool[] _months = new bool[13];
    private readonly HashSet<int>? _years;
    private readonly DayOfMonthRule _dom;
    private readonly DayOfWeekRule _dow;

    private QuartzCron(string expression, int[] seconds, int[] minutes, int[] hours, IEnumerable<int> months, HashSet<int>? years, DayOfMonthRule dom, DayOfWeekRule dow)
    {
        Expression = expression;
        _seconds = seconds;
        _minutes = minutes;
        _hours = hours;
        foreach (var m in months)
        {
            _months[m] = true;
        }

        _years = years;
        _dom = dom;
        _dow = dow;
    }

    public string Expression { get; }

    /// <summary>Fire times per local day that has every hour: hours × minutes × seconds.</summary>
    public long FiresPerMatchingDay => (long)_hours.Length * _minutes.Length * _seconds.Length;

    public static bool TryParse(string expression, out QuartzCron? cron, out string? error)
    {
        try
        {
            cron = Parse(expression);
            error = null;
            return true;
        }
        catch (FormatException ex)
        {
            cron = null;
            error = ex.Message;
            return false;
        }
    }

    public static QuartzCron Parse(string expression)
    {
        var fields = expression.Trim().ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length is not (6 or 7))
        {
            throw new FormatException($"expected 6 or 7 fields (seconds minutes hours day-of-month month day-of-week [year]), found {fields.Length}");
        }

        var seconds = Values(fields[0], 0, 59, null, "seconds");
        var minutes = Values(fields[1], 0, 59, null, "minutes");
        var hours = Values(fields[2], 0, 23, null, "hours");
        var months = Values(fields[4], 1, 12, MonthNames, "month");
        var years = fields.Length == 7 && fields[6] != "*" ? Values(fields[6], 1970, 2199, null, "year").ToHashSet() : null;

        var domField = fields[3];
        var dowField = fields[5];
        if (domField != "?" && dowField != "?" && domField != "*" && dowField != "*")
        {
            throw new FormatException("day-of-month and day-of-week cannot both be set; use '?' in one of them");
        }

        // '*' in one day field with a value in the other means the value governs, as in Quartz.
        var dom = domField == "?" || (domField == "*" && dowField is not ("?" or "*")) ? DayOfMonthRule.Any(ignore: true) : ParseDayOfMonth(domField);
        var dow = dowField == "?" || (dowField == "*" && !dom.Ignore) ? DayOfWeekRule.Any(ignore: true) : ParseDayOfWeek(dowField);
        if (dom.Ignore && dow.Ignore)
        {
            dom = DayOfMonthRule.Any(ignore: false);
        }

        return new QuartzCron(expression.Trim(), seconds, minutes, hours, months, years, dom, dow);
    }

    /// <summary>Counts fire times in [<paramref name="start"/>, <paramref name="end"/>).</summary>
    public long CountFires(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone)
    {
        if (end <= start)
        {
            return 0;
        }

        var startUtc = start.UtcDateTime;
        var endUtc = end.UtcDateTime;
        var firstDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(startUtc, zone)).AddDays(-1);
        var lastDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(endUtc, zone)).AddDays(1);
        long count = 0;
        var perMinute = _seconds.Length;

        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            if (!DayMatches(day))
            {
                continue;
            }

            foreach (var hour in _hours)
            {
                var hourStart = new DateTime(day.Year, day.Month, day.Day, hour, 0, 0, DateTimeKind.Unspecified);
                if (zone.IsInvalidTime(hourStart) && zone.IsInvalidTime(hourStart.AddMinutes(59)))
                {
                    continue;
                }

                var hourUtcStart = ToUtc(hourStart, zone);
                if (hourUtcStart is { } s && s >= startUtc && s.AddHours(1) <= endUtc && !zone.IsInvalidTime(hourStart) && !zone.IsInvalidTime(hourStart.AddMinutes(59)))
                {
                    count += (long)_minutes.Length * perMinute;
                    continue;
                }

                // Partial hour (window edge or a DST change): count minute by minute, second by second at the edges.
                foreach (var minute in _minutes)
                {
                    var local = hourStart.AddMinutes(minute);
                    if (ToUtc(local, zone) is not { } minuteUtc)
                    {
                        continue;
                    }

                    if (minuteUtc >= startUtc && minuteUtc.AddMinutes(1) <= endUtc)
                    {
                        count += perMinute;
                    }
                    else
                    {
                        count += _seconds.Count(sec => minuteUtc.AddSeconds(sec) >= startUtc && minuteUtc.AddSeconds(sec) < endUtc);
                    }
                }
            }
        }

        return count;
    }

    /// <summary>The first fire time strictly after <paramref name="after"/>, within ten years.</summary>
    public DateTimeOffset? NextFire(DateTimeOffset after, TimeZoneInfo zone)
    {
        var afterUtc = after.UtcDateTime;
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(afterUtc, zone)).AddDays(-1);
        var limit = day.AddYears(10);
        for (; day <= limit; day = day.AddDays(1))
        {
            if (!DayMatches(day))
            {
                continue;
            }

            foreach (var hour in _hours)
            {
                var hourStart = new DateTime(day.Year, day.Month, day.Day, hour, 0, 0, DateTimeKind.Unspecified);
                if (ToUtc(hourStart.AddMinutes(59).AddSeconds(59), zone) is { } hourEnd && hourEnd <= afterUtc &&
                    !zone.IsAmbiguousTime(hourStart))
                {
                    continue;
                }

                foreach (var minute in _minutes)
                {
                    foreach (var second in _seconds)
                    {
                        var local = hourStart.AddMinutes(minute).AddSeconds(second);
                        if (ToUtc(local, zone) is { } utc && utc > afterUtc)
                        {
                            return new DateTimeOffset(utc, TimeSpan.Zero);
                        }
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The shortest interval between two consecutive fire times, sampled over up to
    /// <paramref name="maxFires"/> fires in the year after <paramref name="start"/>. Null when it fires less than twice.
    /// </summary>
    public TimeSpan? ShortestInterval(DateTimeOffset start, TimeZoneInfo zone, int maxFires = 20_000)
    {
        TimeSpan? shortest = null;
        var end = start.AddDays(366);
        var previous = NextFire(start.AddTicks(-1), zone);
        for (var i = 0; i < maxFires && previous is { } p && p < end; i++)
        {
            var next = NextFire(p, zone);
            if (next is not { } n || n >= end)
            {
                break;
            }

            // Wall-clock gap: a daily 08:00 schedule stays 24 h apart across a DST change.
            var gap = TimeZoneInfo.ConvertTime(n, zone).DateTime - TimeZoneInfo.ConvertTime(p, zone).DateTime;
            if (shortest is null || gap < shortest)
            {
                shortest = gap;
            }

            if (shortest == TimeSpan.FromSeconds(1))
            {
                break;
            }

            previous = n;
        }

        return shortest;
    }

    private static DateTime? ToUtc(DateTime local, TimeZoneInfo zone)
    {
        if (zone.IsInvalidTime(local))
        {
            return null;
        }

        if (zone.IsAmbiguousTime(local))
        {
            // First occurrence: the larger (daylight) offset.
            var offset = zone.GetAmbiguousTimeOffsets(local).Max();
            return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    private bool DayMatches(DateOnly day)
    {
        if (!_months[day.Month] || (_years is not null && !_years.Contains(day.Year)))
        {
            return false;
        }

        return (_dom.Ignore || _dom.Matches(day)) && (_dow.Ignore || _dow.Matches(day));
    }

    private static DayOfMonthRule ParseDayOfMonth(string field)
    {
        if (field == "L")
        {
            return new DayOfMonthRule { LastOffset = 0 };
        }

        if (field == "LW")
        {
            return new DayOfMonthRule { LastWeekday = true };
        }

        if (field.StartsWith("L-", StringComparison.Ordinal))
        {
            return new DayOfMonthRule { LastOffset = Number(field[2..], 0, 30, "day-of-month") };
        }

        if (field.EndsWith('W'))
        {
            return new DayOfMonthRule { NearestWeekday = Number(field[..^1], 1, 31, "day-of-month") };
        }

        return new DayOfMonthRule { Days = Values(field, 1, 31, null, "day-of-month").ToHashSet() };
    }

    private static DayOfWeekRule ParseDayOfWeek(string field)
    {
        if (field.Contains('#', StringComparison.Ordinal))
        {
            var parts = field.Split('#');
            return new DayOfWeekRule
            {
                NthDay = Value(parts[0], 1, 7, DayNames, "day-of-week"),
                Nth = Number(parts[1], 1, 5, "day-of-week"),
            };
        }

        if (field == "L")
        {
            return new DayOfWeekRule { Days = [7] };
        }

        if (field.EndsWith('L'))
        {
            return new DayOfWeekRule { LastDay = Value(field[..^1], 1, 7, DayNames, "day-of-week") };
        }

        return new DayOfWeekRule { Days = Values(field, 1, 7, DayNames, "day-of-week").ToHashSet() };
    }

    private static int[] Values(string field, int min, int max, string[]? names, string what)
    {
        var result = new SortedSet<int>();
        foreach (var part in field.Split(','))
        {
            var step = 1;
            var range = part;
            var slash = part.IndexOf('/', StringComparison.Ordinal);
            if (slash >= 0)
            {
                step = Number(part[(slash + 1)..], 1, max - min + 1, what);
                range = part[..slash];
            }

            int from, to;
            if (range is "*" or "?")
            {
                from = min;
                to = max;
            }
            else if (range.Contains('-', StringComparison.Ordinal))
            {
                var bounds = range.Split('-');
                if (bounds.Length != 2)
                {
                    throw new FormatException($"invalid range '{range}' in {what}");
                }

                from = Value(bounds[0], min, max, names, what);
                to = Value(bounds[1], min, max, names, what);
            }
            else
            {
                from = Value(range, min, max, names, what);
                to = slash >= 0 ? max : from;
            }

            if (from <= to)
            {
                for (var v = from; v <= to; v += step)
                {
                    result.Add(v);
                }
            }
            else
            {
                // Wrapping range, such as FRI-MON or 22-2: walk from 'from' past 'max' back to 'min', up to 'to'.
                var span = max - min + 1;
                var length = (to + span) - from;
                for (var offset = 0; offset <= length; offset += step)
                {
                    var v = from + offset;
                    result.Add(v > max ? v - span : v);
                }
            }
        }

        return result.ToArray();
    }

    private static int Value(string text, int min, int max, string[]? names, string what)
    {
        if (names is not null)
        {
            var index = Array.IndexOf(names, text);
            if (index >= 0)
            {
                return index + min;
            }
        }

        return Number(text, min, max, what);
    }

    private static int Number(string text, int min, int max, string what)
    {
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max)
        {
            return value;
        }

        throw new FormatException($"invalid {what} value '{text}' (allowed {min}-{max})");
    }

    private sealed class DayOfMonthRule
    {
        public bool Ignore { get; init; }
        public HashSet<int>? Days { get; init; }
        public int? LastOffset { get; init; }
        public bool LastWeekday { get; init; }
        public int? NearestWeekday { get; init; }

        public static DayOfMonthRule Any(bool ignore) => new() { Ignore = ignore, Days = Enumerable.Range(1, 31).ToHashSet() };

        public bool Matches(DateOnly day)
        {
            var daysInMonth = DateTime.DaysInMonth(day.Year, day.Month);
            if (Days is not null)
            {
                return Days.Contains(day.Day);
            }

            if (LastOffset is { } offset)
            {
                return day.Day == daysInMonth - offset;
            }

            if (LastWeekday)
            {
                return day == Nearest(new DateOnly(day.Year, day.Month, daysInMonth));
            }

            if (NearestWeekday is { } target)
            {
                return target <= daysInMonth && day == Nearest(new DateOnly(day.Year, day.Month, target));
            }

            return false;
        }

        // The weekday nearest to the date, without leaving the month.
        private static DateOnly Nearest(DateOnly date)
        {
            var daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);
            return date.DayOfWeek switch
            {
                DayOfWeek.Saturday => date.Day == 1 ? date.AddDays(2) : date.AddDays(-1),
                DayOfWeek.Sunday => date.Day == daysInMonth ? date.AddDays(-2) : date.AddDays(1),
                _ => date,
            };
        }
    }

    private sealed class DayOfWeekRule
    {
        public bool Ignore { get; init; }
        public HashSet<int>? Days { get; init; }
        public int? LastDay { get; init; }
        public int? NthDay { get; init; }
        public int? Nth { get; init; }

        public static DayOfWeekRule Any(bool ignore) => new() { Ignore = ignore, Days = Enumerable.Range(1, 7).ToHashSet() };

        public bool Matches(DateOnly day)
        {
            var quartzDay = (int)day.DayOfWeek + 1;
            if (Days is not null)
            {
                return Days.Contains(quartzDay);
            }

            if (LastDay is { } last)
            {
                return quartzDay == last && day.AddDays(7).Month != day.Month;
            }

            if (NthDay is { } nthDay && Nth is { } nth)
            {
                return quartzDay == nthDay && ((day.Day - 1) / 7) + 1 == nth;
            }

            return false;
        }
    }
}
