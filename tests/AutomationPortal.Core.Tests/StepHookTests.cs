using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using Xunit;

namespace AutomationPortal.Core.Tests;

public sealed class StepHookTests : IDisposable
{
    private readonly TestHost _host = new TestHost().With(typeof(EngineTests).Assembly);
    public void Dispose() => _host.Dispose();

    private static Shortcut ThreeSteps(bool middleOff = false)
    {
        var s = new Shortcut { Name = "Hook" };
        s.Actions.Add(ActionInstance.Create("content.text", ("text", "a")).WithUid("s1"));
        var mid = ActionInstance.Create("content.text", ("text", "b")).WithUid("s2");
        mid.Disabled = middleOff;
        s.Actions.Add(mid);
        s.Actions.Add(ActionInstance.Create("content.text", ("text", "c")).WithUid("s3"));
        return s;
    }

    [Fact]
    public async Task Without_a_hook_runs_exactly_as_before()
    {
        var r = await _host.Runner.RunAsync(ThreeSteps(), ContentValue.Empty, new RunOptions { Source = RunSource.Editor });
        Assert.Equal(RunStatus.Succeeded, r.Status);
        Assert.Equal(3, r.Steps.Count);
    }

    [Fact]
    public async Task Hook_runs_before_each_step_that_runs_and_not_for_turned_off_steps()
    {
        var seen = new List<string>();
        var options = new RunOptions { Source = RunSource.Editor, BeforeStep = i => { seen.Add(i.Uid); return Task.CompletedTask; } };
        var r = await _host.Runner.RunAsync(ThreeSteps(middleOff: true), ContentValue.Empty, options);
        Assert.Equal(RunStatus.Succeeded, r.Status);
        Assert.Equal(new[] { "s1", "s3" }, seen);
    }

    [Fact]
    public async Task A_step_waits_until_the_hook_returns()
    {
        var gate = new TaskCompletionSource();
        var reached = new TaskCompletionSource();
        var options = new RunOptions
        {
            Source = RunSource.Editor,
            BeforeStep = async i => { if (i.Uid == "s2") { reached.TrySetResult(); await gate.Task; } },
        };
        var run = _host.Runner.RunAsync(ThreeSteps(), ContentValue.Empty, options);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(run.IsCompleted);
        gate.SetResult();
        var r = await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(RunStatus.Succeeded, r.Status);
        Assert.Equal(3, r.Steps.Count);
    }

    [Fact]
    public async Task Cancelling_while_waiting_stops_the_run_without_running_the_step()
    {
        using var cts = new CancellationTokenSource();
        var reached = new TaskCompletionSource();
        var options = new RunOptions
        {
            Source = RunSource.Editor, CancellationToken = cts.Token,
            BeforeStep = async i =>
            {
                if (i.Uid != "s2") return;
                reached.TrySetResult();
                await Task.Delay(Timeout.Infinite, cts.Token);
            },
        };
        var run = _host.Runner.RunAsync(ThreeSteps(), ContentValue.Empty, options);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();
        var r = await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(RunStatus.Cancelled, r.Status);
        Assert.Single(r.Steps);
    }
}
