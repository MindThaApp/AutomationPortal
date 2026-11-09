using Flowmate.Core;
using Flowmate.Triggers;

namespace Flowmate.Triggers.Tests;

/// <summary>A folder automation must not trigger itself on the files its own run writes ("Rename to {date} {name}" in Downloads).</summary>
public class FolderLoopTests
{
    private const string Folder = @"C:\fake\Downloads\";

    private sealed class FakeFolderTrigger : TriggerProvider, IFileWatchTrigger
    {
        public List<ITriggerSink> Sinks { get; } = new();
        public override TriggerDescriptor Descriptor { get; } = new() { Id = "test.folder", Title = "Folder", Category = "Test", OutputKind = ContentKind.File };
        public override IDisposable Start(TriggerConfig config, ITriggerSink sink) { Sinks.Add(sink); return new DisposableAction(() => { }); }
    }

    private sealed class FakeProbe : IFileProbe
    {
        public Dictionary<string, long> Sizes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool Exists(string path) => Sizes.ContainsKey(path);
        public long? Size(string path) => Sizes.TryGetValue(path, out var s) ? s : null;
        public bool CanOpen(string path) => true;
    }

    /// <summary>Hands fires to the engine and waits for them, so the test runs step by step.</summary>
    private sealed class WaitingSink : ITriggerSink, ITriggerSinkRunWindow
    {
        private readonly ITriggerSink _inner;
        public WaitingSink(ITriggerSink inner) { _inner = inner; }
        public void Fire(ContentValue output, string? description = null) =>
            ((ITriggerSinkExtras)_inner).FireAndWaitAsync(output, description).GetAwaiter().GetResult();
        public bool IsOwnRunRecent(TimeSpan grace) => ((ITriggerSinkRunWindow)_inner).IsOwnRunRecent(grace);
        public bool WasTouchedByOwnRun(string path) => ((ITriggerSinkRunWindow)_inner).WasTouchedByOwnRun(path);
        public TimeSpan CooldownRemaining() => ((ITriggerSinkRunWindow)_inner).CooldownRemaining();
        public bool IsBusy() => ((ITriggerSinkRunWindow)_inner).IsBusy();
        public void NoteSelfFed(bool selfFed) => ((ITriggerSinkRunWindow)_inner).NoteSelfFed(selfFed);
    }

    /// <summary>Plays the shortcut: renames every input file to "2026-11-09 name", raising the watcher events a real rename would.</summary>
    private sealed class RenamingRunner : IShortcutRunner
    {
        private readonly FakeProbe _probe;
        public FolderTriggerEvents? Handler { get; set; }
        public List<IReadOnlyList<string>> Runs { get; } = new();
        /// <summary>Something that happens while the shortcut runs (the user saving another file, say).</summary>
        public Action? DuringRun { get; set; }
        /// <summary>Also writes a new file next to each input without giving it back, as a command would ("magick x.pdf x.pdf.jpg").</summary>
        public bool WritesUnreportedCopies { get; set; }
        public RenamingRunner(FakeProbe probe) { _probe = probe; }

        public Task<RunResult> RunAsync(Shortcut shortcut, ContentValue input, RunOptions? options = null) => RunAsync(shortcut.Id, input, options);

        public Task<RunResult> RunAsync(Guid shortcutId, ContentValue input, RunOptions? options = null)
        {
            var paths = input.OfType<PathItem>().Select(p => p.Path).ToList();
            Runs.Add(paths);
            var outputs = new List<string>();
            foreach (var p in paths)
            {
                if (WritesUnreportedCopies)
                {
                    _probe.Sizes[p + ".jpg"] = 100;
                    Handler!.OnCreated(p + ".jpg");
                    continue;
                }
                var renamed = Path.Combine(Path.GetDirectoryName(p)!, "2026-11-09 " + Path.GetFileName(p));
                _probe.Sizes[renamed] = _probe.Sizes[p];
                _probe.Sizes.Remove(p);
                Handler!.OnRenamed(p, renamed);
                outputs.Add(renamed);
            }
            // Like the real runner: the Rename step gives back the renamed files.
            options?.Observer?.OnStepFinished("rename", ContentValue.Files(outputs), TimeSpan.Zero, null);
            DuringRun?.Invoke();
            DuringRun = null;
            return Task.FromResult(new RunResult { ShortcutId = shortcutId, Source = RunSource.Automation, Status = RunStatus.Succeeded, Started = DateTimeOffset.Now });
        }
    }

    private sealed class Rig : IDisposable
    {
        public EngineFixture F { get; } = new();
        public FakeProbe Probe { get; } = new();
        public RenamingRunner Runner { get; }
        public FolderTriggerEvents Handler { get; }
        public Automation Automation { get; }

        public Rig(int waitSeconds = 3)
        {
            var trigger = new FakeFolderTrigger();
            F.Triggers.Register(trigger);
            Automation = F.Add(triggerId: "test.folder");
            F.Engine.Reload();
            Runner = new RenamingRunner(Probe);
            F.Host.Services.Runner = Runner;
            var wait = TimeSpan.FromSeconds(waitSeconds);
            var batcher = new FolderChangeBatcher(wait, Probe, now: () => F.Now.UtcDateTime);
            Handler = new FolderTriggerEvents(batcher, new WaitingSink(trigger.Sinks.Single()), new HashSet<string> { "added" }, "Downloads", wait);
            Runner.Handler = Handler;
        }

        /// <summary>Lets time pass a second at a time, ticking the trigger like its timer does.</summary>
        public void Pass(int seconds)
        {
            for (var i = 0; i < seconds; i++)
            {
                F.Now += TimeSpan.FromSeconds(1);
                Handler.Tick();
            }
        }

        public void Drop(string name)
        {
            Probe.Sizes[Folder + name] = 100;
            Handler.OnCreated(Folder + name);
        }

        public void Dispose() => F.Dispose();
    }

    [Theory]
    [InlineData(3)]
    [InlineData(10)] // the renamed file would come out after the 5 s minimum cooldown, so only the run window stops it
    public void A_rename_in_the_watched_folder_does_not_trigger_the_automation_again(int waitSeconds)
    {
        using var r = new Rig(waitSeconds);
        r.Drop("x.pdf");
        r.Pass(20);
        Assert.Single(r.Runner.Runs);
        Assert.True(r.Probe.Exists(Folder + "2026-11-09 x.pdf"));

        // The renamed file, and an event the watcher delivers a moment after the run ended, are the run's own output.
        r.Handler.OnChanged(Folder + "2026-11-09 x.pdf");
        r.Pass(120);
        Assert.Single(r.Runner.Runs);
        Assert.False(r.Probe.Exists(Folder + "2026-11-09 2026-11-09 x.pdf"));

        // A file the user adds later still runs the automation, with just that file.
        r.Drop("y.pdf");
        r.Pass(20);
        Assert.Equal(2, r.Runner.Runs.Count);
        Assert.Equal(new[] { Folder + "y.pdf" }, r.Runner.Runs[1]);
        r.Pass(120);
        Assert.Equal(2, r.Runner.Runs.Count);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(10)]
    public void Files_the_user_adds_while_the_shortcut_runs_still_run_it_afterwards(int waitSeconds)
    {
        using var r = new Rig(waitSeconds);
        r.Runner.DuringRun = () => { r.Drop("second.pdf"); r.Drop("third.pdf"); };
        r.Drop("first.pdf");
        r.Pass(60);
        Assert.Equal(2, r.Runner.Runs.Count);
        Assert.Equal(new[] { Folder + "second.pdf", Folder + "third.pdf" }, r.Runner.Runs[1]);
        r.Pass(120);
        Assert.Equal(2, r.Runner.Runs.Count);   // and their renamed copies don't start a third run
    }

    [Fact]
    public void A_batch_that_is_ready_during_the_cooldown_waits_for_it_instead_of_being_dropped()
    {
        using var r = new Rig();
        var a = r.F.Latest(r.Automation).Clone();
        a.CooldownSeconds = 60;
        r.F.Host.Library.SaveAutomation(a);
        r.F.Engine.Reload();

        r.Drop("x.pdf");
        r.Pass(10);
        Assert.Single(r.Runner.Runs);
        r.Drop("y.pdf");
        r.Pass(30);                               // ready long before the minute is up
        Assert.Single(r.Runner.Runs);
        r.Pass(40);
        Assert.Equal(2, r.Runner.Runs.Count);
        Assert.Equal(new[] { Folder + "y.pdf" }, r.Runner.Runs[1]);
    }

    [Fact]
    public void A_command_that_keeps_adding_files_to_the_watched_folder_is_paused_and_the_user_is_told()
    {
        using var r = new Rig();
        r.Runner.WritesUnreportedCopies = true;
        r.Drop("x.pdf");
        r.Pass(30 * 60);   // a slow loop: every run takes the full grace period and cooldown
        Assert.Equal(AutomationEngine.RunawaySelfFedRuns + 1, r.Runner.Runs.Count);
        var a = r.F.Latest(r.Automation);
        Assert.False(a.Enabled);
        Assert.Equal(AutomationEngine.RanTooOften, a.PausedReason);
        Assert.Contains(r.F.Host.Notifications.Shown, n => n.Title.Contains("paused"));
    }

    [Fact]
    public void A_steady_stream_of_the_users_own_files_is_not_a_loop()
    {
        using var r = new Rig();
        for (var i = 0; i < AutomationEngine.RunawaySelfFedRuns * 2; i++)
        {
            r.Drop($"scan {i}.pdf");
            r.Pass(12);
        }
        Assert.Equal(AutomationEngine.RunawaySelfFedRuns * 2, r.Runner.Runs.Count);
        Assert.True(r.F.Latest(r.Automation).Enabled);
    }

    [Fact]
    public async Task Triggers_that_dont_watch_files_never_trip_the_breaker()
    {
        // Copying things, switching apps, taking screenshots: the user does these, often, on purpose.
        using var f = new EngineFixture();
        var a = f.Add();
        f.Engine.Reload();
        for (var i = 0; i < AutomationEngine.RunawayRuns * 2; i++)
        {
            Assert.True((await f.FireAsync(a)).Ran, $"run {i + 1}");
            f.Now += TimeSpan.FromSeconds(1);
        }
        Assert.True(f.Latest(a).Enabled);
    }

    private sealed class BusySink : ITriggerSink, ITriggerSinkRunWindow
    {
        public bool Busy { get; set; }
        public List<ContentValue> Fired { get; } = new();
        public void Fire(ContentValue output, string? description = null) => Fired.Add(output);
        public void ReportProblem(string? message) { }
        public bool IsOwnRunRecent(TimeSpan grace) => false;
        public bool IsBusy() => Busy;
    }

    [Fact]
    public void Files_that_arrive_while_an_ask_first_prompt_is_open_wait_for_it()
    {
        var now = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
        var probe = new FakeProbe();
        var sink = new BusySink { Busy = true };
        var handler = new FolderTriggerEvents(new FolderChangeBatcher(TimeSpan.FromSeconds(3), probe, now: () => now), sink, new HashSet<string> { "added" }, "Downloads", TimeSpan.FromSeconds(3));
        probe.Sizes[Folder + "a.pdf"] = 1;
        handler.OnCreated(Folder + "a.pdf");
        for (var i = 0; i < 30; i++) { now += TimeSpan.FromSeconds(1); handler.Tick(); }
        Assert.Empty(sink.Fired);
        sink.Busy = false;
        for (var i = 0; i < 3; i++) { now += TimeSpan.FromSeconds(1); handler.Tick(); }
        var fired = Assert.Single(sink.Fired);
        Assert.Equal(Folder + "a.pdf", fired.OfType<PathItem>().Single().Path);
    }

    [Fact]
    public void The_batcher_holds_a_bounded_number_of_paths()
    {
        var probe = new FakeProbe();
        var batcher = new FolderChangeBatcher(TimeSpan.Zero, probe, stableFor: TimeSpan.Zero);
        for (var i = 0; i < FolderChangeBatcher.MaxPending + 50; i++)
        {
            probe.Sizes[Folder + i] = 1;
            batcher.OnCreated(Folder + i);
            batcher.OnChanged(Folder + i);
        }
        var total = 0;
        for (var i = 0; i < 5 && batcher.Poll() is { } b; i++) total += b.Added.Count;
        Assert.Equal(FolderChangeBatcher.MaxPending, total);
    }

    [Fact]
    public async Task File_watching_automations_get_a_five_second_minimum_cooldown_without_changing_the_stored_value()
    {
        using var r = new Rig();
        var f = r.F;
        Assert.True((await f.FireAsync(r.Automation)).Ran);

        var again = await f.FireAsync(r.Automation);
        Assert.False(again.Ran);
        Assert.Contains("5 seconds", again.SkipReason);

        f.Now += TimeSpan.FromSeconds(6);
        Assert.True((await f.FireAsync(r.Automation)).Ran);
        Assert.Equal(0, f.Latest(r.Automation).CooldownSeconds);

        // Other triggers keep "no cooldown".
        var other = f.Add();
        f.Engine.Reload();
        Assert.True((await f.FireAsync(other)).Ran);
        Assert.True((await f.FireAsync(other)).Ran);
    }
}
