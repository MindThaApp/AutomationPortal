using System.IO.Compression;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using Xunit;

namespace AutomationPortal.Core.Tests;

public class RecentlyDeletedTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "automationportal-tests", Guid.NewGuid().ToString("N")[..8]);
    private AutomationPortalPaths Paths => AutomationPortalPaths.ForRoot(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static Shortcut Make(string name) { var b = new ShortcutBuilder(name); b.Add("flow.nothing"); return b.Build(); }

    [Fact]
    public void Delete_MovesTheFile_AndRestoreBringsItBackWithTheSameId()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var s = Make("Rename photos");
        lib.Save(s);
        lib.SaveAutomation(new Automation { ShortcutId = s.Id, Trigger = new TriggerInstance { TriggerId = "time.daily" } });
        var original = File.ReadAllBytes(Paths.ShortcutFile(s.Id));

        lib.Delete(s.Id);

        Assert.False(File.Exists(Paths.ShortcutFile(s.Id)));
        Assert.Null(lib.GetById(s.Id));
        var entry = Assert.Single(lib.RecentlyDeleted.List());
        Assert.Equal(s.Id, entry.ShortcutId);
        Assert.Equal("Rename photos", entry.Name);
        Assert.Equal(1, entry.AutomationCount);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(Paths.DeletedFolder, entry.Key)));

        var result = lib.Restore(entry);

        Assert.False(result.AsCopy);
        Assert.Null(result.RenamedFrom);
        Assert.True(result.RemovedFromDeleted);
        Assert.Equal(s.Id, result.Shortcut.Id);
        Assert.Equal("Rename photos", lib.GetById(s.Id)!.Name);
        Assert.True(File.Exists(Paths.ShortcutFile(s.Id)));
        Assert.Empty(lib.RecentlyDeleted.List());
        Assert.Empty(Directory.GetFiles(Paths.DeletedFolder));
        Assert.Single(lib.Automations, a => a.ShortcutId == s.Id);
    }

    [Fact]
    public void Delete_TwiceWithTheSameFileName_KeepsBothCopies()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var s = Make("A");
        lib.Save(s);
        lib.Delete(s.Id);
        lib.Save(s);
        lib.Delete(s.Id);

        var keys = lib.RecentlyDeleted.List().Select(d => d.Key).OrderBy(k => k).ToList();
        Assert.Equal(new[] { $"{s.Id:D} (2).json", $"{s.Id:D}.json" }.OrderBy(k => k), keys);
    }

    [Fact]
    public void Restore_WhenIdAndNameAreTaken_ComesBackAsARenamedCopy_AndOverwritesNothing()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var s = Make("Backup");
        s.Hotkey = "Ctrl+Alt+B";
        lib.Save(s);
        lib.Delete(s.Id);
        var entry = Assert.Single(lib.RecentlyDeleted.List());

        var again = Make("Backup");
        again.Id = s.Id;
        lib.Save(again);
        var before = File.ReadAllBytes(Paths.ShortcutFile(s.Id));

        var result = lib.Restore(entry);

        Assert.True(result.AsCopy);
        Assert.NotEqual(s.Id, result.Shortcut.Id);
        Assert.Equal("Backup", result.RenamedFrom);
        Assert.Equal("Backup 2", result.Shortcut.Name);
        Assert.Null(result.Shortcut.Hotkey);
        Assert.Equal(before, File.ReadAllBytes(Paths.ShortcutFile(s.Id)));
        Assert.Equal(2, lib.All.Count);
        Assert.True(File.Exists(Paths.ShortcutFile(result.Shortcut.Id)));
    }

    [Fact]
    public void Restore_WhenOnlyTheNameIsTaken_KeepsTheIdAndAddsANumber()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var s = Make("Notes");
        lib.Save(s);
        lib.Delete(s.Id);
        lib.Save(Make("Notes"));

        var result = lib.Restore(Assert.Single(lib.RecentlyDeleted.List()));

        Assert.False(result.AsCopy);
        Assert.Equal(s.Id, result.Shortcut.Id);
        Assert.Equal("Notes 2", result.Shortcut.Name);
    }

    [Fact]
    public void Purge_RemovesOnlyExpiredItems_AndOnlyInsideTheDeletedFolder()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var old = Make("Old"); var fresh = Make("Fresh"); var kept = Make("Kept");
        lib.Save(old); lib.Save(fresh); lib.Save(kept);
        lib.Delete(old.Id); lib.Delete(fresh.Id);

        var oldInfo = InfoOf(old.Id);
        var now = DateTimeOffset.Now;
        SetDeletedAt(oldInfo, now - RecentlyDeleted.KeepFor - TimeSpan.FromDays(1));
        var stranger = Path.Combine(Paths.DeletedFolder, "not ours.txt");
        File.WriteAllText(stranger, "x");
        var outside = Path.Combine(_root, "outside.json");
        File.WriteAllText(outside, "{}");
        File.SetLastWriteTime(outside, DateTime.Now.AddYears(-2));

        Assert.Equal(0, lib.RecentlyDeleted.Purge(now));
        Assert.Equal(0, lib.RecentlyDeleted.Purge(now + TimeSpan.FromHours(1)));
        Assert.Equal(2, lib.RecentlyDeleted.List().Count);

        var removed = lib.RecentlyDeleted.Purge(now + TimeSpan.FromHours(25));

        Assert.Equal(1, removed);
        Assert.Equal("Fresh", Assert.Single(lib.RecentlyDeleted.List()).Name);
        Assert.False(File.Exists(Path.Combine(Paths.DeletedFolder, $"{old.Id:D}.json")));
        Assert.False(File.Exists(oldInfo));
        Assert.True(File.Exists(stranger));
        Assert.True(File.Exists(outside));
        Assert.True(File.Exists(Paths.ShortcutFile(kept.Id)));
        var later = now + RecentlyDeleted.KeepFor + TimeSpan.FromDays(1);
        Assert.Equal(0, lib.RecentlyDeleted.Purge(later));
        Assert.Equal(1, lib.RecentlyDeleted.Purge(later + TimeSpan.FromHours(25)));
    }

    [Fact]
    public void Purge_AClockThatJumpsAheadOnce_RemovesNothing()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var a = Make("A"); var b = Make("B");
        lib.Save(a); lib.Save(b);
        lib.Delete(a.Id); lib.Delete(b.Id);
        var now = DateTimeOffset.Now;

        Assert.Equal(0, lib.RecentlyDeleted.Purge(now + TimeSpan.FromDays(400)));
        Assert.Equal(0, lib.RecentlyDeleted.Purge(now + TimeSpan.FromHours(30)));
        Assert.Equal(0, lib.RecentlyDeleted.Purge(now + TimeSpan.FromHours(60)));

        Assert.Equal(2, lib.RecentlyDeleted.List().Count);
    }

    [Fact]
    public void Purge_NeverRemovesItemsWithAMissingOrFutureDeletionTime()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var missing = Make("Missing"); var future = Make("Future");
        lib.Save(missing); lib.Save(future);
        lib.Delete(missing.Id); lib.Delete(future.Id);
        var now = DateTimeOffset.Now;
        SetDeletedAt(InfoOf(missing.Id), null);
        SetDeletedAt(InfoOf(future.Id), now + TimeSpan.FromDays(10));

        foreach (var at in new[] { now, now + TimeSpan.FromHours(25), now + TimeSpan.FromDays(2), now + TimeSpan.FromDays(3) })
            Assert.Equal(0, lib.RecentlyDeleted.Purge(at));

        Assert.Equal(2, lib.RecentlyDeleted.List().Count);
        Assert.True(File.Exists(Path.Combine(Paths.DeletedFolder, $"{missing.Id:D}.json")));
        Assert.True(File.Exists(Path.Combine(Paths.DeletedFolder, $"{future.Id:D}.json")));
    }

    [Fact]
    public void Orphans_AreListedAsDamaged_RestorableWhenTheyParse_AndNeverPurged()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var noInfo = Make("No info"); var badInfo = Make("Bad info");
        lib.Save(noInfo); lib.Save(badInfo);
        lib.Delete(noInfo.Id); lib.Delete(badInfo.Id);
        File.Delete(InfoOf(noInfo.Id));
        File.WriteAllText(InfoOf(badInfo.Id), "{ not json");
        var garbage = Path.Combine(Paths.DeletedFolder, "garbage.json");
        File.WriteAllText(garbage, "not a shortcut");
        var files = Directory.GetFiles(Paths.DeletedFolder).Where(f => !f.EndsWith(".dat")).OrderBy(f => f).ToList();

        var list = lib.RecentlyDeleted.List();

        Assert.Equal(3, list.Count);
        Assert.All(list, d => { Assert.True(d.Damaged); Assert.Equal(RecentlyDeleted.DamagedName, d.Name); });
        Assert.True(list.Single(d => d.ShortcutId == noInfo.Id).CanRestore);
        Assert.True(list.Single(d => d.ShortcutId == badInfo.Id).CanRestore);
        Assert.False(list.Single(d => d.Key == "garbage.json").CanRestore);

        var far = DateTimeOffset.Now + TimeSpan.FromDays(400);
        Assert.Equal(0, lib.RecentlyDeleted.Purge(far));
        Assert.Equal(0, lib.RecentlyDeleted.Purge(far + TimeSpan.FromDays(2)));
        Assert.Equal(files, Directory.GetFiles(Paths.DeletedFolder).Where(f => !f.EndsWith(".dat")).OrderBy(f => f).ToList());

        var result = lib.Restore(list.Single(d => d.ShortcutId == noInfo.Id));
        Assert.Equal(noInfo.Id, result.Shortcut.Id);
        Assert.Equal("No info", result.Shortcut.Name);
        Assert.True(result.RemovedFromDeleted);
    }

    [Fact]
    public void Delete_WhenTheCopyFails_KeepsTheOriginal()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var s = Make("Keep me");
        lib.Save(s);
        var before = File.ReadAllBytes(Paths.ShortcutFile(s.Id));
        Directory.CreateDirectory(Path.GetDirectoryName(Paths.DeletedFolder)!);
        File.WriteAllText(Paths.DeletedFolder, "a file where the folder should be");

        Assert.Throws<LibraryException>(() => lib.Delete(s.Id));

        Assert.Equal(before, File.ReadAllBytes(Paths.ShortcutFile(s.Id)));
        Assert.NotNull(lib.GetById(s.Id));
        Assert.Equal("a file where the folder should be", File.ReadAllText(Paths.DeletedFolder));
    }

    [Fact]
    public void Delete_WhenRemovingAFileFails_RollsBackTheStagedCopies()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var s = Make("Busy");
        lib.Save(s);
        var conflict = Path.Combine(Paths.ShortcutsFolder, "busy-conflict.json");
        File.Copy(Paths.ShortcutFile(s.Id), conflict);
        lib.Reload();

        using (new FileStream(conflict, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<LibraryException>(() => lib.Delete(s.Id));

        Assert.True(File.Exists(Paths.ShortcutFile(s.Id)));
        Assert.True(File.Exists(conflict));
        Assert.NotNull(lib.GetById(s.Id));
        Assert.Empty(lib.RecentlyDeleted.List());
        Assert.Empty(Directory.GetFiles(Paths.DeletedFolder, "*.json*"));
    }

    [Fact]
    public void Delete_WithADuplicateFile_MovesBothToRecentlyDeleted_SoItStaysDeleted()
    {
        var s = Make("Twice");
        var conflict = Path.Combine(Paths.ShortcutsFolder, "twice-conflict.json");
        using (var lib = new JsonShortcutLibrary(Paths, null, watch: false))
        {
            lib.Save(s);
            File.Copy(Paths.ShortcutFile(s.Id), conflict);
            lib.Reload();

            lib.Delete(s.Id);

            Assert.False(File.Exists(Paths.ShortcutFile(s.Id)));
            Assert.False(File.Exists(conflict));
            var list = lib.RecentlyDeleted.List();
            Assert.Equal(2, list.Count);
            Assert.All(list, d => Assert.Equal(s.Id, d.ShortcutId));
            Assert.Contains(list, d => d.OriginalFileName == "twice-conflict.json");
        }
        using var reopened = new JsonShortcutLibrary(Paths, null, watch: false);
        Assert.Null(reopened.GetById(s.Id));
    }

    [Fact]
    public void Delete_WithDuplicates_KeepsEarlierDupFilesAndEveryDuplicate()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var s = Make("Synced");
        lib.Save(s);
        var first = Path.Combine(Paths.ShortcutsFolder, "synced-pc1.json");
        var second = Path.Combine(Paths.ShortcutsFolder, "synced-pc2.json");
        File.WriteAllText(first, File.ReadAllText(Paths.ShortcutFile(s.Id)).Replace("\"Synced\"", "\"Synced edit 1\""));
        File.WriteAllText(second, File.ReadAllText(Paths.ShortcutFile(s.Id)).Replace("\"Synced\"", "\"Synced edit 2\""));
        var oldDup = first + ".dup";
        File.WriteAllText(oldDup, "an earlier set-aside copy");
        lib.Reload();

        lib.Delete(s.Id);

        Assert.Equal("an earlier set-aside copy", File.ReadAllText(oldDup));
        var list = lib.RecentlyDeleted.List();
        Assert.Equal(3, list.Count);
        Assert.Contains(list, d => d.Name == "Synced edit 1");
        Assert.Contains(list, d => d.Name == "Synced edit 2");
        Assert.Contains(list, d => d.Name == "Synced");
    }

    [Fact]
    public void Restore_WhenTheIdsFileIsOnDiskButNotLoaded_ComesBackAsACopy_AndLeavesTheFile()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var s = Make("Outside");
        lib.Save(s);
        lib.Delete(s.Id);
        var entry = Assert.Single(lib.RecentlyDeleted.List());
        var other = Make("Written elsewhere");
        other.Id = s.Id;
        File.WriteAllText(Paths.ShortcutFile(s.Id), AutomationPortalJson.Serialize(other, indented: true));
        var before = File.ReadAllBytes(Paths.ShortcutFile(s.Id));

        var result = lib.Restore(entry);

        Assert.True(result.AsCopy);
        Assert.NotEqual(s.Id, result.Shortcut.Id);
        Assert.Equal(before, File.ReadAllBytes(Paths.ShortcutFile(s.Id)));
        Assert.True(File.Exists(Paths.ShortcutFile(result.Shortcut.Id)));
    }

    [Fact]
    public void Restore_KeepingItsId_LeavesOffAHotkeyAndAliasAnotherShortcutUses()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var s = Make("Mine");
        s.Hotkey = "Ctrl+Alt+B"; s.Alias = "bk";
        lib.Save(s);
        lib.Delete(s.Id);
        var other = Make("Other");
        other.Hotkey = "alt + ctrl+B"; other.Alias = "BK";
        lib.Save(other);

        var result = lib.Restore(Assert.Single(lib.RecentlyDeleted.List()));

        Assert.False(result.AsCopy);
        Assert.Equal("Ctrl+Alt+B", result.ClearedHotkey);
        Assert.Equal("bk", result.ClearedAlias);
        Assert.Null(lib.GetById(s.Id)!.Hotkey);
        Assert.Null(lib.GetById(s.Id)!.Alias);
        Assert.Equal("alt + ctrl+B", lib.GetById(other.Id)!.Hotkey);
    }

    [Fact]
    public void Empty_WithListedItems_LeavesItemsAddedOrReplacedSince()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var a = Make("A"); var b = Make("B");
        lib.Save(a); lib.Save(b);
        lib.Delete(a.Id);
        var counted = lib.RecentlyDeleted.List();
        lib.Restore(Assert.Single(counted));
        lib.Delete(a.Id);
        lib.Delete(b.Id);

        Assert.Equal(0, lib.RecentlyDeleted.Empty(counted));
        Assert.Equal(2, lib.RecentlyDeleted.List().Count);
    }

    [Fact]
    public void Backup_RefusesATargetInsideTheLibraryReachedThroughAJunction()
    {
        Directory.CreateDirectory(Paths.LibraryFolder);
        var link = Path.Combine(_root, "link-to-library");
        var mk = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{Paths.LibraryFolder}\"")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
        mk.WaitForExit(10000);
        if (!Directory.Exists(link)) return;
        try
        {
            Assert.Throws<LibraryException>(() => LibraryBackup.Create(Paths, link, DateTime.Now));
            Assert.Throws<LibraryException>(() => LibraryBackup.Create(Paths, Path.Combine(link, "backups"), DateTime.Now));
            Assert.False(Directory.Exists(Path.Combine(Paths.LibraryFolder, "backups")));
            Assert.Empty(Directory.GetFiles(Paths.LibraryFolder, "*.zip", SearchOption.AllDirectories));
        }
        finally
        {
            // Removes only the junction itself (non-recursive), never the library it points to.
            Directory.Delete(link);
        }
    }

    private string InfoOf(Guid id) => Path.Combine(Paths.DeletedFolder, $"{id:D}.json.info");

    private static void SetDeletedAt(string infoPath, DateTimeOffset? value)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(infoPath))!.AsObject();
        var key = node.Select(p => p.Key).First(k => k.Equals("DeletedAt", StringComparison.OrdinalIgnoreCase));
        if (value is null) node.Remove(key); else node[key] = value.Value;
        File.WriteAllText(infoPath, node.ToJsonString());
    }

    [Fact]
    public void Empty_LeavesFilesItDidNotCreate()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var a = Make("A"); var b = Make("B");
        lib.Save(a); lib.Save(b);
        lib.Delete(a.Id); lib.Delete(b.Id);
        var stranger = Path.Combine(Paths.DeletedFolder, "notes.txt");
        File.WriteAllText(stranger, "{}");

        Assert.Equal(2, lib.RecentlyDeleted.Empty());
        Assert.Empty(lib.RecentlyDeleted.List());
        Assert.Equal(new[] { stranger }, Directory.GetFiles(Paths.DeletedFolder));
    }

    [Fact]
    public void Backup_ZipHoldsTheLibraryFiles_AndLeavesThemUnchanged()
    {
        var a = Make("Resize images"); var b = Make("Clean desktop");
        using (var lib = new JsonShortcutLibrary(Paths, null, watch: false))
        {
            lib.Save(a); lib.Save(b);
            lib.SaveAutomation(new Automation { ShortcutId = a.Id, Trigger = new TriggerInstance { TriggerId = "time.daily" } });
        }
        var files = Directory.GetFiles(Paths.LibraryFolder, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, f => (Bytes: File.ReadAllBytes(f), Time: File.GetLastWriteTimeUtc(f)));
        var target = Path.Combine(_root, "backups");
        var when = new DateTime(2026, 11, 11, 9, 30, 0);

        var first = LibraryBackup.Create(Paths, target, when);
        var second = LibraryBackup.Create(Paths, target, when);

        Assert.NotEqual(first, second);
        Assert.Equal("Automation Portal backup 2026-11-11 093000.zip", Path.GetFileName(first));
        Assert.Equal("Automation Portal backup 2026-11-11 093000 (2).zip", Path.GetFileName(second));
        using (var zip = ZipFile.OpenRead(first))
        {
            Assert.Equal(File.ReadAllBytes(Paths.ShortcutFile(a.Id)), Read(zip, $"Library/shortcuts/{a.Id:D}.json"));
            Assert.Equal(File.ReadAllBytes(Paths.ShortcutFile(b.Id)), Read(zip, $"Library/shortcuts/{b.Id:D}.json"));
            Assert.Equal(File.ReadAllBytes(Paths.AutomationsFile), Read(zip, "Library/automations.json"));
            var imported = ShortcutPackage.Parse(System.Text.Encoding.UTF8.GetString(Read(zip, "Shortcuts to import/Resize images.autoportal")));
            Assert.Equal(a.Id, imported.Id);
        }
        var after = Directory.GetFiles(Paths.LibraryFolder, "*", SearchOption.AllDirectories);
        Assert.Equal(files.Keys.OrderBy(f => f), after.OrderBy(f => f));
        foreach (var (f, v) in files)
        {
            Assert.Equal(v.Bytes, File.ReadAllBytes(f));
            Assert.Equal(v.Time, File.GetLastWriteTimeUtc(f));
        }
        Assert.Throws<LibraryException>(() => LibraryBackup.Create(Paths, Path.Combine(Paths.LibraryFolder, "b"), when));
    }

    private static byte[] Read(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name) ?? throw new Xunit.Sdk.XunitException("Missing zip entry " + name);
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
