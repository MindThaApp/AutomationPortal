using AutomationPortal.Core;

namespace AutomationPortal.Triggers;

/// <summary>When a schedule automation will next fire, from its settings, using the same calculator the scheduler uses.</summary>
public static class NextScheduledRun
{
    /// <summary>The next time after <paramref name="now"/>, or null when the trigger isn't a schedule or has nothing coming up.</summary>
    public static DateTime? Find(string triggerId, IReadOnlyDictionary<string, ParamValue> values, DateTime now)
    {
        var config = new TriggerConfig(values);
        switch (triggerId)
        {
            case "time.daily":
                var days = TP.ParseDays(config.GetStrings("days"));
                if (values.ContainsKey("days") && days.Count == 0) return null;
                if (days.Count == 0) days = ScheduleCalculator.EveryDay;
                return ScheduleCalculator.NextDaily(now, config.GetTime("time") ?? new TimeOnly(8, 0), days);
            case "time.monthly":
                return ScheduleCalculator.NextMonthly(now, MonthlyTrigger.RuleFrom(config), config.GetTime("time") ?? new TimeOnly(9, 0));
            case "time.interval":
                var every = Math.Max(1, config.GetNumber("every", 30));
                var interval = config.GetChoice("unit", "minutes") == "hours" ? TimeSpan.FromHours(every) : TimeSpan.FromMinutes(every);
                ActiveHours? hours = config.GetChoice("window", "always") == "limited"
                    ? new ActiveHours(config.GetTime("from") ?? new TimeOnly(9, 0), config.GetTime("to") ?? new TimeOnly(17, 0)) : null;
                return ScheduleCalculator.NextInterval(now, interval, hours);
            case "time.once":
                return config.GetDate("when")?.LocalDateTime is { } w && w > now ? w : null;
            default:
                return null;
        }
    }
}
