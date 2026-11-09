using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.Core.Tests;

/// <summary>Data from the folders used before the rename is copied once, never moved or changed. Temp folders only.</summary>
public sealed class DataMigrationTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "automationportal-migration-tests", Guid.NewGuid().ToString("N")[..8]);

    public void Dispose() { try { Directory.Delete(_base, true); } catch { } }

    private string Old => Path.Combine(_base, "Flowmate");
    private string New => Path.Combine(_base, "Automation Portal");

    private void Seed(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "Library", "shortcuts"));
        File.WriteAllText(Path.Combine(root, "settings.json"), "{\"theme\":\"dark\"}");
        File.WriteAllBytes(Path.Combine(root, "secrets.dat"), new byte[] { 1, 2, 3, 4, 5 });
        File.WriteAllText(Path.Combine(root, "Library", "shortcuts", "a.json"), "{}");
        Directory.CreateDirectory(Path.Combine(root, "empty"));
    }

    private static string[] Listing(string root) =>
        Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(root, p) + (File.Exists(p) ? ":" + new FileInfo(p).Length : "")).Order().ToArray();

    private string[] Siblings() => Directory.GetFileSystemEntries(_base).Select(Path.GetFileName).Order().ToArray()!;

    [Fact]
    public void The_old_tree_is_copied_into_the_new_folder_and_the_old_one_is_left_alone()
    {
        Seed(Old);
        var before = Listing(Old);
        var lines = new List<string>();

        var used = DataRootMigration.Resolve(Old, New, lines.Add);

        Assert.Equal(New, used);
        Assert.Equal(before, Listing(New));
        Assert.Equal(before, Listing(Old));
        Assert.Equal("{\"theme\":\"dark\"}", File.ReadAllText(Path.Combine(New, "settings.json")));
        Assert.Equal(new[] { "Automation Portal", "Flowmate" }, Siblings());   // no temp folder left behind
        Assert.Single(lines);
    }

    [Fact]
    public void Stale_copies_from_a_crash_are_removed_and_nothing_else_is()
    {
        Seed(Old);
        var stale = New + ".migrating-0a1b2c3d";
        var recent = New + ".migrating-ffffffff";
        var lookalikes = new[] { New + ".migrating-0a1b2c3", New + ".migrating-0a1b2c3dx", "Other.migrating-0a1b2c3d", New + "-x.migrating-0a1b2c3d" };
        foreach (var d in lookalikes.Append(stale).Append(recent)) { Directory.CreateDirectory(d); File.WriteAllText(Path.Combine(d, "f.txt"), "x"); }
        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-2));
        foreach (var d in lookalikes) Directory.SetLastWriteTimeUtc(d, DateTime.UtcNow.AddDays(-2));

        Assert.Equal(New, DataRootMigration.Resolve(Old, New));

        Assert.False(Directory.Exists(stale));
        Assert.True(Directory.Exists(recent));   // might still be in use by another copy of the app
        foreach (var d in lookalikes) Assert.True(Directory.Exists(d), d);
    }

    [Fact]
    public void The_temp_folder_is_not_copied()
    {
        Seed(Old);
        Directory.CreateDirectory(Path.Combine(Old, "temp", "run1"));
        File.WriteAllText(Path.Combine(Old, "temp", "run1", "x.tmp"), "x");
        Directory.CreateDirectory(Path.Combine(Old, "Library", "temp"));   // only the top-level one is disposable
        File.WriteAllText(Path.Combine(Old, "Library", "temp", "keep.txt"), "k");

        Assert.Equal(New, DataRootMigration.Resolve(Old, New));

        Assert.False(Directory.Exists(Path.Combine(New, "temp")));
        Assert.True(File.Exists(Path.Combine(New, "Library", "temp", "keep.txt")));
        Assert.True(File.Exists(Path.Combine(Old, "temp", "run1", "x.tmp")));
    }

    [Fact]
    public void A_linked_folder_stops_the_copy_and_the_old_folder_is_used()
    {
        Seed(Old);
        var target = Path.Combine(_base, "elsewhere");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "t.txt"), "t");
        var link = Path.Combine(Old, "Library", "linked");
        try { Directory.CreateSymbolicLink(link, target); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }   // links need rights on this PC; nothing to test
        var lines = new List<string>();

        Assert.Equal(Old, DataRootMigration.Resolve(Old, New, lines.Add));

        Assert.False(Directory.Exists(New));
        Assert.Single(lines);
        Assert.Contains("linked", lines[0]);
        Assert.True(File.Exists(Path.Combine(target, "t.txt")));
    }

    [Fact]
    public void An_existing_new_folder_is_used_as_it_is()
    {
        Seed(Old);
        Directory.CreateDirectory(New);
        File.WriteAllText(Path.Combine(New, "settings.json"), "new");

        Assert.Equal(New, DataRootMigration.Resolve(Old, New));

        Assert.Equal(new[] { "settings.json:3" }, Listing(New));
    }

    [Fact]
    public void Nothing_is_created_when_there_is_no_old_folder()
    {
        Directory.CreateDirectory(_base);
        Assert.Equal(New, DataRootMigration.Resolve(Old, New));
        Assert.Empty(Siblings());
    }

    [Fact]
    public void A_failed_copy_leaves_no_half_copy_and_the_old_folder_is_used()
    {
        Seed(Old);
        var before = Listing(Old);
        var lines = new List<string>();
        string used;
        using (new FileStream(Path.Combine(Old, "secrets.dat"), FileMode.Open, FileAccess.Read, FileShare.None))   // locked: cannot be copied
            used = DataRootMigration.Resolve(Old, New, lines.Add);

        Assert.Equal(Old, used);
        Assert.False(Directory.Exists(New));
        Assert.Equal(new[] { "Flowmate" }, Siblings());
        Assert.Equal(before, Listing(Old));
        Assert.Contains("old folder", Assert.Single(lines));
    }

    [Fact]
    public void The_paths_use_the_copied_roots_and_note_it_in_the_new_log()
    {
        var oldRoaming = Path.Combine(_base, "old-roaming"); var oldLocal = Path.Combine(_base, "old-local");
        var newRoaming = Path.Combine(_base, "new-roaming"); var newLocal = Path.Combine(_base, "new-local");
        Seed(oldRoaming); Seed(oldLocal);

        var paths = new AutomationPortalPaths(newRoaming, newLocal, null, oldRoaming, oldLocal);

        Assert.Equal(newRoaming, paths.RoamingRoot);
        Assert.Equal(newLocal, paths.LocalRoot);
        Assert.Equal(Path.Combine(newRoaming, "Library"), paths.LibraryFolder);
        Assert.True(File.Exists(paths.SettingsFile));
        Assert.Single(File.ReadAllLines(Path.Combine(paths.LogsFolder, "migration.log")));
        Assert.False(File.Exists(Path.Combine(oldLocal, "logs", "migration.log")));   // the old folder is never written to
    }

    [Fact]
    public void Roots_that_are_given_without_an_old_location_never_migrate()
    {
        Seed(Old);
        var paths = new AutomationPortalPaths(New, Path.Combine(_base, "local"));
        Assert.Equal(New, paths.RoamingRoot);
        Assert.False(Directory.Exists(New));
    }

    [Fact]
    public void Files_from_before_the_rename_still_import()
    {
        var json = ShortcutPackage.Serialize(new ShortcutBuilder("Old one").Build()).Replace("autoportal.shortcut", "flowmate.shortcut");
        Assert.Equal("Old one", ShortcutPackage.Parse(json).Name);
        Assert.True(ShortcutPackage.HasPackageExtension(@"C:\x\a.flowmate"));
        Assert.True(ShortcutPackage.HasPackageExtension(@"C:\x\a.AUTOPORTAL"));
        Assert.False(ShortcutPackage.HasPackageExtension(@"C:\x\a.txt"));
        Assert.Equal(@"C:\x\a.flowmate", LaunchArguments.Parse(new[] { @"C:\x\a.flowmate" }).ImportFile);
        Assert.Null(LaunchArguments.LinkImportProblem(@"C:\x\a.flowmate"));
    }
}
