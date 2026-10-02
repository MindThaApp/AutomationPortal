using Flowmate.Core;
using Flowmate.Core.Testing;
using Xunit;

namespace Flowmate.Core.Tests;

public class LaunchArgumentsTests
{
    [Fact]
    public void Run_with_input_and_files()
    {
        var c = LaunchArguments.Parse(new[] { "--run", "Resize Images", "--input", "hello", "--files", @"C:\a b.png", @"C:\c.png" });
        Assert.Equal("Resize Images", c.RunTarget);
        Assert.Equal("hello", c.Input);
        Assert.Equal(new[] { @"C:\a b.png", @"C:\c.png" }, c.Files);
        Assert.False(c.FromLink);
    }

    [Fact]
    public void Background_and_import_and_bare_file()
    {
        Assert.True(LaunchArguments.Parse(new[] { "--background" }).Background);
        Assert.Equal("x.flowmate", LaunchArguments.Parse(new[] { "--import", "x.flowmate" }).ImportFile);
        Assert.Equal(@"C:\d\y.FLOWMATE", LaunchArguments.Parse(new[] { @"C:\d\y.FLOWMATE" }).ImportFile);
        Assert.True(LaunchArguments.Parse(Array.Empty<string>()).IsEmpty);
    }

    [Fact]
    public void Files_after_files_flag_are_not_taken_for_imports()
    {
        var c = LaunchArguments.Parse(new[] { "--run", "X", "--files", "a.flowmate" });
        Assert.Null(c.ImportFile);
        Assert.Single(c.Files);
    }

    [Fact]
    public void Run_link_decodes_name_and_input()
    {
        var c = LaunchArguments.Parse(new[] { "flowmate://run?name=Good%20Morning&input=a%2Bb+c" });
        Assert.Equal("Good Morning", c.RunTarget);
        Assert.Equal("a+b c", c.Input);
        Assert.True(c.FromLink);
    }

    [Fact]
    public void Import_link_and_bad_links()
    {
        var c = LaunchArguments.Parse(new[] { "flowmate://import?file=C%3A%5Cx%5Cs.flowmate" });
        Assert.Equal(@"C:\x\s.flowmate", c.ImportFile);
        Assert.True(c.FromLink);

        var bad = LaunchArguments.Parse(new[] { "flowmate://run" });
        Assert.NotNull(bad.Error);
        Assert.False(bad.FromLink);
        Assert.NotNull(LaunchArguments.Parse(new[] { "flowmate://reboot?now=1" }).Error);
        Assert.NotNull(LaunchArguments.Parse(new[] { "--run" }).Error);
    }

    [Fact]
    public void Unknown_toast_and_dev_arguments_are_ignored()
    {
        var c = LaunchArguments.Parse(new[] { "-ToastActivated", "--data-root", @"C:\t", "--show", "history" });
        Assert.True(c.IsEmpty);
        Assert.Null(c.Error);
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class InstanceNamesTests
{
    [Fact]
    public void Custom_data_root_gets_its_own_name()
    {
        var real = InstanceNames.For(null);
        var a = InstanceNames.For(@"C:\Temp\one");
        Assert.StartsWith("Flowmate-S-1-", real);
        Assert.NotEqual(real, a);
        Assert.Equal(a, InstanceNames.For(@"c:\temp\ONE\"));
        Assert.NotEqual(a, InstanceNames.For(@"C:\Temp\two"));
    }
}

public class SettingsMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "flowmate-tests", Guid.NewGuid().ToString("N")[..8]);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
    private FlowmatePaths Paths => FlowmatePaths.ForRoot(_root);

    [Fact]
    public void Old_default_quick_run_key_moves_to_the_new_default()
    {
        Directory.CreateDirectory(Paths.RoamingRoot);
        File.WriteAllText(Paths.SettingsFile, "{ \"quickRunHotkey\": \"Ctrl+Alt+Space\", \"theme\": \"Dark\" }");
        var s = new JsonSettingsStore(Paths).Current;
        Assert.Equal(AppSettings.DefaultQuickRunHotkey, s.QuickRunHotkey);
        Assert.Equal(ThemeSetting.Dark, s.Theme);
        Assert.Equal(AppSettings.CurrentVersion, s.SettingsVersion);
        Assert.Equal(AppSettings.DefaultQuickRunHotkey, new JsonSettingsStore(Paths).Current.QuickRunHotkey);
    }

    [Fact]
    public void A_key_the_user_chose_is_left_alone()
    {
        Directory.CreateDirectory(Paths.RoamingRoot);
        File.WriteAllText(Paths.SettingsFile, "{ \"quickRunHotkey\": \"Ctrl+Alt+Q\" }");
        Assert.Equal("Ctrl+Alt+Q", new JsonSettingsStore(Paths).Current.QuickRunHotkey);
    }

    [Fact]
    public void A_user_who_picked_the_old_key_after_migration_keeps_it()
    {
        var store = new JsonSettingsStore(Paths);
        store.Update(s => s.QuickRunHotkey = "Ctrl+Alt+Space");
        Assert.Equal("Ctrl+Alt+Space", new JsonSettingsStore(Paths).Current.QuickRunHotkey);
    }
}

public class PipeAndCommandTests
{
    private static string PipeName() => "Flowmate-test-" + Guid.NewGuid().ToString("N");

    private static (ShortcutCommands Commands, InMemoryShortcutLibrary Lib, List<(string Name, ContentValue Input)> Runs) Make(Func<Shortcut, RunResult>? result = null)
    {
        var lib = new InMemoryShortcutLibrary();
        var runs = new List<(string, ContentValue)>();
        var cmds = new ShortcutCommands(lib, (s, input, source, ct) =>
        {
            runs.Add((s.Name, input));
            var r = result?.Invoke(s) ?? new RunResult { ShortcutId = s.Id, ShortcutName = s.Name, Status = RunStatus.Succeeded, Output = ContentValue.Text("done " + input.AsText()) };
            return Task.FromResult<RunResult?>(r);
        });
        return (cmds, lib, runs);
    }

    private static Shortcut Sc(string name, string? alias = null) { var s = new ShortcutBuilder(name).Build(); s.Alias = alias; return s; }

    [Fact]
    public async Task Run_by_name_alias_prefix_and_id()
    {
        var (cmds, lib, runs) = Make();
        var gm = Sc("Good Morning", "gm"); lib.Save(gm);
        lib.Save(Sc("Weather Now"));
        foreach (var key in new[] { "good morning", "GM", "Good", gm.Id.ToString() })
        {
            var r = await cmds.ExecuteAsync(new PipeRequest { Kind = "run", Name = key, Wait = true, Input = "x" }, default);
            Assert.Equal(CliExit.Ok, r.ExitCode);
            Assert.Equal("done x", r.Output);
        }
        Assert.Equal(4, runs.Count);
    }

    [Fact]
    public async Task Run_unknown_is_not_found_with_hint()
    {
        var (cmds, lib, _) = Make();
        lib.Save(Sc("Weather Now"));
        var r = await cmds.ExecuteAsync(new PipeRequest { Kind = "run", Name = "Weather", Wait = true }, default);
        Assert.Equal(CliExit.Ok, r.ExitCode);   // unique prefix
        var none = await cmds.ExecuteAsync(new PipeRequest { Kind = "run", Name = "Nope", Wait = true }, default);
        Assert.Equal(CliExit.NotFound, none.ExitCode);
        Assert.Contains("fm list", none.Message);
    }

    [Fact]
    public async Task Run_maps_status_to_exit_codes_and_json()
    {
        var (cmds, lib, _) = Make(s => new RunResult { ShortcutId = s.Id, ShortcutName = s.Name, Status = RunStatus.Failed, ErrorMessage = "Couldn't find the file." });
        lib.Save(Sc("Broken"));
        var r = await cmds.ExecuteAsync(new PipeRequest { Kind = "run", Name = "Broken", Wait = true }, default);
        Assert.Equal(CliExit.Failed, r.ExitCode);
        Assert.Equal("Couldn't find the file.", r.Message);
        var j = await cmds.ExecuteAsync(new PipeRequest { Kind = "run", Name = "Broken", Wait = true, Json = true }, default);
        Assert.Contains("\"status\": \"failed\"", j.Output);
        Assert.Contains("Couldn't find the file.", j.Output);
    }

    [Fact]
    public async Task Run_passes_files_as_input_and_does_not_wait_by_default()
    {
        var (cmds, lib, runs) = Make();
        lib.Save(Sc("Resize"));
        var r = await cmds.ExecuteAsync(new PipeRequest { Kind = "run", Name = "Resize", Files = new[] { @"C:\nope\a.png" } }, default);
        Assert.Equal(CliExit.Ok, r.ExitCode);
        Assert.Contains("Started", r.Message);
        Assert.Single(runs);
        Assert.Equal(@"C:\nope\a.png", runs[0].Input.AsText());
    }

    [Fact]
    public async Task List_text_and_json()
    {
        var (cmds, lib, _) = Make();
        lib.Save(Sc("Alpha", "al")); lib.Save(Sc("Beta"));
        var text = await cmds.ExecuteAsync(new PipeRequest { Kind = "list" }, default);
        Assert.Contains("Alpha", text.Output); Assert.Contains("(al)", text.Output);
        var json = await cmds.ExecuteAsync(new PipeRequest { Kind = "list", Json = true }, default);
        using var doc = System.Text.Json.JsonDocument.Parse(json.Output!);
        Assert.Equal(2, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Export_then_import_round_trips()
    {
        var (cmds, lib, _) = Make();
        lib.Save(Sc("Alpha"));
        var export = await cmds.ExecuteAsync(new PipeRequest { Kind = "export", Name = "Alpha" }, default);
        Assert.Equal(CliExit.Ok, export.ExitCode);
        var import = await cmds.ExecuteAsync(new PipeRequest { Kind = "import", Input = export.Output }, default);
        Assert.Equal(CliExit.Ok, import.ExitCode);
        Assert.Equal(2, lib.All.Count);
        var bad = await cmds.ExecuteAsync(new PipeRequest { Kind = "import", Input = "not json" }, default);
        Assert.Equal(CliExit.Problem, bad.ExitCode);
    }

    [Fact]
    public async Task Pipe_round_trip_and_nobody_listening()
    {
        var name = PipeName();
        Assert.Null(await PipeClient.TrySendAsync(name, new PipeRequest { Kind = "ping" }, TimeSpan.FromMilliseconds(50)));

        using var server = new PipeServer(name, (req, ct) => Task.FromResult(PipeResponse.Done("echo:" + req.Name + ":" + string.Join(",", req.Args))));
        server.Start();
        var r = await PipeClient.TrySendAsync(name, new PipeRequest { Kind = "x", Name = "n", Args = new[] { "a", "b c" } }, TimeSpan.FromSeconds(2));
        Assert.Equal("echo:n:a,b c", r!.Output);
        var again = await PipeClient.TrySendAsync(name, new PipeRequest { Kind = "x", Name = "second" }, TimeSpan.FromSeconds(2));
        Assert.Equal("echo:second:", again!.Output);
    }

    [Fact]
    public async Task Closing_the_pipe_cancels_a_long_request()
    {
        var name = PipeName();
        var cancelled = new TaskCompletionSource<bool>();
        using var server = new PipeServer(name, async (req, ct) =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(20), ct); }
            catch (OperationCanceledException) { cancelled.TrySetResult(true); throw; }
            return PipeResponse.Done();
        });
        server.Start();
        using var cts = new CancellationTokenSource(300);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PipeClient.TrySendAsync(name, new PipeRequest { Kind = "x" }, TimeSpan.FromSeconds(2), cts.Token));
        Assert.True(await Task.WhenAny(cancelled.Task, Task.Delay(5000)) == cancelled.Task);
    }
}
