using System.Text.Json;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using Xunit;

namespace AutomationPortal.Core.Tests;

public class NewShortcutPathTests
{
    [Theory]
    [InlineData(@"C:\Photos\a.jpg")]
    [InlineData(@"c:\a.txt")]
    [InlineData(@"C:/Photos/a.jpg")]
    [InlineData(@"\\server\share\a.txt")]
    [InlineData(@"\\server\share")]
    [InlineData(@"C:\Folder")]
    [InlineData(@"C:\con-notes\console.txt")]
    public void Full_local_and_network_paths_are_accepted(string path) => Assert.Null(LaunchArguments.NewShortcutPathProblem(path));

    [Theory]
    [InlineData(@"a.txt")]
    [InlineData(@"Photos\a.jpg")]
    [InlineData(@"\Photos\a.jpg")]
    [InlineData(@"C:a.txt")]
    [InlineData(@"C:")]
    [InlineData(@"\\?\C:\a.txt")]
    [InlineData(@"\\?\UNC\server\share\a.txt")]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"//?/C:/a.txt")]
    [InlineData(@"\\server")]
    [InlineData(@"\\server\")]
    [InlineData(@"C:\Folder\NUL")]
    [InlineData(@"C:\Folder\com1.txt")]
    [InlineData(@"C:\a.txt:secret")]
    [InlineData(@"C:\Folder\..\b.txt")]
    [InlineData(@"C:\Folder\a*.txt")]
    [InlineData("C:\\a\n.txt")]
    [InlineData("")]
    [InlineData("   ")]
    public void Relative_device_and_odd_paths_are_refused(string path) => Assert.NotNull(LaunchArguments.NewShortcutPathProblem(path));

    [Fact]
    public void Check_dedupes_and_reports_the_first_bad_path()
    {
        var (ok, error) = LaunchArguments.CheckNewShortcutPaths(new[] { @"C:\a.txt", @"c:\A.TXT", @"\\srv\share\b.txt" });
        Assert.Null(error);
        Assert.Equal(new[] { @"C:\a.txt", @"\\srv\share\b.txt" }, ok);

        var (none, problem) = LaunchArguments.CheckNewShortcutPaths(new[] { @"C:\a.txt", "relative.txt" });
        Assert.Empty(none);
        Assert.Contains("relative.txt", problem);
    }

    [Fact]
    public void Missing_files_are_listed_and_network_paths_are_never_looked_at()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ap-new-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var there = Path.Combine(folder, "there.txt");
            File.WriteAllText(there, "x");
            var gone = Path.Combine(folder, "gone.txt");
            var missing = LaunchArguments.MissingNewShortcutFiles(new[] { there, gone, folder, @"\\nowhere-host-for-tests\share\x.txt" });
            Assert.Equal(new[] { gone }, missing);
            Assert.Equal("x", File.ReadAllText(there));   // untouched
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}

public class NewShortcutLinkTests
{
    [Fact]
    public void A_new_link_carries_its_files_and_asks_nothing()
    {
        var c = LaunchArguments.Parse(new[] { "automationportal://new?path=C%3A%5CPhotos%5Ca.jpg&path=%5C%5Cserver%5Cshare%5Cb.txt&path=C%3A%5CPhotos%5Ca.jpg" });
        Assert.Null(c.Error);
        Assert.Equal(new[] { @"C:\Photos\a.jpg", @"\\server\share\b.txt" }, c.NewShortcutFiles);
        Assert.False(c.FromLink);   // can't run anything, so "Allow links" doesn't matter
        Assert.Null(c.RunTarget);
        Assert.False(c.IsEmpty);
    }

    [Fact]
    public void A_new_link_through_the_link_switch_ignores_other_arguments()
    {
        var c = LaunchArguments.Parse(new[] { "--run", "Wipe", "--link", "automationportal://new/?path=C%3A%5Ca.txt" });
        Assert.Equal(new[] { @"C:\a.txt" }, c.NewShortcutFiles);
        Assert.Null(c.RunTarget);
    }

    [Theory]
    [InlineData("automationportal://new")]
    [InlineData("automationportal://new?name=x")]
    [InlineData("automationportal://new?path=a.txt")]
    [InlineData("automationportal://new?path=%5C%5C%3F%5CC%3A%5Ca.txt")]
    [InlineData("automationportal://new?path=C%3A%5Cok.txt&path=C%3A%5CNUL")]
    public void Bad_new_links_are_refused_with_a_reason(string link)
    {
        var c = LaunchArguments.Parse(new[] { link });
        Assert.NotNull(c.Error);
        Assert.Empty(c.NewShortcutFiles);
    }

    [Fact]
    public void A_new_link_takes_at_most_100_paths()
    {
        string Link(int n) => "automationportal://new?" + string.Join("&", Enumerable.Range(0, n).Select(i => $"path=C%3A%5Cf{i}.txt"));
        Assert.Equal(100, LaunchArguments.Parse(new[] { Link(100) }).NewShortcutFiles.Count);
        var over = LaunchArguments.Parse(new[] { Link(101) });
        Assert.Contains("more than 100", over.Error);
        Assert.Empty(over.NewShortcutFiles);
    }

    [Fact]
    public void Other_links_are_unchanged()
    {
        Assert.Equal("Tidy", LaunchArguments.Parse(new[] { "automationportal://run?name=Tidy" }).RunTarget);
        Assert.Empty(LaunchArguments.Parse(new[] { "automationportal://run?name=Tidy" }).NewShortcutFiles);
        Assert.NotNull(LaunchArguments.Parse(new[] { "automationportal://newer?path=C%3A%5Ca.txt" }).Error);
    }
}

public class NewShortcutPipeTests
{
    private static (ShortcutCommands Commands, List<NewShortcutRequest> Opened, List<string> Runs) Make(bool withEditor = true)
    {
        var opened = new List<NewShortcutRequest>();
        var runs = new List<string>();
        var lib = new InMemoryShortcutLibrary();
        lib.Save(new ShortcutBuilder("Tidy").Build());
        var cmds = new ShortcutCommands(lib, (s, _, _, _) => { runs.Add(s.Name); return Task.FromResult<RunResult?>(null); })
        {
            OpenNewShortcut = withEditor ? (r, _) => { opened.Add(r); return Task.CompletedTask; } : null,
        };
        return (cmds, opened, runs);
    }

    [Fact]
    public async Task New_opens_the_editor_with_the_files_and_replies_0()
    {
        var (cmds, opened, runs) = Make();
        var gone = Path.Combine(Path.GetTempPath(), "ap-new-missing-" + Guid.NewGuid().ToString("N") + ".txt");
        var r = await cmds.ExecuteAsync(new PipeRequest { Kind = "new", Files = new[] { gone, @"\\server\share\b.txt" }, Source = "filesportal", Json = true }, default);
        Assert.Equal(CliExit.Ok, r.ExitCode);
        var req = Assert.Single(opened);
        Assert.Equal(new[] { gone, @"\\server\share\b.txt" }, req.Files);
        Assert.Equal(new[] { gone }, req.Missing);
        Assert.Equal("filesportal", req.CallerId);
        Assert.Empty(runs);
        using var doc = JsonDocument.Parse(r.Output!);
        Assert.Equal("opened", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("files").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("missing").GetInt32());
        Assert.Contains("couldn't be found", r.Message);
    }

    [Fact]
    public async Task The_cli_and_bad_sources_are_not_named_as_the_caller()
    {
        var (cmds, opened, _) = Make();
        await cmds.ExecuteAsync(new PipeRequest { Kind = "new", Files = new[] { @"C:\a.txt" }, Source = "cli" }, default);
        await cmds.ExecuteAsync(new PipeRequest { Kind = "new", Files = new[] { @"C:\a.txt" }, Source = "Not Valid" }, default);
        Assert.All(opened, o => Assert.Null(o.CallerId));
    }

    [Theory]
    [InlineData("relative.txt")]
    [InlineData(@"\\?\C:\a.txt")]
    [InlineData(@"C:\NUL")]
    public async Task Bad_paths_are_a_usage_error_and_nothing_opens(string path)
    {
        var (cmds, opened, _) = Make();
        var r = await cmds.ExecuteAsync(new PipeRequest { Kind = "new", Files = new[] { @"C:\ok.txt", path } }, default);
        Assert.Equal(CliExit.Usage, r.ExitCode);
        Assert.Empty(opened);
    }

    [Fact]
    public async Task No_files_is_a_usage_error()
    {
        var (cmds, opened, _) = Make();
        Assert.Equal(CliExit.Usage, (await cmds.ExecuteAsync(new PipeRequest { Kind = "new" }, default)).ExitCode);
        Assert.Empty(opened);
    }

    [Fact]
    public async Task Without_an_editor_new_is_refused()
    {
        var (cmds, _, _) = Make(withEditor: false);
        Assert.Equal(CliExit.Problem, (await cmds.ExecuteAsync(new PipeRequest { Kind = "new", Files = new[] { @"C:\a.txt" } }, default)).ExitCode);
    }

    [Fact]
    public async Task New_is_a_contract_kind_and_a_feature()
    {
        var (cmds, _, _) = Make();
        var r = await cmds.ExecuteAsync(new PipeRequest { Kind = "capabilities" }, default);
        using var doc = JsonDocument.Parse(r.Output!);
        Assert.Contains("new", doc.RootElement.GetProperty("kinds").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("new-shortcut", doc.RootElement.GetProperty("features").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(1, doc.RootElement.GetProperty("contract").GetInt32());
    }
}
