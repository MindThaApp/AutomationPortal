using AutomationPortal.App.Logic;
using AutomationPortal.App.Services;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.App.Tests;

public class DelightMomentsTests
{
    [Fact]
    public void First_automation_run_is_taken_once()
    {
        var data = new LocalUiSettingsData();
        Assert.True(FirstAutomationRun.TryTake(data));
        Assert.True(data.FirstAutomationRunShown);
        Assert.False(FirstAutomationRun.TryTake(data));
    }

    [Fact]
    public void First_automation_run_says_what_it_did()
    {
        var bar = new RunResultBarModel("r", "Tidy", "moved 14 files", null, true);
        Assert.Equal("It moved 14 files.", FirstAutomationRun.Body(bar));
        Assert.Equal("It ran without problems.", FirstAutomationRun.Body(null));
        Assert.Equal("\"Tidy\" just ran by itself for the first time", FirstAutomationRun.Title("Tidy"));
    }

    [Theory]
    [InlineData(RunSource.Library, TileRunState.Succeeded, RunSound.Done)]
    [InlineData(RunSource.Hotkey, TileRunState.Failed, RunSound.Problem)]
    [InlineData(RunSource.Automation, TileRunState.Succeeded, RunSound.None)]
    [InlineData(RunSource.Automation, TileRunState.Failed, RunSound.None)]
    [InlineData(RunSource.Reminder, TileRunState.Succeeded, RunSound.None)]
    [InlineData(RunSource.Library, TileRunState.Idle, RunSound.None)]
    public void Sound_only_for_runs_the_person_started(RunSource source, TileRunState state, RunSound expected) =>
        Assert.Equal(expected, RunSounds.For(true, source, state));

    [Fact]
    public void Sound_is_off_unless_switched_on()
    {
        Assert.False(new LocalUiSettingsData().PlayRunSounds);
        Assert.Equal(RunSound.None, RunSounds.For(false, RunSource.Library, TileRunState.Succeeded));
    }

    private static RunRecord Rec(RunSource s, DateTimeOffset at, RunStatus st = RunStatus.Succeeded) => new() { Source = s, Started = at, Status = st };

    [Fact]
    public void Counts_name_the_period_and_only_count_finished_runs()
    {
        var now = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
        var records = new[]
        {
            Rec(RunSource.Library, now.AddDays(-14)), Rec(RunSource.Automation, now.AddDays(-2)), Rec(RunSource.Automation, now.AddDays(-1), RunStatus.Failed),
            Rec(RunSource.Library, now.AddDays(-1), RunStatus.Cancelled),
        };
        var text = RunCounts.Text(records, now)!;
        Assert.Contains("3 runs, 2 by themselves.", text);
        Assert.StartsWith("Since " + now.AddDays(-14).ToString("d MMMM", System.Globalization.CultureInfo.CurrentCulture), text);
        Assert.Null(RunCounts.Text(Array.Empty<RunRecord>(), now));
    }
}
