using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.Core.Tests;

public sealed class TestRootSandboxTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ap-sandbox", Guid.NewGuid().ToString("N")[..8]);

    public TestRootSandboxTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData("desktop", "Desktop")]
    [InlineData("documents", "Documents")]
    [InlineData("downloads", "Downloads")]
    [InlineData("pictures", "Pictures")]
    [InlineData("music", "Music")]
    [InlineData("videos", "Videos")]
    [InlineData("home", "Home")]
    [InlineData("onedrive", "OneDrive")]
    [InlineData("roaming", "AppData")]
    [InlineData("local", "LocalAppData")]
    public void Folders_map_under_the_root(string name, string folder)
    {
        using var _ = UserFolders.Scope(_root);
        var path = UserFolders.Map(name);
        Assert.Equal(Path.Combine(Path.GetFullPath(_root), "user", folder), path);
        Assert.True(Directory.Exists(path));
        Assert.True(UserFolders.IsInsideRoot(path!));
    }

    [Fact]
    public void Screenshots_are_inside_pictures()
    {
        using var _ = UserFolders.Scope(_root);
        Assert.Equal(Path.Combine(Path.GetFullPath(_root), "user", "Pictures", "Screenshots"), UserFolders.Map("screenshots"));
    }

    [Fact]
    public void Without_a_root_nothing_is_mapped_and_real_folders_are_used()
    {
        Assert.Null(UserFolders.Map("documents"));
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), UserFolders.Documents());
        Assert.Equal(Path.GetTempPath(), UserFolders.TempPath());
        Assert.True(UserFolders.IsInsideRoot(@"C:\anywhere"));
    }

    [Fact]
    public void Resolvers_follow_the_root()
    {
        using var _ = UserFolders.Scope(_root);
        Assert.True(UserFolders.IsInsideRoot(UserFolders.Documents()));
        Assert.True(UserFolders.IsInsideRoot(UserFolders.Home()));
        Assert.True(UserFolders.IsInsideRoot(UserFolders.LocalAppData()));
        Assert.True(UserFolders.IsInsideRoot(LibraryBackup.DefaultFolder));
    }

    [Fact]
    public void Guard_allows_inside_and_blocks_outside()
    {
        using var _ = UserFolders.Scope(_root);
        TestRootGuard.Check(Path.Combine(_root, "a", "b.txt"));
        TestRootGuard.Check(_root);
        var sibling = _root + "-other";
        Assert.Throws<ActionException>(() => TestRootGuard.Check(Path.Combine(sibling, "x.txt")));
        Assert.Throws<ActionException>(() => TestRootGuard.Check(Path.Combine(_root, "..", "x.txt")));
        Assert.Throws<ActionException>(() => TestRootGuard.Check(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)));
        Assert.Throws<ActionException>(() => TestRootGuard.Check((string?)null));
    }

    [Fact]
    public void Guard_does_nothing_without_a_root()
    {
        TestRootGuard.Check(@"C:\anywhere\x.txt");
        TestRootGuard.Check((string?)null);
    }

    private ScriptedPicker Picker(string answersJson)
    {
        File.WriteAllText(Path.Combine(_root, "picker-answers.json"), answersJson);
        return new ScriptedPicker(_root);
    }

    private static PickRequest Req(PickKind kind = PickKind.OpenFile) => new(kind, "Choose");

    [Fact]
    public void Scripted_picker_returns_queued_in_root_answers_in_order()
    {
        var inside1 = Path.Combine(_root, "one.txt");
        var picker = Picker(System.Text.Json.JsonSerializer.Serialize(new object[] { inside1, "two.txt" }));
        Assert.Equal(new[] { inside1 }, picker.Pick(Req()));
        Assert.Equal(new[] { Path.Combine(_root, "two.txt") }, picker.Pick(Req(PickKind.SaveFile)));
        Assert.Null(picker.Pick(Req()));
        Assert.Contains("OpenFile", File.ReadAllText(picker.LogFile));
    }

    [Fact]
    public void Scripted_picker_returns_several_files_for_a_multi_pick()
    {
        var a = Path.Combine(_root, "a.txt");
        var b = Path.Combine(_root, "b.txt");
        var picker = Picker(System.Text.Json.JsonSerializer.Serialize(new object[] { new[] { a, b } }));
        Assert.Equal(new[] { a, b }, picker.Pick(Req(PickKind.OpenFiles)));
    }

    [Fact]
    public void Scripted_picker_cancels_an_answer_outside_the_root_and_when_empty()
    {
        var outside = Path.Combine(Path.GetTempPath(), "ap-sandbox-outside", "x.txt");
        var picker = Picker(System.Text.Json.JsonSerializer.Serialize(new object[] { outside }));
        Assert.Null(picker.Pick(Req()));
        Assert.Null(picker.Pick(Req()));
        Assert.Contains("outside the test root", File.ReadAllText(picker.LogFile));
        Assert.Null(new ScriptedPicker(Path.Combine(_root, "no-such-subroot")).Pick(Req()));
    }

    private string MakeJunctionToOutside(out string outside)
    {
        outside = Path.Combine(Path.GetTempPath(), "ap-sandbox-outside", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(outside);
        var link = Path.Combine(_root, "link");
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{link}\" \"{outside}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
        using (var p = System.Diagnostics.Process.Start(psi)!) { p.StandardOutput.ReadToEnd(); p.WaitForExit(); Assert.Equal(0, p.ExitCode); }
        return link;
    }

    [Fact]
    public void A_junction_inside_the_root_that_points_outside_is_blocked()
    {
        var link = MakeJunctionToOutside(out var outside);
        try
        {
            using var _ = UserFolders.Scope(_root);
            var target = Path.Combine(link, "sub", "file.txt");
            Assert.False(UserFolders.IsInsideRoot(target));
            Assert.Throws<ActionException>(() => PathSafety.WriteViaTemp(Path.Combine(link, "file.txt"), tmp => File.WriteAllText(tmp, "x")));
            Assert.Empty(Directory.GetFileSystemEntries(outside));

            var picker = Picker(System.Text.Json.JsonSerializer.Serialize(new object[] { Path.Combine(link, "file.txt") }));
            Assert.Null(picker.Pick(Req(PickKind.SaveFile)));
        }
        finally
        {
            Directory.Delete(Path.Combine(_root, "link"));   // removes the junction only, not its target
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void Scope_restores_the_previous_root_when_nested()
    {
        var inner = Path.Combine(_root, "inner");
        using var outer = UserFolders.Scope(_root);
        using (UserFolders.Scope(inner)) Assert.Equal(Path.GetFullPath(inner), UserFolders.Root);
        Assert.Equal(Path.GetFullPath(_root), UserFolders.Root);
    }

    [Fact]
    public void System_changing_actions_are_blocked_only_under_a_root()
    {
        TestRootGuard.BlockSystemChange("opening apps");
        using var _ = UserFolders.Scope(_root);
        Assert.Throws<ActionException>(() => TestRootGuard.BlockSystemChange("opening apps"));
    }

    [Fact]
    public void Picker_host_is_scripted_under_a_root()
    {
        using var _ = UserFolders.Scope(_root);
        Assert.IsType<ScriptedPicker>(PickerHost.Current);
    }
}
