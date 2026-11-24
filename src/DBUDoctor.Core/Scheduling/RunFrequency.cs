using System.Globalization;
using DBUDoctor.Core.Model;

namespace DBUDoctor.Core.Scheduling;

/// <summary>How often a job runs. <see cref="RunsPerMonth"/> is null when it cannot be known from configuration.</summary>
public sealed record RunFrequency(
    decimal? RunsPerMonth,
    bool AlwaysOn,
    string Description,
    string? UnknownReason,
    TimeSpan? ShortestInterval,
    string? CronExpression)
{
    public const decimal HoursPerMonth = 730m;

    public static RunFrequency Continuous { get; } = new(null, true, "continuous", null, null, null);

    public static RunFrequency Unknown(string description, string reason) => new(null, false, description, reason, null, null);

    /// <summary>
    /// Runs per month for a job. Cron schedules count the fire times in the 365 days after
    /// <paramref name="reference"/>, divided by 12, in the job's time zone.
    /// </summary>
    public static RunFrequency ForJob(Job job, DateTimeOffset reference)
    {
        if (job.Continuous is { } continuous)
        {
            return IsPaused(continuous.PauseStatus)
                ? new RunFrequency(0, false, "continuous (paused)", null, null, null)
                : Continuous;
        }

        if (job.Schedule is { } schedule)
        {
            if (IsPaused(schedule.PauseStatus))
            {
                return new RunFrequency(0, false, "paused", null, null, schedule.QuartzCronExpression);
            }

            return ForCron(schedule.QuartzCronExpression, schedule.TimezoneId, reference);
        }

        if (job.Trigger is { } trigger)
        {
            if (IsPaused(trigger.PauseStatus))
            {
                return new RunFrequency(0, false, "paused", null, null, null);
            }

            if (trigger.Periodic is { } periodic)
            {
                var hours = periodic.Unit?.ToUpperInvariant() switch
                {
                    "HOURS" => periodic.Interval,
                    "DAYS" => periodic.Interval * 24m,
                    "WEEKS" => periodic.Interval * 168m,
                    _ => (decimal?)null,
                };
                if (hours is not > 0)
                {
                    return Unknown("periodic trigger", $"unsupported periodic unit '{periodic.Unit}'");
                }

                var runs = 8760m / hours.Value / 12m;
                var interval = TimeSpan.FromHours((double)hours.Value);
                return new RunFrequency(runs, false, Humanize(interval), null, interval, null);
            }

            if (trigger.FileArrival is not null)
            {
                return Unknown("file-arrival trigger", "event-driven (file arrival); observed runs arrive with calibration");
            }

            if (trigger.TableUpdate is not null || trigger.Table is not null)
            {
                return Unknown("table-update trigger", "event-driven (table update); observed runs arrive with calibration");
            }
        }

        return Unknown("no schedule", "no schedule or trigger (runs on demand)");
    }

    public static RunFrequency ForCron(string? expression, string? timezoneId, DateTimeOffset reference)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return Unknown("schedule", "schedule has no quartz_cron_expression");
        }

        if (!QuartzCron.TryParse(expression, out var cron, out var error))
        {
            return Unknown($"cron `{expression}`", $"cannot parse cron `{expression}`: {error}");
        }

        TimeZoneInfo zone;
        try
        {
            zone = string.IsNullOrWhiteSpace(timezoneId) || timezoneId == "UTC" ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return Unknown($"cron `{expression}`", $"unknown time zone '{timezoneId}'");
        }

        var fires = cron!.CountFires(reference, reference.AddDays(365), zone);
        var runs = fires / 12m;
        var shortest = cron.ShortestInterval(reference, zone);
        var description = shortest is { } s && IsRegular(runs, s) ? Humanize(s) : $"cron `{expression}`";
        return new RunFrequency(runs, false, description, null, shortest, expression);
    }

    /// <summary>The reference instant: 1 January of the current UTC year.</summary>
    public static DateTimeOffset DefaultReference(TimeProvider time)
    {
        var now = time.GetUtcNow();
        return new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private static bool IsPaused(string? status) => string.Equals(status, "PAUSED", StringComparison.OrdinalIgnoreCase);

    // A schedule is regular when it fires at its shortest interval all month long.
    private static bool IsRegular(decimal runsPerMonth, TimeSpan interval)
    {
        if (runsPerMonth <= 0 || interval <= TimeSpan.Zero)
        {
            return false;
        }

        var expected = HoursPerMonth / (decimal)interval.TotalHours;
        return Math.Abs(runsPerMonth - expected) <= expected * 0.02m;
    }

    public static string Humanize(TimeSpan interval)
    {
        if (interval == TimeSpan.FromHours(1)) return "hourly";
        if (interval == TimeSpan.FromDays(1)) return "daily";
        if (interval == TimeSpan.FromDays(7)) return "weekly";
        if (interval.TotalDays >= 1 && interval.TotalDays == Math.Floor(interval.TotalDays)) return $"every {interval.TotalDays.ToString(CultureInfo.InvariantCulture)} days";
        if (interval.TotalHours >= 1 && interval.TotalHours == Math.Floor(interval.TotalHours)) return $"every {interval.TotalHours.ToString(CultureInfo.InvariantCulture)} h";
        if (interval.TotalMinutes >= 1 && interval.TotalMinutes == Math.Floor(interval.TotalMinutes)) return $"every {interval.TotalMinutes.ToString(CultureInfo.InvariantCulture)} min";
        return $"every {interval.TotalSeconds.ToString(CultureInfo.InvariantCulture)} s";
    }
}
