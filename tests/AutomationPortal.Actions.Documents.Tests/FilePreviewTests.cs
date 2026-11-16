using System.Security.Cryptography;
using AutomationPortal.Actions.Documents;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;

namespace AutomationPortal.Actions.Documents.Tests;

/// <summary>"Show what it would do": the preview lists exactly what a real run does, and changes nothing. Every folder is under %TEMP%.</summary>
public class FilePreviewTests
{
    private static TestHost Host() => T.Host().With(typeof(AutomationPortal.Actions.Content.Utilities.ExpressionEvaluator).Assembly);

    private static string Gallery()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "gallery")) && File.Exists(Path.Combine(dir.FullName, "AutomationPortal.sln"))) return Path.Combine(dir.FullName, "gallery");
        throw new InvalidOperationException("gallery folder not found");
    }

    private static Shortcut Template(string file) => ShortcutPackage.Parse(File.ReadAllText(Path.Combine(Gallery(), file)));

    /// <summary>Every file and folder under <paramref name="root"/> with its size, write time and contents hash.</summary>
    private static SortedDictionary<string, string> Snapshot(string root)
    {
        var map = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            map[d] = "dir " + Directory.GetLastWriteTimeUtc(d).Ticks;
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(f);
            map[f] = $"{info.Length} {info.LastWriteTimeUtc.Ticks} {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))}";
        }
        return map;
    }

    private static string Old(TempDir d, string name, string text)
    {
        var p = Path.GetFullPath(d.File(name, text));
        File.SetLastWriteTime(p, DateTime.Now.AddDays(-30));
        return p;
    }

    private static Shortcut TidyIn(string folder)
    {
        var s = Template("template-tidy-downloads.autoportal");
        var get = s.FindAction("tidyFolder")!;
        get.Parameters["folder"] = ParamValue.Choice("chosen");
        get.Parameters["chosen"] = ParamValue.Text(folder);
        return s;
    }

    [Fact]
    public async Task Tidy_preview_lists_each_move_and_changes_nothing()
    {
        using var d = new TempDir();
        var dl = d.Folder("dl");
        var photo = Old(d, "dl/photo.png", "png");
        var report = Old(d, "dl/report.pdf", "pdf");
        d.File("dl/fresh.txt", "new");                       // newer than 7 days: left alone
        Old(d, "dl/Pictures/keep.png", "in a subfolder");      // not loose: left alone
        var before = Snapshot(d.Path);

        var host = Host();
        var plan = await FilePreview.RunAsync(host.Runner, TidyIn(dl), ContentValue.Empty);

        var moves = plan.Changes.Where(c => c.Kind == PlannedChangeKind.Move).Select(c => (c.Source, c.Target)).OrderBy(x => x.Source).ToList();
        Assert.Equal(new[]
        {
            (photo, Path.Combine(dl, Transfer.ExpandSubfolder("{kind}", photo), "photo.png")),
            (report, Path.Combine(dl, Transfer.ExpandSubfolder("{kind}", report), "report.pdf")),
        }.OrderBy(x => x.Item1), moves.Select(m => (m.Source!, m.Target!)));
        Assert.Equal(2, plan.ChangeCount);
        Assert.Contains(plan.Changes, c => c.Kind == PlannedChangeKind.NotRun);   // the notification is not shown
        Assert.StartsWith("2 items would move.", plan.Summary());
        Assert.Equal(before, Snapshot(d.Path));
        Assert.Empty(host.History.Records);
        Assert.Empty(host.Notifications.Shown);
    }

    [Fact]
    public async Task Name_collisions_get_2_in_the_preview_and_in_the_real_run_alike()
    {
        using var d = new TempDir();
        var dl = d.Folder("dl");
        var src = Old(d, "dl/notes.txt", "new notes");
        var kind = Transfer.ExpandSubfolder("{kind}", src);
        var existing = d.File(Path.Combine("dl", kind, "notes.txt"), "old notes");
        var host = Host();
        var s = TidyIn(dl);

        var plan = await FilePreview.RunAsync(host.Runner, s, ContentValue.Empty);
        var planned = plan.Changes.Single(c => c.Kind == PlannedChangeKind.Move);
        Assert.Equal(Path.Combine(dl, kind, "notes (2).txt"), planned.Target);
        Assert.True(File.Exists(src));

        var result = await host.Runner.RunAsync(s, ContentValue.Empty);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal("new notes", File.ReadAllText(planned.Target!));
        Assert.Equal("old notes", File.ReadAllText(existing));
        Assert.False(File.Exists(src));
    }

    [Fact]
    public async Task Convert_preview_names_new_files_and_the_real_run_leaves_originals_byte_identical()
    {
        using var d = new TempDir();
        d.Folder("pics");
        var a = await T.MakeImage(Path.Combine(d.Path, "pics", "photo.png"), 40, 30);
        var b = await T.MakeImage(Path.Combine(d.Path, "pics", "other.png"), 30, 20);
        var taken = d.File("pics/photo.jpg", "already here");
        var hashA = SHA256.HashData(File.ReadAllBytes(a));
        var hashB = SHA256.HashData(File.ReadAllBytes(b));
        var host = Host();
        var s = Template("template-convert-pictures.autoportal");
        var input = ContentValue.Files(new[] { a, b });
        var before = Snapshot(d.Path);

        var plan = await FilePreview.RunAsync(host.Runner, s, input);
        var targets = plan.Changes.Where(c => c.Kind == PlannedChangeKind.NewFile).Select(c => c.Target).ToList();
        Assert.Equal(new[] { Path.Combine(d.Path, "pics", "photo (2).jpg"), Path.Combine(d.Path, "pics", "other.jpg") }, targets);
        Assert.Equal(before, Snapshot(d.Path));

        var result = await host.Runner.RunAsync(s, input);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(targets, T.Paths(result.Output));
        Assert.All(targets, t => Assert.True(File.Exists(t)));
        Assert.Equal(hashA, SHA256.HashData(File.ReadAllBytes(a)));
        Assert.Equal(hashB, SHA256.HashData(File.ReadAllBytes(b)));
        Assert.Equal("already here", File.ReadAllText(taken));
    }

    [Fact]
    public async Task Backup_copies_into_a_dated_folder_and_never_deletes_or_replaces_in_the_destination()
    {
        using var d = new TempDir();
        var source = d.Folder("Work");
        d.File("Work/a.txt", "A");
        d.File("Work/sub/b.txt", "B");
        var dest = d.Folder("Backups");
        var today = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        d.File("Backups/older/a.txt", "an older backup");
        d.File($"Backups/{today}/Work/a.txt", "earlier today");
        var s = Template("template-folder-backup.autoportal");
        var from = s.FindAction("backFrom01")!;
        from.Parameters["folder"] = ParamValue.Choice("chosen");
        from.Parameters["chosen"] = ParamValue.Text(source);
        s.FindAction("backDest01")!.Parameters["parent"] = ParamValue.Text(dest);
        var host = Host();
        var destBefore = Snapshot(dest);
        var all = Snapshot(d.Path);

        var plan = await FilePreview.RunAsync(host.Runner, s, ContentValue.Empty);
        var copy = plan.Changes.Single(c => c.Kind == PlannedChangeKind.Copy);
        Assert.Equal(Path.Combine(dest, today, "Work (2)"), copy.Target);
        Assert.DoesNotContain(plan.Changes, c => c.Kind is PlannedChangeKind.Recycle or PlannedChangeKind.Delete or PlannedChangeKind.Move);
        Assert.Equal(all, Snapshot(d.Path));

        for (var run = 0; run < 2; run++)
        {
            var result = await host.Runner.RunAsync(s, ContentValue.Empty);
            Assert.True(result.Succeeded, result.ErrorMessage);
        }
        var after = Snapshot(dest);
        foreach (var (path, state) in destBefore)
            Assert.True(after.TryGetValue(path, out var now) && (path.EndsWith(today, StringComparison.OrdinalIgnoreCase) || now == state), path);
        Assert.Equal("B", File.ReadAllText(Path.Combine(dest, today, "Work (2)", "sub", "b.txt")));
        Assert.True(Directory.Exists(Path.Combine(dest, today, "Work (3)")));
        Assert.Equal("A", File.ReadAllText(Path.Combine(source, "a.txt")));
    }

    [Fact]
    public async Task Work_setup_preview_runs_nothing_and_plans_no_file_changes()
    {
        // The Windows actions are deliberately not registered: even a broken preview could not open anything here.
        var plan = await FilePreview.RunAsync(Host().Runner, Template("template-work-setup.autoportal"), ContentValue.Empty);
        Assert.Equal(0, plan.ChangeCount);
        Assert.Contains(plan.Changes, c => c.Kind == PlannedChangeKind.NotRun);
    }

    [Fact]
    public async Task Delete_and_unknown_steps_are_listed_but_not_run()
    {
        using var d = new TempDir();
        var f = d.File("x.txt", "keep me");
        var s = new Shortcut { Name = "t" };
        s.Actions.Add(new ActionInstance { Uid = "del", ActionId = "file.delete", Parameters = { ["files"] = ParamValue.Text(f) } });
        s.Actions.Add(new ActionInstance { Uid = "dl", ActionId = "web.download", Parameters = { ["url"] = ParamValue.Text("https://example.invalid/") } });
        var before = Snapshot(d.Path);

        var plan = await FilePreview.RunAsync(Host().Runner, s, ContentValue.Empty);

        Assert.Equal(new[] { PlannedChangeKind.Recycle, PlannedChangeKind.CantPreview }, plan.Changes.Select(c => c.Kind));
        Assert.Equal(f, plan.Changes[0].Source);
        Assert.True(plan.IsIncomplete);
        Assert.Equal(before, Snapshot(d.Path));
    }

    [Fact]
    public async Task Rename_preview_matches_the_planner_and_renames_nothing()
    {
        using var d = new TempDir();
        var a = d.File("one.txt", "1");
        d.File("Holiday 1.txt", "taken");
        var s = new Shortcut { Name = "t" };
        s.Actions.Add(new ActionInstance { Uid = "ren", ActionId = "file.rename", Parameters = { ["files"] = ParamValue.Text(a), ["pattern"] = ParamValue.Text("Holiday {n}") } });
        var before = Snapshot(d.Path);

        var plan = await FilePreview.RunAsync(Host().Runner, s, ContentValue.Empty);

        var rename = plan.Changes.Single();
        Assert.Equal(PlannedChangeKind.Rename, rename.Kind);
        Assert.NotEqual(Path.Combine(d.Path, "Holiday 1.txt"), rename.Target);
        Assert.Equal(before, Snapshot(d.Path));
    }

    [Fact]
    public void Templates_use_known_actions_and_only_the_backup_place_is_left_blank()
    {
        var host = Host().With(typeof(AutomationPortal.Actions.Windows.Apps.InstalledApps).Assembly);
        foreach (var file in Directory.EnumerateFiles(Gallery(), "template-*.autoportal"))
        {
            var s = ShortcutPackage.Parse(File.ReadAllText(file));
            Assert.All(s.Actions, a => Assert.NotNull(host.Registry.Describe(a.ActionId)));
            Assert.All(s.ImportQuestions, q => Assert.NotNull(s.FindAction(q.ActionUid)));
            var errors = ShortcutValidator.Validate(s, host.Registry).Where(i => i.Severity.ToString() == "Error").ToList();
            Assert.All(errors, e => Assert.Equal("backDest01", e.ActionUid));
        }
    }
}
