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
            Assert.False(PathSafety.IsProtected(Path.Combine(f, "Holiday 2026")), f + @"\Holiday 2026");
        }
        Assert.False(PathSafety.IsProtected(@"\\server\share\folder"));
        Assert.False(PathSafety.IsProtected(@"E:\Photos"));
        Assert.False(PathSafety.IsProtected(_root));
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
    public async Task A_path_that_is_only_an_empty_variable_stays_empty()
    {
        using var host = new TestHost();
        var ctx = ActionTestHarness.CreateContext(host, new FolderAction(), null, ("folder", ParamValue.Text(VarRef.Named("Nothing"))));
        Assert.Null(await ctx.GetPathAsync("folder"));
    }
}
