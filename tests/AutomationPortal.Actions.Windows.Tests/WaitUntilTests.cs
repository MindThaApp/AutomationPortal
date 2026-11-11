using AutomationPortal.Actions.Windows.Apps;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;

namespace AutomationPortal.Actions.Windows.Tests;

public class WaitUntilTests
{
    private sealed class Fake
    {
        public DateTime Now = new(2026, 1, 1, 10, 0, 0);
        public HashSet<string> Paths = new();
        public TimeSpan Idle;
        public bool App, Clip, Site;
        public int Delays;
        public Action? OnDelay;

        public WaitProbes Probes() => new()
        {
            Now = () => Now,
            PathExists = p => Paths.Contains(p),
            IdleFor = () => Idle,
            AppRunning = _ => Task.FromResult(App),
            ClipboardHasText = _ => Task.FromResult(Clip),
            SiteUp = _ => Task.FromResult(Site),
            Delay = (t, ct) => { ct.ThrowIfCancellationRequested(); Delays++; Now += t; OnDelay?.Invoke(); return Task.CompletedTask; },
        };
    }

    private static Task<bool> Run(Fake f, string condition, TimeSpan timeout, TimeOnly? time = null, string path = "x", CancellationToken ct = default)
    {
        var p = f.Probes();
        var check = WaitUntil.Build(condition, p, time ?? TimeOnly.MinValue, path, TimeSpan.FromMinutes(5));
        return WaitUntil.RunAsync(check, TimeSpan.FromSeconds(5), timeout, p, ct);
    }

    [Fact]
    public async Task Time_of_day_waits_until_reached()
    {
        var f = new Fake();
        Assert.True(await Run(f, "time", TimeSpan.FromMinutes(10), new TimeOnly(10, 3)));
        Assert.True(f.Now >= new DateTime(2026, 1, 1, 10, 3, 0));
        Assert.True(f.Now < new DateTime(2026, 1, 1, 10, 4, 0));
    }

    [Fact]
    public async Task Time_of_day_that_has_passed_means_tomorrow()
    {
        var f = new Fake();
        Assert.False(await Run(f, "time", TimeSpan.FromMinutes(10), new TimeOnly(9, 0)));
    }

    [Fact]
    public async Task File_exists_and_file_gone()
    {
        var f = new Fake();
        f.OnDelay = () => f.Paths.Add("a.txt");
        Assert.True(await Run(f, "fileExists", TimeSpan.FromMinutes(1), path: "a.txt"));
        Assert.Equal(1, f.Delays);

        var g = new Fake();
        g.Paths.Add("a.txt");
        g.OnDelay = () => g.Paths.Clear();
        Assert.True(await Run(g, "fileGone", TimeSpan.FromMinutes(1), path: "a.txt"));
    }

    [Fact]
    public async Task App_running_and_not_running()
    {
        var f = new Fake { App = false };
        f.OnDelay = () => f.App = true;
        Assert.True(await Run(f, "appRunning", TimeSpan.FromMinutes(1)));
        f.OnDelay = () => f.App = false;
        Assert.True(await Run(f, "appNotRunning", TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task Idle_clipboard_and_site()
    {
        var f = new Fake();
        f.OnDelay = () => f.Idle = TimeSpan.FromMinutes(6);
        Assert.True(await Run(f, "idle", TimeSpan.FromMinutes(1)));
        f.OnDelay = () => f.Clip = true;
        Assert.True(await Run(f, "clipboardText", TimeSpan.FromMinutes(1)));
        f.OnDelay = () => f.Site = true;
        Assert.True(await Run(f, "siteUp", TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task Times_out_and_zero_means_no_limit()
    {
        var f = new Fake();
        Assert.False(await Run(f, "fileExists", TimeSpan.FromSeconds(12)));
        Assert.Equal(new DateTime(2026, 1, 1, 10, 0, 12), f.Now);

        var g = new Fake();
        g.OnDelay = () => { if (g.Delays == 100) g.Paths.Add("x"); };
        Assert.True(await Run(g, "fileExists", TimeSpan.Zero));
    }

    [Fact]
    public async Task Cancelling_stops_the_wait()
    {
        var f = new Fake();
        using var cts = new CancellationTokenSource();
        f.OnDelay = () => cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(f, "fileExists", TimeSpan.FromMinutes(1), ct: cts.Token));
        Assert.Equal(1, f.Delays);
    }

    [Fact]
    public async Task Cancelling_the_real_action_ends_it_at_once()
    {
        using var host = new TestHost();
        using var cts = new CancellationTokenSource(200);
        var started = DateTime.UtcNow;
        var action = new WaitUntilAction();
        var step = ActionInstance.Create(action.Descriptor.Id, ("condition", ParamValue.Choice("clipboardText")));
        var scope = new RunScope(host.Services, "Test", ContentValue.Empty, new RunOptions { CancellationToken = cts.Token });
        var ctx = new ActionContext(scope, step, action.Descriptor, ContentValue.Empty);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => action.RunAsync(ctx));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Timeout_can_stop_with_a_message_or_carry_on()
    {
        using var host = new TestHost();
        var stop = await Assert.ThrowsAsync<ActionException>(() => ActionTestHarness.RunAsync(host, new WaitUntilAction(),
            ("condition", ParamValue.Choice("clipboardText")), ("timeout", ParamValue.Duration(1)), ("onTimeout", ParamValue.Choice("stop"))));
        Assert.Contains("didn't happen", stop.Message);
        await ActionTestHarness.RunAsync(host, new WaitUntilAction(),
            ("condition", ParamValue.Choice("clipboardText")), ("timeout", ParamValue.Duration(1)), ("onTimeout", ParamValue.Choice("continue")));
    }
}
