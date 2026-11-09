using Flowmate.Core;
using Flowmate.Core.Testing;
using Xunit;

namespace Flowmate.Core.Tests;

public class PathSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "flowmate-tests", Guid.NewGuid().ToString("N")[..8]);
    public PathSafetyTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static string Folder(Environment.SpecialFolder f) => Environment.GetFolderPath(f, Environment.SpecialFolderOption.DoNotVerify);

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:")]
    [InlineData(@"E:")]
    [InlineData(@"E:\")]
    [InlineData(@"e:/")]
    [InlineData(@"\\server\share")]
    [InlineData(@"\\server\share\")]
    [InlineData(@"\\server")]
    [InlineData("")]
    [InlineData("   ")]
    public void Roots_and_blank_paths_are_protected(string path) => Assert.True(PathSafety.IsProtected(path));

    [Fact]
    public void System_profile_and_known_folders_are_protected_but_not_their_contents()
    {
        var profile = Folder(Environment.SpecialFolder.UserProfile);
        var folders = new[]
        {
            Folder(Environment.SpecialFolder.Windows), Folder(Environment.SpecialFolder.ProgramFiles), profile,
            Folder(Environment.SpecialFolder.MyDocuments), Folder(Environment.SpecialFolder.MyPictures),
            Folder(Environment.SpecialFolder.MyMusic), Folder(Environment.SpecialFolder.MyVideos),
            Folder(Environment.SpecialFolder.DesktopDirectory), Path.Combine(profile, "Downloads"),
        };
        foreach (var f in folders.Where(f => f.Length > 0))
        {
            Assert.True(PathSafety.IsProtected(f), f);
            Assert.True(PathSafety.IsProtected(f + Path.DirectorySeparatorChar), f + " with a trailing separator");
            Assert.True(PathSafety.IsProtected(f.ToUpperInvariant()), f + " in capitals");
            // A path that isn't protected by its text is opened to follow links: only do that on the drive the tests use.
            if (SameDrive(f, _root)) Assert.False(PathSafety.IsProtected(Path.Combine(f, "Holiday 2026")), f + @"\Holiday 2026");
        }
        Assert.False(PathSafety.IsProtected(_root));
    }

    private static bool SameDrive(string a, string b) => string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);

    private string Windows => Folder(Environment.SpecialFolder.Windows);
    private string Profile => Folder(Environment.SpecialFolder.UserProfile);

    [Fact]
    public void Device_forms_and_admin_shares_of_this_PC_are_read_as_the_local_path()
    {
        var drive = Windows[..1];
        foreach (var p in new[]
        {
            @"\\?\" + Windows, @"\\.\" + Windows, @"\\?\" + Profile + @"\", @"\??\" + Windows,
            $@"\\localhost\{drive}$\" + Windows[3..], $@"\\127.0.0.1\{drive.ToLowerInvariant()}$\" + Windows[3..],
            $@"\\{Environment.MachineName}\{drive}$\" + Windows[3..], $@"\\?\UNC\localhost\{drive}$\" + Windows[3..],
            $@"\\[::1]\{drive}$\" + Windows[3..], @"\\localhost\ADMIN$", $@"\\localhost\{drive}$", $@"\\localhost\{drive}$\",
            $@"//localhost/{drive}$/" + Windows[3..].Replace('\\', '/'), @"\\?\" + drive + ":", @"\\?\Volume{00000000-0000-0000-0000-000000000000}\x",
        })
            Assert.True(PathSafety.IsProtected(p), p);

        Assert.False(PathSafety.IsProtected(@"\\?\" + Path.Combine(_root, "child")));
        Assert.Equal(Path.Combine(drive + @":\", @"Temp\x"), PathSafety.ToWin32Form($@"\\localhost\{drive}$\Temp\x"));
        Assert.Equal(@"\\server\share\x", PathSafety.ToWin32Form(@"\\?\UNC\server\share\x"));
        Assert.Equal(@"\\server\C$\x", PathSafety.ToWin32Form(@"\\server\C$\x"));   // another computer stays as it is
    }

    [Fact]
    public void Junctions_and_links_are_followed_before_the_check()
    {
        var real = Directory.CreateDirectory(Path.Combine(_root, "real")).FullName;
        var link = Path.Combine(_root, "link");
        using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{link}\" \"{real}\"")
               { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }))
            p!.WaitForExit();
        try
        {
            Assert.True(Directory.Exists(link));
            var set = new[] { real };
            Assert.True(PathSafety.IsProtected(link, set));
            Assert.True(PathSafety.IsProtected(link + @"\", set));
            Assert.False(PathSafety.IsProtected(Path.Combine(link, "child"), set));

            // The Windows compatibility junction for the old profile location.
            var legacy = Path.Combine(Path.GetPathRoot(Profile)!, "Documents and Settings", Path.GetFileName(Profile));
            if (Directory.Exists(legacy) && SameDrive(Profile, _root)) Assert.True(PathSafety.IsProtected(legacy), legacy);
        }
        finally { Directory.Delete(link); }   // removes the junction only
    }

    [Fact]
    public void Folders_that_hold_a_protected_folder_are_protected_too()
    {
        // Documents redirected into another folder (a data drive or the OneDrive folder): that folder can't go as a whole.
        var documents = Path.Combine(_root, "Data", "Documents");
        var set = new[] { documents };
        Assert.True(PathSafety.IsProtected(Path.Combine(_root, "Data"), set));
        Assert.True(PathSafety.IsProtected(_root, set));
        Assert.True(PathSafety.IsProtected(documents, set));
        Assert.False(PathSafety.IsProtected(Path.Combine(documents, "Taxes"), set));
        Assert.False(PathSafety.IsProtected(Path.Combine(_root, "Data", "Doc"), set));       // a name that only starts the same
        Assert.False(PathSafety.IsProtected(Path.Combine(_root, "Other"), set));
        // The real set: the folder that holds the profile.
        Assert.True(PathSafety.IsProtected(Path.GetDirectoryName(Profile)));
    }

    [Fact]
    public void WriteViaTemp_swaps_in_the_new_version_and_keeps_the_backup()
    {
        var path = Path.Combine(_root, "note.md");
        File.WriteAllText(path, "old");
        var backup = path + ".keep";
        PathSafety.WriteViaTemp(path, tmp => File.WriteAllText(tmp, "new"), backup);
        Assert.Equal("new", File.ReadAllText(path));
        Assert.Equal("old", File.ReadAllText(backup));
        Assert.Equal(new[] { "note.md", "note.md.keep" }, Directory.GetFiles(_root).Select(Path.GetFileName).Order().ToArray());
    }

    [Fact]
    public void WriteViaTemp_leaves_the_original_alone_when_writing_fails()
    {
        var path = Path.Combine(_root, "data.csv");
        File.WriteAllText(path, "a,b\r\n1,2\r\n");
        Assert.Throws<IOException>(() => PathSafety.WriteViaTemp(path, tmp =>
        {
            File.WriteAllText(tmp, "a,b");      // half-written …
            throw new IOException("Disk full");  // … then the disk fills up
        }));
        Assert.Equal("a,b\r\n1,2\r\n", File.ReadAllText(path));
        Assert.Equal(new[] { "data.csv" }, Directory.GetFiles(_root).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public void WriteViaTemp_creates_a_missing_file()
    {
        var path = Path.Combine(_root, "new.txt");
        PathSafety.WriteViaTemp(path, tmp => File.WriteAllText(tmp, "hello"));
        Assert.Equal("hello", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_root));

        // A new file whose write fails leaves no temp file behind.
        Assert.Throws<IOException>(() => PathSafety.WriteViaTemp(Path.Combine(_root, "other.txt"), tmp =>
        {
            File.WriteAllText(tmp, "half");
            throw new IOException("Disk full");
        }));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public void WriteViaTemp_writes_through_a_symbolic_link()
    {
        var real = Path.Combine(_root, "real.csv");
        var link = Path.Combine(_root, "link.csv");
        File.WriteAllText(real, "old");
        try { File.CreateSymbolicLink(link, real); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }   // needs Developer Mode or admin
        PathSafety.WriteViaTemp(link, tmp => File.WriteAllText(tmp, "new"));
        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Equal("new", File.ReadAllText(real));
        Assert.Equal("new", File.ReadAllText(link));
    }

    [Fact]
    public void Work_in_progress_names_are_spotted()
    {
        Assert.True(PathSafety.IsWorkInProgressName(@"C:\x\~data.1a2b3c4d.flowmate-tmp.csv"));
        Assert.True(PathSafety.IsWorkInProgressName(@"C:\x\.box.flowmate-copying-1a2b3c4d\"));
        Assert.True(PathSafety.IsWorkInProgressName(@"C:\x\a.txt.flowmate-old-1a2b3c4d"));
        Assert.False(PathSafety.IsWorkInProgressName(@"C:\x\flowmate notes.txt"));
    }

    [Fact]
    public void KeepFirstBackup_copies_once_and_never_overwrites()
    {
        var path = Path.Combine(_root, "budget.xlsx");
        Assert.Null(PathSafety.KeepFirstBackup(path));
        File.WriteAllText(path, "v1");
        var backup = PathSafety.KeepFirstBackup(path);
        Assert.Equal(Path.Combine(_root, "budget.bak.xlsx"), backup);
        Assert.Equal("v1", File.ReadAllText(backup!));
        File.WriteAllText(path, "v2");
        Assert.Equal(backup, PathSafety.KeepFirstBackup(path));
        Assert.Equal("v1", File.ReadAllText(backup!));
    }

    private sealed class FolderAction : ShortcutAction
    {
        public override ActionDescriptor Descriptor { get; } = new()
        {
            Id = "test.folderPath", Title = "Folder path", Category = ActionCategories.Advanced,
            Parameters = new[]
            {
                ParameterDescriptor.FolderParam("folder", "Folder"),
                ParameterDescriptor.FolderParam("other", "Other folder"),
                ParameterDescriptor.Text("text", "Text"),
            },
        };
        public override Task<ContentValue> RunAsync(ActionContext ctx) => Task.FromResult(ContentValue.Empty);
    }

    [Fact]
    public async Task An_empty_variable_in_a_path_stops_the_step_instead_of_widening_the_path()
    {
        using var host = new TestHost();
        var action = new FolderAction();
        var ctx = ActionTestHarness.CreateContext(host, action, null,
            ("folder", ParamValue.Text(_root + @"\", VarRef.Named("Album"))),
            ("text", ParamValue.Text("Album: ", VarRef.Named("Album"))));

        var ex = await Assert.ThrowsAsync<ActionException>(() => ctx.GetPathAsync("folder"));
        Assert.Contains("'Album' is empty", ex.Message);
        Assert.Equal("Album: ", await ctx.GetTextAsync("text"));     // ordinary text may still use an empty variable

        ctx.SetVariable("Album", ContentValue.Text("Summer"));
        Assert.Equal(Path.Combine(_root, "Summer"), await ctx.GetPathAsync("folder"));
    }

    [Fact]
    public async Task A_variable_next_to_text_may_be_empty_but_may_not_climb_out_of_the_folder()
    {
        using var host = new TestHost();
        var ctx = ActionTestHarness.CreateContext(host, new FolderAction(), null,
            ("folder", ParamValue.Text(_root + @"\", VarRef.Named("Prefix"), "report.txt")),
            ("other", ParamValue.Text(_root + @"\Photos\", VarRef.Named("Album"))));

        // An optional prefix: "C:\Out\{Prefix}report.txt" with no prefix is still a complete path.
        Assert.Equal(Path.Combine(_root, "report.txt"), await ctx.GetPathAsync("folder"));

        ctx.SetVariable("Album", ContentValue.Text(".."));
        var ex = await Assert.ThrowsAsync<ActionException>(() => ctx.GetPathAsync("other"));
        Assert.Contains("'Album' is '..'", ex.Message);
        ctx.SetVariable("Album", ContentValue.Text(@"\"));
        await Assert.ThrowsAsync<ActionException>(() => ctx.GetPathAsync("other"));

        ctx.SetVariable("Prefix", ContentValue.Text(@"..\"));
        await Assert.ThrowsAsync<ActionException>(() => ctx.GetPathAsync("folder"));
        ctx.SetVariable("Prefix", ContentValue.Text("2026 "));
        Assert.Equal(Path.Combine(_root, "2026 report.txt"), await ctx.GetPathAsync("folder"));
        ctx.SetVariable("Album", ContentValue.Text("Summer.v2"));
        Assert.Equal(Path.Combine(_root, "Photos", "Summer.v2"), await ctx.GetPathAsync("other"));
    }

    [Fact]
    public async Task A_path_that_is_only_an_empty_variable_stays_empty()
    {
        using var host = new TestHost();
        var ctx = ActionTestHarness.CreateContext(host, new FolderAction(), null, ("folder", ParamValue.Text(VarRef.Named("Nothing"))));
        Assert.Null(await ctx.GetPathAsync("folder"));
    }
}
