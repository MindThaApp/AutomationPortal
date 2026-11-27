using System.Text.Json;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using Xunit;

namespace AutomationPortal.Core.Tests;

/// <summary>The files list in the <c>run</c> reply with <c>json</c>.</summary>
public class RunFilesListTests
{
    private static async Task<JsonElement> RunAsync(Func<string, IReadOnlyList<FileChange>?>? changes)
    {
        var lib = new InMemoryShortcutLibrary();
        lib.Save(new ShortcutBuilder("Tidy").Build());
        var cmds = new ShortcutCommands(lib, (s, _, _, _) => Task.FromResult<RunResult?>(new RunResult { RunId = "r1", ShortcutName = s.Name, Status = RunStatus.Succeeded }))
        {
            RunChanges = changes,
        };
        var r = await cmds.ExecuteAsync(new PipeRequest { Kind = "run", Name = "Tidy", Wait = true, Json = true }, default);
        using var doc = JsonDocument.Parse(r.Output!);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task A_copy_is_reported_as_copy_and_other_kinds_as_new_or_changed()
    {
        var root = await RunAsync(id => id == "r1"
            ? new[]
            {
                new FileChange { Kind = FileChangeKind.Created, Path = @"C:\out\a (2).txt", From = @"C:\in\a.txt" },
                new FileChange { Kind = FileChangeKind.Created, Path = @"C:\out\new.txt" },
                new FileChange { Kind = FileChangeKind.Replaced, Path = @"C:\out\old.txt" },
                new FileChange { Kind = FileChangeKind.Deleted, Path = @"C:\out\gone.txt" },
                new FileChange { Kind = FileChangeKind.Changed, Path = @"C:\out\new.txt" },
            }
            : null);
        var files = root.GetProperty("files").EnumerateArray().Select(f => (f.GetProperty("path").GetString(), f.GetProperty("kind").GetString())).ToArray();
        Assert.Equal(new[] { (@"C:\out\a (2).txt", "copy"), (@"C:\out\new.txt", "new"), (@"C:\out\old.txt", "changed") }, files);
    }

    [Fact]
    public async Task A_run_with_no_file_effects_reports_an_empty_list()
    {
        Assert.Equal(0, (await RunAsync(_ => null)).GetProperty("files").GetArrayLength());
        Assert.Equal(0, (await RunAsync(_ => Array.Empty<FileChange>())).GetProperty("files").GetArrayLength());
        Assert.Equal(0, (await RunAsync(null)).GetProperty("files").GetArrayLength());
    }

    [Fact]
    public async Task Capabilities_lists_the_feature_and_keeps_contract_1_1()
    {
        var cmds = new ShortcutCommands(new InMemoryShortcutLibrary(), (_, _, _, _) => Task.FromResult<RunResult?>(null));
        var r = await cmds.ExecuteAsync(new PipeRequest { Kind = "capabilities" }, default);
        using var doc = JsonDocument.Parse(r.Output!);
        Assert.Contains("run.files-list", doc.RootElement.GetProperty("features").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("1.1", doc.RootElement.GetProperty("contractVersion").GetString());
    }
}
