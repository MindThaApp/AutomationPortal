using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using Xunit;

namespace AutomationPortal.App.Tests;

public sealed class DroppedFilesTests
{
    private const string Doc = @"C:\Temp\drop\a.txt";
    private const string Pic = @"C:\Temp\drop\b.jpg";
    private const string Dir = @"C:\Temp\drop\Photos";
    private const string Package = @"C:\Temp\drop\Tidy.autoportal";
    private static readonly string[] Dropped = { Doc, Pic, Dir };

    private static bool IsFolder(string p) => p == Dir || p.EndsWith("Folder", StringComparison.Ordinal);

    private static DroppedSelection Select(Shortcut s, IEnumerable<string> paths, Func<string, bool>? isNetwork = null) =>
        DroppedFiles.Select(s, DroppedFiles.Paths(paths), IsFolder, isNetwork ?? (_ => false));

    private static ActionRegistry Registry()
    {
        var actions = new ActionRegistry();
        actions.RegisterAssembly(typeof(AutomationPortal.Actions.Documents.Transfer).Assembly);
        return actions;
    }

    private static Shortcut With(params string[] ids)
    {
        var s = new Shortcut { Name = "Tidy", AcceptsInput = { ContentKind.File } };
        foreach (var id in ids) s.Actions.Add(ActionInstance.Create(id));
        return s;
    }

    private static string[] Accepted(DroppedSelection d) => d.Accepted.Select(i => i.Path).ToArray();

    [Fact]
    public void Only_shortcuts_that_take_files_or_folders_show_they_take_a_drop()
    {
        var paths = DroppedFiles.Paths(Dropped);
        Assert.True(DroppedFiles.CanRunWith(new Shortcut { AcceptsInput = { ContentKind.File } }, paths));
        Assert.True(DroppedFiles.CanRunWith(new Shortcut { AcceptsInput = { ContentKind.Folder } }, paths));
        Assert.True(DroppedFiles.CanRunWith(new Shortcut { AcceptsInput = { ContentKind.Image } }, paths));
        Assert.True(DroppedFiles.CanRunWith(new Shortcut { AcceptsInput = { ContentKind.Any } }, paths));
        Assert.True(DroppedFiles.CanRunWith(new Shortcut { WhenNoInput = WhenNoInput.AskForFiles }, paths));
        Assert.False(DroppedFiles.CanRunWith(new Shortcut(), paths));
        Assert.False(DroppedFiles.CanRunWith(new Shortcut { AcceptsInput = { ContentKind.Text, ContentKind.Url } }, paths));
        Assert.False(DroppedFiles.CanRunWith(new Shortcut { AcceptsInput = { ContentKind.File } }, DroppedFiles.Paths(Array.Empty<string>())));
    }

    [Fact]
    public void Dropped_items_are_filtered_by_what_the_shortcut_takes()
    {
        Assert.Equal(new[] { Doc, Pic }, Accepted(Select(new Shortcut { AcceptsInput = { ContentKind.File } }, Dropped)));
        Assert.Equal(new[] { Dir }, Accepted(Select(new Shortcut { AcceptsInput = { ContentKind.Folder } }, Dropped)));
        Assert.Equal(new[] { Pic }, Accepted(Select(new Shortcut { AcceptsInput = { ContentKind.Image } }, Dropped)));
        Assert.Equal(Dropped, Accepted(Select(new Shortcut { AcceptsInput = { ContentKind.Any } }, Dropped)));
        Assert.Equal(Dropped, Accepted(Select(new Shortcut { AcceptsInput = { ContentKind.File, ContentKind.Folder } }, Dropped)));

        // Asking for files takes files only, never folders.
        var asks = Select(new Shortcut { WhenNoInput = WhenNoInput.AskForFiles }, Dropped);
        Assert.Equal(new[] { Doc, Pic }, Accepted(asks));
        Assert.Equal(new[] { new DroppedItem(Dir, true) }, asks.LeftOut);
        Assert.Equal(2, asks.Input.Count);

        var folders = Select(new Shortcut { AcceptsInput = { ContentKind.Folder } }, new[] { Doc });
        Assert.Empty(folders.Accepted);
        Assert.True(folders.Input.IsEmpty);
    }

    [Fact]
    public void The_sheet_lists_the_dropped_items_marks_folders_and_says_what_was_left_out()
    {
        var s = new Shortcut { Name = "Tidy", AcceptsInput = { ContentKind.Folder } };
        var many = Enumerable.Range(1, 11).Select(i => $@"C:\Temp\drop\{i}Folder").Append(Doc);
        var text = DroppedFiles.Describe(s, Select(s, many));
        Assert.StartsWith("Dropped:", text);
        Assert.Contains(@"C:\Temp\drop\1Folder (folder)", text);
        Assert.DoesNotContain(@"9Folder", text);
        Assert.Contains("…and 3 more", text);
        Assert.Contains("Left out, because \"Tidy\" doesn't take them:\n" + Doc, text);
    }

    [Fact]
    public void Paths_keep_full_paths_once_each_and_drop_the_rest()
    {
        var paths = DroppedFiles.Paths(new[] { @"C:\Temp\a.txt", @"c:\temp\A.TXT", "", "   ", @"relative\b.txt", @"C:\Temp\Folder" });
        Assert.Equal(new[] { @"C:\Temp\a.txt", @"C:\Temp\Folder" }, paths);
        Assert.Empty(DroppedFiles.Paths((IEnumerable<string>?)null));
    }

    [Theory]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"\\?\C:\Temp\a.txt")]
    [InlineData(@"//./C:")]
    [InlineData(@"\\?\UNC\server\share\a.txt")]
    public void Device_paths_are_never_handed_on(string path)
    {
        Assert.True(DroppedFiles.IsDevicePath(path));
        Assert.Empty(DroppedFiles.Paths(new[] { path }));
    }

    [Fact]
    public void Share_paths_are_kept_and_count_as_network()
    {
        Assert.Equal(new[] { @"\\server\share\a.txt" }, DroppedFiles.Paths(new[] { @"\\server\share\a.txt" }));
        Assert.True(DroppedFiles.IsNetworkPath(@"\\server\share\a.txt"));
    }

    [Fact]
    public void Shortcut_files_are_never_run_input()
    {
        Assert.False(DroppedFiles.IsForTile(DroppedFiles.Paths(new[] { Package })));
        Assert.True(DroppedFiles.IsForTile(DroppedFiles.Paths(new[] { Package, Doc })));

        var mixed = Select(new Shortcut { AcceptsInput = { ContentKind.Any } }, new[] { Package, Doc });
        Assert.Equal(new[] { Doc }, Accepted(mixed));
        Assert.Equal(new[] { Package }, mixed.ShortcutFiles);
        Assert.Empty(mixed.LeftOut);
        Assert.Single(mixed.Input.Items);
    }

    [Fact]
    public async Task Dropped_files_become_the_Shortcut_Input_without_asking_for_files()
    {
        using var host = new TestHost();
        var b = new ShortcutBuilder("Ask");
        b.Add("content.text", ("text", ParamValue.Text(VarRef.ShortcutInput)));
        var s = b.Build();
        s.WhenNoInput = WhenNoInput.AskForFiles;

        var r = await host.Runner.RunAsync(s, Select(s, Dropped).Input, new RunOptions { Source = RunSource.Library });

        Assert.Equal(RunStatus.Succeeded, r.Status);
        Assert.Contains("a.txt", r.Output.AsText());
        Assert.Contains("b.jpg", r.Output.AsText());
        Assert.DoesNotContain("Photos", r.Output.AsText());
        Assert.DoesNotContain("pickFiles", host.UI.Calls);
    }

    [Fact]
    public void A_drop_on_a_file_changing_shortcut_always_asks_first()
    {
        // Unlike a click, neither the one-time check having been done nor Settings turning it off lets a drop skip the question.
        var actions = Registry();
        var s = With("file.move");
        var drop = Select(s, new[] { Doc });
        Assert.True(TileRunGate.DropAsksFirst(s, actions, drop));

        var settings = new AppSettings { AskBeforeFileChangingRuns = false };
        settings.FileChangeCheckedShortcutIds.Add(s.Id);
        var clickRan = false;
        TileRunGate.Start(s, settings, actions, _ => Assert.Fail("The click check is off here."), () => clickRan = true);
        Assert.True(clickRan);
        Assert.True(TileRunGate.DropAsksFirst(s, actions, drop));
    }

    [Fact]
    public void A_small_local_drop_on_a_shortcut_that_only_reads_runs_straight_away()
    {
        var actions = Registry();
        var s = With("file.readText");
        Assert.False(TileRunGate.DropAsksFirst(s, actions, Select(s, new[] { Doc })));

        var many = Enumerable.Range(0, TileRunGate.ConfirmAboveItems + 1).Select(i => $@"C:\Temp\drop\{i}.txt");
        Assert.True(TileRunGate.DropAsksFirst(s, actions, Select(s, many)));
        Assert.True(TileRunGate.DropAsksFirst(s, actions, Select(s, new[] { Doc }, isNetwork: _ => true)));
    }

    [Fact]
    public void The_click_check_is_unchanged()
    {
        var actions = Registry();
        var s = With("file.move");
        var route = "";
        TileRunGate.Start(s, new AppSettings(), actions, _ => route = "check", () => route = "run");
        Assert.Equal("check", route);
        var done = new AppSettings();
        done.FileChangeCheckedShortcutIds.Add(s.Id);
        TileRunGate.Start(s, done, actions, _ => route = "check", () => route = "run");
        Assert.Equal("run", route);
    }

    [Fact]
    public void Only_one_sheet_per_shortcut()
    {
        var sheets = new SheetPerShortcut<object>();
        var a = Guid.NewGuid();
        var sheet = new object();

        Assert.True(sheets.TryReserve(a, out _));
        Assert.False(sheets.TryReserve(a, out var preparing));
        Assert.Null(preparing);

        sheets.Attach(a, sheet);
        Assert.False(sheets.TryReserve(a, out var open));
        Assert.Same(sheet, open);
        Assert.True(sheets.TryReserve(Guid.NewGuid(), out _));

        sheets.Release(a);
        Assert.False(sheets.IsReserved(a));
        Assert.True(sheets.TryReserve(a, out _));
    }
}
