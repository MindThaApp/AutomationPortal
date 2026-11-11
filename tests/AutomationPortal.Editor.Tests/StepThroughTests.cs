using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using AutomationPortal.Editor.Logic;
using Xunit;

namespace AutomationPortal.Editor.Tests;

public class StepThroughTests
{
    private static Shortcut Three()
    {
        var s = new Shortcut { Name = "Steps" };
        foreach (var u in new[] { "t1", "t2", "t3" })
            s.Actions.Add(ActionInstance.Create("content.text", ("text", u)).WithUid(u));
        return s;
    }

    private static async Task WaitPausedAsync(RunController rc, string uid)
    {
        for (var i = 0; i < 500 && rc.PausedUid != uid; i++) await Task.Delay(10);
        Assert.Equal(uid, rc.PausedUid);
    }

    [Fact]
    public async Task Step_by_step_pauses_before_each_step_and_next_step_continues()
    {
        using var host = new TestHost();
        var rc = new RunController { StepByStep = true };
        var run = rc.RunAsync(Three(), host.Runner);

        await WaitPausedAsync(rc, "t1");
        Assert.Equal(StepState.Paused, rc.StepOf("t1")!.State);
        Assert.Empty(rc.Outputs());

        rc.NextStep();
        await WaitPausedAsync(rc, "t2");
        Assert.Equal("t1", rc.Outputs()["t1"].AsText());
        Assert.Equal(StepState.Succeeded, rc.StepOf("t1")!.State);

        rc.NextStep();
        await WaitPausedAsync(rc, "t3");
        rc.NextStep();
        var r = await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(RunStatus.Succeeded, r.Status);
        Assert.False(rc.IsPaused);
    }

    [Fact]
    public async Task Run_to_end_stops_pausing()
    {
        using var host = new TestHost();
        var rc = new RunController { StepByStep = true };
        var run = rc.RunAsync(Three(), host.Runner);
        await WaitPausedAsync(rc, "t1");
        rc.RunToEnd();
        var r = await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(RunStatus.Succeeded, r.Status);
        Assert.Equal(3, r.Steps.Count);
    }

    [Fact]
    public async Task Stop_while_paused_cancels_the_run()
    {
        using var host = new TestHost();
        var rc = new RunController { StepByStep = true };
        var run = rc.RunAsync(Three(), host.Runner);
        await WaitPausedAsync(rc, "t1");
        rc.Cancel();
        var r = await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(RunStatus.Cancelled, r.Status);
        Assert.Empty(r.Steps);
        Assert.False(rc.IsPaused);
    }

    [Fact]
    public async Task A_pause_here_step_pauses_only_there()
    {
        using var host = new TestHost();
        var rc = new RunController();
        rc.SetBreakpoint("t2", true);
        var run = rc.RunAsync(Three(), host.Runner);
        await WaitPausedAsync(rc, "t2");
        Assert.Equal(StepState.Succeeded, rc.StepOf("t1")!.State);
        rc.NextStep();
        var r = await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(RunStatus.Succeeded, r.Status);
    }

    [Fact]
    public async Task Pause_here_is_ignored_by_runs_that_do_not_come_from_the_editor_controller()
    {
        using var host = new TestHost();
        var rc = new RunController();
        rc.SetBreakpoint("t2", true);
        var s = Three();
        var r = await host.Runner.RunAsync(s, ContentValue.Empty, new RunOptions { Source = RunSource.Library });
        Assert.Equal(RunStatus.Succeeded, r.Status);
        Assert.False(rc.IsPaused);
    }

    [Fact]
    public void Pause_here_is_never_part_of_the_saved_shortcut()
    {
        var rc = new RunController();
        var s = Three();
        var before = System.Text.Json.JsonSerializer.Serialize(s);
        rc.SetBreakpoint("t2", true);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(s));
        Assert.True(rc.HasBreakpoint("t2"));
        rc.SetBreakpoint("t2", false);
        Assert.False(rc.HasBreakpoint("t2"));
    }
}
