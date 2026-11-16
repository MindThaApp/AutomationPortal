using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.App.Tests;

public class NextRunTextTests
{
    private static readonly DateTime Mon9 = new(2026, 11, 16, 9, 0, 0);   // a Monday

    private static Dictionary<string, ParamValue> Daily(string time, params string[] days) =>
        new() { ["time"] = ParamValue.Time(time), ["days"] = ParamValue.List(days) };

    [Fact]
    public void Daily_before_the_time_runs_today() =>
        Assert.Equal("Next run: today at 18:00", NextRunText.For("time.daily", Daily("18:00", "mon", "tue", "wed", "thu", "fri", "sat", "sun"), Mon9));

    [Fact]
    public void Daily_after_the_time_runs_tomorrow() =>
        Assert.Equal("Next run: tomorrow at 08:00", NextRunText.For("time.daily", Daily("08:00", "mon", "tue", "wed", "thu", "fri", "sat", "sun"), Mon9));

    [Fact]
    public void Weekdays_skip_the_weekend_and_name_the_day()
    {
        var friNight = new DateTime(2026, 11, 20, 20, 0, 0);
        Assert.Equal("Next run: Monday at 08:00", NextRunText.For("time.daily", Daily("08:00", "mon", "tue", "wed", "thu", "fri"), friNight));
    }

    [Fact]
    public void Far_dates_use_the_day_and_month() =>
        Assert.StartsWith("3 ", NextRunText.Describe(Mon9, new DateTime(2026, 12, 3, 8, 0, 0)).Substring(0, 2));

    [Fact]
    public void Other_triggers_have_no_next_run() =>
        Assert.Null(NextRunText.For("hotkey", new Dictionary<string, ParamValue>(), Mon9));

    [Fact]
    public void Chips_set_the_time_and_days_and_report_themselves_on()
    {
        var values = new Dictionary<string, ParamValue>();
        var weekdays = ScheduleChip.All.Single(c => c.Label == "Weekdays");
        var noon = ScheduleChip.All.Single(c => c.Label == "12:00");
        weekdays.Apply(values); noon.Apply(values);
        Assert.True(weekdays.IsOn(values));
        Assert.True(noon.IsOn(values));
        Assert.False(ScheduleChip.All.Single(c => c.Label == "Every day").IsOn(values));
        Assert.Equal("Next run: tomorrow at 12:00", NextRunText.For("time.daily", values, new DateTime(2026, 11, 16, 13, 0, 0)));
    }

    [Fact]
    public void Chips_only_apply_to_the_daily_schedule()
    {
        Assert.True(ScheduleChip.AppliesTo("time.daily"));
        Assert.False(ScheduleChip.AppliesTo("time.monthly"));
    }
}
