using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.App.Tests;

public class AutomationFixAndSoundTests
{
    [Theory]
    [InlineData(AutomationProblem.TriggerError, WizardFocus.Trigger)]
    [InlineData(AutomationProblem.TriggerMissing, WizardFocus.Trigger)]
    [InlineData(AutomationProblem.ShortcutMissing, WizardFocus.Shortcut)]
    [InlineData(AutomationProblem.LastRunFailed, WizardFocus.None)]
    public void Fix_button_opens_at_the_part_that_needs_fixing(AutomationProblem problem, WizardFocus expected) =>
        Assert.Equal(expected, AutomationFixTarget.For(problem));

    [Theory]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(3, false)]
    [InlineData(2, false)]
    [InlineData(-1, false)]
    public void Sound_plays_only_when_windows_accepts_notifications(int state, bool expected) =>
        Assert.Equal(expected, NotificationState.AllowsSound(state));

    [Fact]
    public void Sound_query_error_means_silent()
    {
        Assert.False(NotificationState.AllowsSound((Func<int>)(() => throw new InvalidOperationException())));
        Assert.True(NotificationState.AllowsSound((Func<int>)(() => 5)));
    }

    [Fact]
    public void Ran_times_line_is_hidden_when_never_ran() => Assert.Null(RanTimes.Text(0, null, DateTimeOffset.Now));

    [Fact]
    public void Ran_times_line_says_count_and_last_time()
    {
        var now = new DateTimeOffset(2026, 11, 17, 15, 0, 0, TimeSpan.Zero).ToLocalTime();
        var last = now.AddHours(-1);
        var text = RanTimes.Text(12, last, now)!;
        Assert.StartsWith("Ran 12 times. Last: ", text);
        Assert.Contains("today at", text);
        Assert.StartsWith("Ran 1 time. Last: ", RanTimes.Text(1, last, now));
        Assert.Contains("yesterday at", RanTimes.Text(2, now.AddDays(-1), now));
    }

    [Fact]
    public void Ran_times_counts_only_finished_runs_from_automations_of_that_shortcut()
    {
        var id = Guid.NewGuid();
        var t = DateTimeOffset.Now;
        RunRecord R(Guid s, RunSource src, RunStatus st, double mins) => new() { ShortcutId = s, Source = src, Status = st, Started = t.AddMinutes(mins) };
        var (count, last) = RanTimes.For(id, new[]
        {
            R(id, RunSource.Automation, RunStatus.Succeeded, -30), R(id, RunSource.Automation, RunStatus.Failed, -5),
            R(id, RunSource.Library, RunStatus.Succeeded, -1), R(Guid.NewGuid(), RunSource.Automation, RunStatus.Succeeded, -2),
            R(id, RunSource.Automation, RunStatus.Cancelled, -1),
        });
        Assert.Equal(2, count);
        Assert.Equal(t.AddMinutes(-5), last);
    }
}
