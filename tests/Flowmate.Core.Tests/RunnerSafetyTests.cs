using Flowmate.Core;
using Flowmate.Core.Testing;
using Xunit;

namespace Flowmate.Core.Tests;

public sealed class MakeTempFileAction : ShortcutAction
{
    public override ActionDescriptor Descriptor { get; } = new() { Id = "test.makeTemp", Title = "Make temp", Category = ActionCategories.Advanced };
    public override Task<ContentValue> RunAsync(ActionContext ctx) => Task.FromResult(ContentValue.Files(new[] { ctx.WriteTempFile("made.txt", "hello").Path }));
}

public sealed class InputFileExistsAction : ShortcutAction
{
    public override ActionDescriptor Descriptor { get; } = new() { Id = "test.inputExists", Title = "Input exists", Category = ActionCategories.Advanced };
    public override Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var p = ctx.Input.OfType<PathItem>().FirstOrDefault()?.Path;
        if (p is null || !File.Exists(p)) throw new ActionException("Couldn't find the file.");
        return Task.FromResult(ContentValue.Text(File.ReadAllText(p)));
    }
}

public sealed class RunnerSafetyTests : IDisposable
{
    private readonly TestHost _host = new TestHost().With(typeof(RunnerSafetyTests).Assembly);
    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task NestedRun_TempFilesSurviveUntilTheTopLevelRunEnds()
    {
        var child = new ShortcutBuilder("Maker");
        child.Add("test.makeTemp");
        _host.Library.Save(child.Build());
        var parent = new ShortcutBuilder("Uses");
        parent.Add("flow.runShortcut", ("shortcut", ParamValue.Choice("Maker")));
        parent.Add("test.inputExists");
        var r = await _host.Runner.RunAsync(parent.Build(), ContentValue.Empty, new RunOptions { Source = RunSource.Editor });
        Assert.Equal(RunStatus.Succeeded, r.Status);
        Assert.Equal("hello", r.Output.AsText());
        Assert.False(Directory.Exists(_host.Services.Paths.RunTempFolder(r.RunId)));
    }

    [Fact]
    public async Task SensitiveOutput_IsHiddenInHistoryPreview()
    {
        var b = new ShortcutBuilder("Pw");
        b.Add("test.secret");
        var r = await _host.Runner.RunAsync(b.Build(), ContentValue.Empty, new RunOptions { Source = RunSource.Editor });
        Assert.Equal("(hidden)", r.Steps.Single().OutputPreview);
    }

    [Fact]
    public async Task ConcurrencyLimit_FailsAfterABoundedWaitInsteadOfQueueingForever()
    {
        var runner = (ShortcutRunner)_host.Runner;
        runner.SlotWaitTimeout = TimeSpan.FromMilliseconds(200);
        var slow = new ShortcutBuilder("Slow"); slow.Add("test.slow");
        var s = slow.Build();
        using var cts = new CancellationTokenSource();
        var running = Enumerable.Range(0, ShortcutRunner.MaxConcurrentRuns)
            .Select(_ => runner.RunAsync(s, ContentValue.Empty, new RunOptions { CancellationToken = cts.Token })).ToList();
        await Task.Delay(300);
        var extra = await runner.RunAsync(s, ContentValue.Empty, new RunOptions());
        Assert.Equal(RunStatus.Failed, extra.Status);
        Assert.Contains("Too many", extra.ErrorMessage);
        cts.Cancel();
        await Task.WhenAll(running);
    }
}

public sealed class SecretAction : ShortcutAction
{
    public override ActionDescriptor Descriptor { get; } = new() { Id = "test.secret", Title = "Secret", Category = ActionCategories.Advanced, SensitiveOutput = true };
    public override Task<ContentValue> RunAsync(ActionContext ctx) => Task.FromResult(ContentValue.Text("hunter2"));
}

public class TempSweeperTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "flowmate-tests", Guid.NewGuid().ToString("N")[..8]);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void Sweep_RemovesOldRunFoldersAndClipboardImages_KeepsRecentAndShared()
    {
        var paths = FlowmatePaths.ForRoot(_root);
        var oldRun = Directory.CreateDirectory(Path.Combine(paths.TempRoot, "oldrun")); File.WriteAllText(Path.Combine(oldRun.FullName, "a"), "x");
        var newRun = Directory.CreateDirectory(Path.Combine(paths.TempRoot, "newrun"));
        var clip = Directory.CreateDirectory(Path.Combine(paths.TempRoot, "clipboard"));
        var oldPng = Path.Combine(clip.FullName, "old.png"); File.WriteAllText(oldPng, "x");
        var newPng = Path.Combine(clip.FullName, "new.png"); File.WriteAllText(newPng, "x");
        var ago = DateTime.UtcNow.AddDays(-3);
        File.SetLastWriteTimeUtc(Path.Combine(oldRun.FullName, "a"), ago); Directory.SetLastWriteTimeUtc(oldRun.FullName, ago);
        File.SetLastWriteTimeUtc(oldPng, ago);
        TempSweeper.Sweep(paths);
        Assert.False(Directory.Exists(oldRun.FullName));
        Assert.True(Directory.Exists(newRun.FullName));
        Assert.False(File.Exists(oldPng));
        Assert.True(File.Exists(newPng));
    }
}
