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

        var oldInfo = Path.Combine(Paths.DeletedFolder, $"{old.Id:D}.json.info");
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(oldInfo))!.AsObject();
        var key = node.Select(p => p.Key).First(k => k.Equals("DeletedAt", StringComparison.OrdinalIgnoreCase));
        node[key] = DateTimeOffset.Now - RecentlyDeleted.KeepFor - TimeSpan.FromDays(1);
        File.WriteAllText(oldInfo, node.ToJsonString());
        var stranger = Path.Combine(Paths.DeletedFolder, "not ours.txt");
        File.WriteAllText(stranger, "x");
        var outside = Path.Combine(_root, "outside.json");
        File.WriteAllText(outside, "{}");
        File.SetLastWriteTime(outside, DateTime.Now.AddYears(-2));

        var removed = lib.RecentlyDeleted.Purge(DateTimeOffset.Now);

        Assert.Equal(1, removed);
        Assert.Equal("Fresh", Assert.Single(lib.RecentlyDeleted.List()).Name);
        Assert.False(File.Exists(Path.Combine(Paths.DeletedFolder, $"{old.Id:D}.json")));
        Assert.False(File.Exists(oldInfo));
        Assert.True(File.Exists(stranger));
        Assert.True(File.Exists(outside));
        Assert.True(File.Exists(Paths.ShortcutFile(kept.Id)));
        Assert.Equal(0, lib.RecentlyDeleted.Purge(DateTimeOffset.Now));
        Assert.Equal(1, lib.RecentlyDeleted.Purge(DateTimeOffset.Now + RecentlyDeleted.KeepFor));
    }

    [Fact]
    public void Empty_LeavesFilesItDidNotCreate()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var a = Make("A"); var b = Make("B");
        lib.Save(a); lib.Save(b);
        lib.Delete(a.Id); lib.Delete(b.Id);
        var stranger = Path.Combine(Paths.DeletedFolder, "notes.json");
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
