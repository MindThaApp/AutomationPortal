using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

/// <summary>One of the most common ways to start an automation, shown on the "Popular" row.</summary>
public sealed record PopularTrigger(string TriggerId, string Title, string Hint);

/// <summary>The popular-trigger list, defaults and plain-language "when" text for the one-sentence automation flow.</summary>
public static class WhenSummary
{
    /// <summary>The time a new schedule starts at.</summary>
    public static readonly TimeOnly DefaultTime = new(8, 0);

    public static IReadOnlyList<PopularTrigger> Popular { get; } = new[]
    {
        new PopularTrigger("time.daily", Strings.Get("WhenSummary_EveryDayAtA"), Strings.Get("WhenSummary_StartsAt0800")),
        new PopularTrigger("key.shortcut", Strings.Get("WhenSummary_WhenIPressA"), Strings.Get("WhenSummary_AnywhereInWindows")),
        new PopularTrigger("system.signIn", Strings.Get("WhenSummary_WhenISignIn"), Strings.Get("WhenSummary_EachTimeYouStart")),
        new PopularTrigger("file.changed", Strings.Get("WhenSummary_WhenAFileAppears"), Strings.Get("WhenSummary_SuchAsANew")),
        new PopularTrigger("app.opened", Strings.Get("WhenSummary_WhenAnAppOpens"), Strings.Get("WhenSummary_PickTheAppNext")),
        new PopularTrigger("net.wifiJoined", Strings.Get("WhenSummary_WhenIJoinWi"), Strings.Get("WhenSummary_AnyNetworkOrOne")),
        new PopularTrigger("device.usb", Strings.Get("WhenSummary_WhenAUSBDevice"), Strings.Get("WhenSummary_OrAnyDeviceThat")),
    };

    /// <summary>The popular triggers that this build actually has, in the popular order.</summary>
    public static IReadOnlyList<PopularTrigger> AvailablePopular(TriggerRegistry triggers) =>
        Popular.Where(p => triggers.Get(p.TriggerId) is not null).ToList();

    /// <summary>"Every day at 08:00", "Every weekday at 08:00", "Every Mon, Wed at 08:00".</summary>
    public static string DailyText(TimeOnly time, IReadOnlyCollection<string> days)
    {
        var at = time.ToString("HH':'mm", System.Globalization.CultureInfo.InvariantCulture);
        var set = new HashSet<string>(days.Select(d => d.ToLowerInvariant()));
        string[] week = { "mon", "tue", "wed", "thu", "fri" };
        string[] weekend = { "sat", "sun" };
        if (set.Count == 0 || set.Count == 7) return Strings.Get("WhenSummary_EveryDayAt") + at;
        if (set.SetEquals(week)) return Strings.Get("WhenSummary_EveryWeekdayAt") + at;
        if (set.SetEquals(weekend)) return Strings.Get("WhenSummary_EveryWeekendDayAt") + at;
        string[] order = { "mon", "tue", "wed", "thu", "fri", "sat", "sun" };
        var names = order.Where(set.Contains).Select(d => char.ToUpperInvariant(d[0]) + d.Substring(1));
        return "Every " + string.Join(", ", names) + " at " + at;
    }

    /// <summary>The "when" half of the sentence for a trigger and its values.</summary>
    public static string Text(string triggerId, IReadOnlyDictionary<string, ParamValue> values, TriggerRegistry triggers)
    {
        var d = triggers.Get(triggerId)?.Descriptor;
        if (d is null) return "";
        if (string.Equals(triggerId, "time.daily", StringComparison.OrdinalIgnoreCase))
        {
            var time = values.TryGetValue("time", out var t) && t is TimeValue tv ? tv.Value : DefaultTime;
            var days = values.TryGetValue("days", out var l) && l is ListValue lv ? lv.Items.Select(i => i.ToLiteralText()).ToList() : new List<string>();
            return DailyText(time, days);
        }
        return AutomationSentence.RenderSummary(d.SummarySegments, d.Parameters, values);
    }
}
