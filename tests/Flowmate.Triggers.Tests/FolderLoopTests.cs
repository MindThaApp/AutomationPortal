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
    }

    /// <summary>Plays the shortcut: renames every input file to "2026-11-09 name", raising the watcher events a real rename would.</summary>
    private sealed class RenamingRunner : IShortcutRunner
    {
        private readonly FakeProbe _probe;
        public FolderTriggerEvents? Handler { get; set; }
        public List<IReadOnlyList<string>> Runs { get; } = new();
        public RenamingRunner(FakeProbe probe) { _probe = probe; }

        public Task<RunResult> RunAsync(Shortcut shortcut, ContentValue input, RunOptions? options = null) => RunAsync(shortcut.Id, input, options);

        public Task<RunResult> RunAsync(Guid shortcutId, ContentValue input, RunOptions? options = null)
        {
            var paths = input.OfType<PathItem>().Select(p => p.Path).ToList();
            Runs.Add(paths);
            foreach (var p in paths)
            {
                var renamed = Path.Combine(Path.GetDirectoryName(p)!, "2026-11-09 " + Path.GetFileName(p));
                _probe.Sizes[renamed] = _probe.Sizes[p];
                _probe.Sizes.Remove(p);
                Handler!.OnRenamed(p, renamed);
            }
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
