using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.App.Tests;

public class TimeSavedTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid();

    private static RunRecord Run(Guid id, DateTimeOffset when, RunStatus status = RunStatus.Succeeded) =>
        new() { RunId = Guid.NewGuid().ToString("N"), ShortcutId = id, Started = when, Status = status };

    [Fact]
    public void Nothing_is_shown_without_an_estimate()
    {
        var runs = new[] { Run(A, Now.AddDays(-1)) };
        Assert.Null(TimeSaved.Text(runs, _ => null, Now));
    }

    [Fact]
    public void Counts_only_shortcuts_with_an_estimate_and_names_the_period()
    {
        var runs = new[] { Run(A, Now.AddDays(-20)), Run(A, Now.AddDays(-3)), Run(A, Now.AddDays(-2)), Run(A, Now.AddDays(-1)), Run(B, Now.AddDays(-1)) };
        var text = TimeSaved.Text(runs, id => id == A ? 60 : null, Now);
        Assert.Equal("About 3 hours saved this month, by your estimates.", text);
    }

    [Fact]
    public void Failed_runs_and_last_months_runs_do_not_count()
    {
        var runs = new[] { Run(A, Now.AddDays(-1), RunStatus.Failed), Run(A, Now.AddDays(-40)) };
        Assert.Null(TimeSaved.Text(runs, _ => 30, Now));
    }

    [Fact]
    public void Says_since_when_records_start_inside_the_month()
    {
        var runs = new[] { Run(A, new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero)) };
        var text = TimeSaved.Text(runs, _ => 10, Now);
        Assert.StartsWith("About 10 minutes saved since ", text);
        Assert.EndsWith(", by your estimates.", text);
    }

    [Theory]
    [InlineData(1, "1 minute")]
    [InlineData(45, "45 minutes")]
    [InlineData(60, "1 hour")]
    [InlineData(90, "1.5 hours")]
    [InlineData(185, "3 hours")]
    [InlineData(1500, "25 hours")]
    public void Duration_wording(long minutes, string expected) => Assert.Equal(expected, TimeSaved.Duration(minutes).Replace(',', '.'));

    [Fact]
    public void Old_shortcut_file_loads_unchanged_and_no_estimate_is_written()
    {
        var old = """{"id":"3f2b8a52-6c0f-4f0b-9a52-1d8a4e5c7b10","name":"Old","icon":"Flash24","color":"blue","actions":[]}""";
        var s = AutomationPortalJson.Deserialize<Shortcut>(old)!;
        Assert.Null(s.MinutesByHand);
        Assert.DoesNotContain("minutesByHand", AutomationPortalJson.Serialize(s), StringComparison.OrdinalIgnoreCase);
        s.MinutesByHand = 20;
        Assert.Equal(20, AutomationPortalJson.Deserialize<Shortcut>(AutomationPortalJson.Serialize(s))!.MinutesByHand);
    }
}
