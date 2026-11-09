using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using AutomationPortal.Triggers;

namespace AutomationPortal.Triggers.Tests;

/// <summary>"Test now" must never hand a shortcut real folders: it gets a fresh temp sandbox that is removed afterwards.</summary>
public class SampleSandboxTests
{
    [Fact]
    public void A_drive_sample_is_a_fresh_temp_folder_with_sample_files_never_a_drive_root()
    {
        using var host = new TestHost();
        var drive = new DriveConnectedTrigger().Descriptor;
        var samplesRoot = Path.Combine(host.Services.Paths.TempRoot, "trigger-samples");
        string folder;
        using (var sandbox = SampleSandbox.Create(host.Services.Paths))
        {
            folder = ((PathItem)SampleInputs.For(drive, new TriggerConfig(null, drive.Parameters), sandbox)[0]).Path;
            Assert.Equal(sandbox.Folder, folder);
            Assert.StartsWith(samplesRoot, folder, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(Path.GetPathRoot(folder), folder);
            Assert.True(File.Exists(sandbox.TextFile));
            Assert.True(File.Exists(sandbox.ImageFile));
            Assert.True(File.Exists(sandbox.SubfolderFile));
        }
        Assert.False(Directory.Exists(folder));
        Assert.True(Directory.Exists(samplesRoot));
    }

    [Fact]
    public void Each_test_gets_its_own_sandbox()
    {
        using var host = new TestHost();
        using var a = SampleSandbox.Create(host.Services.Paths);
        using var b = SampleSandbox.Create(host.Services.Paths);
        Assert.NotEqual(a.Folder, b.Folder);
    }

    [Fact]
    public void Cleaning_up_keeps_anything_the_sandbox_did_not_make()
    {
        using var host = new TestHost();
        var sandbox = SampleSandbox.Create(host.Services.Paths);
        var other = Path.Combine(sandbox.Folder, "Holiday photo.jpg");
        File.WriteAllText(other, "not ours");
        File.WriteAllText(sandbox.TextFile, "changed by the shortcut");

        sandbox.Dispose();

        Assert.True(sandbox.HasLeftovers);
        Assert.Equal("not ours", File.ReadAllText(other));
        Assert.Equal("changed by the shortcut", File.ReadAllText(sandbox.TextFile));
        Assert.False(File.Exists(sandbox.ImageFile));
        Assert.False(Directory.Exists(sandbox.Subfolder));
    }

    [Fact]
    public async Task Test_now_on_a_drive_automation_runs_on_the_sandbox_and_removes_it()
    {
        using var f = new EngineFixture();
        f.Triggers.Register(new DriveConnectedTrigger());
        var a = f.Add(triggerId: "drive.connected");
        ContentValue? seen = null;
        f.Host.Services.Runner = new CapturingRunner(f.Host.Services.Runner, v => seen = v);

        var result = await f.Engine.TestNowAsync(a.Id);

        Assert.Equal(RunStatus.Succeeded, result.Status);
        var folder = ((PathItem)seen![0]).Path;
        Assert.StartsWith(Path.Combine(f.Host.Services.Paths.TempRoot, "trigger-samples"), folder, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(folder));
    }

    private sealed class CapturingRunner : IShortcutRunner
    {
        private readonly IShortcutRunner _inner;
        private readonly Action<ContentValue> _seen;
        public CapturingRunner(IShortcutRunner inner, Action<ContentValue> seen) { _inner = inner; _seen = seen; }
        public Task<RunResult> RunAsync(Shortcut shortcut, ContentValue input, RunOptions? options = null) { _seen(input); return _inner.RunAsync(shortcut, input, options); }
        public Task<RunResult> RunAsync(Guid shortcutId, ContentValue input, RunOptions? options = null) { _seen(input); return _inner.RunAsync(shortcutId, input, options); }
    }
}
