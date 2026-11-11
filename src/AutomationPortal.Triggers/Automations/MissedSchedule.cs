using AutomationPortal.Core;

namespace AutomationPortal.Triggers;

/// <summary>Finds scheduled times that passed while Automation Portal wasn't running, the PC was off or asleep. Pure maths, no side effects.</summary>
internal static class MissedSchedule
{
    /// <summary>Times older than this are never caught up.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    /// <summary>The latest scheduled time after <paramref name="since"/> and up to <paramref name="now"/>, or null when none (or the trigger isn't a schedule).</summary>
    public static DateTime? LatestMissed(TriggerProvider provider, TriggerInstance trigger, DateTimeOffset since, DateTimeOffset now)
    {
        var from = since.LocalDateTime;
        var to = now.LocalDateTime;
        if (provider is not (DailyTrigger or MonthlyTrigger or IntervalTrigger or OnceTrigger) || from >= to) return null;
        var config = TriggerConfig.From(trigger, provider.Descriptor.Parameters);

        switch (provider)
        {
            case DailyTrigger:
                var time = config.GetTime("time") ?? new TimeOnly(8, 0);
                var days = TP.ParseDays(config.GetStrings("days"));
                return Latest(from, to, after => ScheduleCalculator.NextDaily(after, time, days));
            case MonthlyTrigger:
                var rule = MonthlyTrigger.RuleFrom(config);
                var monthTime = config.GetTime("time") ?? new TimeOnly(9, 0);
                return Latest(from, to, after => ScheduleCalculator.NextMonthly(after, rule, monthTime));
            case IntervalTrigger:
                var every = Math.Max(1, config.GetNumber("every", 30));
                var interval = config.GetChoice("unit", "minutes") == "hours" ? TimeSpan.FromHours(every) : TimeSpan.FromMinutes(every);
                ActiveHours? hours = config.GetChoice("window", "always") == "limited"
                    ? new ActiveHours(config.GetTime("from") ?? new TimeOnly(9, 0), config.GetTime("to") ?? new TimeOnly(17, 0)) : null;
                return Latest(from, to, after => ScheduleCalculator.NextInterval(after, interval, hours));
            default:
                var when = config.GetDate("when")?.LocalDateTime;
                return when is { } w && w > from && w <= to ? w : null;
        }
    }

    private static DateTime? Latest(DateTime from, DateTime to, Func<DateTime, DateTime?> next)
    {
        DateTime? latest = null;
        var t = next(from);
        for (var guard = 0; t is { } c && c <= to && guard < 2000; guard++)
        {
            latest = c;
            t = next(c);
        }
        return latest;
    }
}
