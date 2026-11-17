using AutomationPortal.App.Logic;
using Xunit;

namespace AutomationPortal.App.Tests;

public class TrayTooltipTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 7, 0, 0);
    private static readonly string[] None = Array.Empty<string>();

    [Fact]
    public void Idle_with_nothing_planned_is_just_the_app_name() =>
        Assert.Equal("Automation Portal", TrayTooltip.Build(null, None, null, null, Now));

    [Fact]
    public void Names_the_shortcut_that_is_running() =>
        Assert.Equal("Automation Portal\nRunning \"Tidy Downloads\"", TrayTooltip.Build(null, new[] { "Tidy Downloads" }, null, null, Now));

    [Fact]
    public void Counts_several_running_shortcuts() =>
        Assert.EndsWith("Running 3 shortcuts", TrayTooltip.Build(null, new[] { "a", "b", "c" }, null, null, Now));

    [Fact]
    public void Says_when_the_last_run_did_not_finish() =>
        Assert.EndsWith("\"Backup\" didn't finish", TrayTooltip.Build(null, None, "Backup", null, Now));

    [Fact]
    public void Says_what_comes_next() =>
        Assert.EndsWith("Next: Tidy Downloads today at 08:00", TrayTooltip.Build(null, None, null, ("Tidy Downloads", new DateTime(2026, 11, 17, 8, 0, 0)), Now));

    [Fact]
    public void Paused_wins_over_running() =>
        Assert.EndsWith("Paused", TrayTooltip.Build("Paused", new[] { "a" }, "b", ("c", Now.AddHours(1)), Now));

    [Fact]
    public void Running_wins_over_a_failure_and_the_next_run() =>
        Assert.Contains("Running \"a\"", TrayTooltip.Build(null, new[] { "a" }, "b", ("c", Now.AddHours(1)), Now));

    [Fact]
    public void Never_longer_than_the_tooltip_allows()
    {
        var long1 = new string('x', 300);
        foreach (var text in new[]
        {
            TrayTooltip.Build(null, new[] { long1 }, null, null, Now),
            TrayTooltip.Build(null, None, long1, null, Now),
            TrayTooltip.Build(null, None, null, (long1, Now.AddDays(20)), Now),
            TrayTooltip.Build(long1, None, null, null, Now),
        })
            Assert.True(text.Length <= TrayTooltip.MaxLength, text.Length.ToString());
    }
}
