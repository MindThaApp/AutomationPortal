using AutomationPortal.Core;
using AutomationPortal.Triggers;

namespace AutomationPortal.App.Logic;

/// <summary>The "Next run: tomorrow at 08:00" line and the quick time and day chips for schedule automations.</summary>
public static class NextRunText
{
    /// <summary>"Next run: tomorrow at 08:00" for an enabled schedule, or null for anything else.</summary>
    public static string? For(string triggerId, IReadOnlyDictionary<string, ParamValue> values, DateTime now)
    {
        var next = NextScheduledRun.Find(triggerId, values, now);
        return next is { } n ? "Next run: " + Describe(now, n) : null;
    }

    /// <summary>"today at 08:00", "tomorrow at 08:00", "Friday at 08:00" within the week, otherwise "3 Dec at 08:00".</summary>
    public static string Describe(DateTime now, DateTime next)
    {
        var time = next.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        var gap = (next.Date - now.Date).Days;
        return gap switch
        {
            <= 0 => "today at " + time,
            1 => "tomorrow at " + time,
            < 7 => next.DayOfWeek + " at " + time,
            _ => next.ToString("d MMM", System.Globalization.CultureInfo.CurrentCulture) + " at " + time,
        };
    }
}

/// <summary>A quick choice under a daily schedule's time and day boxes.</summary>
public sealed record ScheduleChip(string Label, string? Time, string[]? Days)
{
    private static readonly string[] Weekdays = { "mon", "tue", "wed", "thu", "fri" };
    private static readonly string[] Every = { "mon", "tue", "wed", "thu", "fri", "sat", "sun" };

    public static IReadOnlyList<ScheduleChip> All { get; } = new[]
    {
        new ScheduleChip("08:00", "08:00", null), new ScheduleChip("12:00", "12:00", null), new ScheduleChip("18:00", "18:00", null),
        new ScheduleChip("Weekdays", null, Weekdays), new ScheduleChip("Every day", null, Every),
    };

    /// <summary>Only the daily schedule has a time and days to set.</summary>
    public static bool AppliesTo(string? triggerId) => triggerId == "time.daily";

    public void Apply(IDictionary<string, ParamValue> values)
    {
        if (Time is { } t) values["time"] = ParamValue.Time(t);
        if (Days is { } d) values["days"] = ParamValue.List(d);
    }

    public bool IsOn(IReadOnlyDictionary<string, ParamValue> values)
    {
        var config = new TriggerConfig(values);
        if (Time is { } t) return config.GetTime("time") == TimeOnly.Parse(t, System.Globalization.CultureInfo.InvariantCulture);
        var have = config.GetStrings("days");
        if (have.Count == 0 && !values.ContainsKey("days")) have = Every;
        return Days is { } d && have.Count == d.Length && d.All(have.Contains);
    }
}
