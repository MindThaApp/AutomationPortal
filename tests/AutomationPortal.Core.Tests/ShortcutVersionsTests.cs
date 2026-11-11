using System.Text;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.Core.Tests;

public class ShortcutVersionsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "automationportal-tests", Guid.NewGuid().ToString("N")[..8]);
    private AutomationPortalPaths Paths => AutomationPortalPaths.ForRoot(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static Shortcut Make(string name, int steps = 1)
    {
        var b = new ShortcutBuilder(name);
        for (var i = 0; i < steps; i++) b.Add("flow.nothing");
        return b.Build();
    }

    private static byte[] Bytes(Shortcut s) => new UTF8Encoding(false).GetBytes(AutomationPortalJson.Serialize(s, indented: true));

    [Fact]
    public void Save_KeepsThePreviousVersion_AndSkipsIdenticalContent()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var s = Make("Tidy downloads");
        lib.Save(s);
        Assert.Empty(lib.Versions.List(s.Id));

        lib.Save(s);
        Assert.Empty(lib.Versions.List(s.Id));

        s.SortOrder = 7;
        lib.Save(s);
        Assert.Empty(lib.Versions.List(s.Id));

        s.Actions.Add(ActionInstance.Create("flow.nothing"));
        lib.Save(s);
        var one = Assert.Single(lib.Versions.List(s.Id));
        Assert.Equal(1, one.StepCount);
        Assert.Equal("Oldest kept version", one.Changes);

        lib.Save(s);
        Assert.Single(lib.Versions.List(s.Id));

        // Within the merge window of the version kept above: still only the state from before these edits.
        s.Name = "Tidy downloads folder";
        s.Actions.Add(ActionInstance.Create("flow.nothing"));
        lib.Save(s);
        var still = Assert.Single(lib.Versions.List(s.Id));
        Assert.Equal(1, still.StepCount);
        Assert.Equal("Tidy downloads", still.Name);

        Assert.True(Directory.Exists(Path.Combine(Paths.VersionsFolder, s.Id.ToString("D"))));
        Assert.False(Paths.VersionsFolder.StartsWith(Paths.LibraryFolder, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Describe_SummarisesStepsAndRename()
    {
        Assert.Equal("+2 steps, renamed", ShortcutVersions.Describe(Make("A", 1), Make("B", 3)));
        Assert.Equal("-1 step", ShortcutVersions.Describe(Make("A", 2), Make("A", 1)));
    }

    [Fact]
    public void Keep_SameTime_GetsAUniqueName()
    {
        var versions = new ShortcutVersions(Paths.VersionsFolder);
        var id = Guid.NewGuid();
        var t = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
        var a = Make("A"); a.Id = id;
        var b = Make("B"); b.Id = id;
        Assert.True(versions.Keep(id, Bytes(a), t));
        Assert.True(versions.Keep(id, Bytes(b), t));
        Assert.False(versions.Keep(id, Bytes(b), t));
        var keys = versions.List(id).Select(v => v.Key).ToList();
        Assert.Equal(new[] { "20260501T100000000Z (2).json", "20260501T100000000Z.json" }, keys);
        Assert.Equal("B", versions.Read(versions.List(id)[0]).Name);
    }

    [Fact]
    public void Purge_KeepsTheNewest30_InTwoSteps()
    {
        var versions = new ShortcutVersions(Paths.VersionsFolder);
        var id = Guid.NewGuid();
        var t0 = DateTimeOffset.Now.AddDays(-1);
        for (var i = 0; i < 35; i++) { var s = Make("v" + i); s.Id = id; Assert.True(versions.Keep(id, Bytes(s), t0.AddMinutes(i))); }
        var now = t0.AddHours(1);

        Assert.Equal(0, versions.Purge(now));
        Assert.Equal(35, versions.List(id).Count);
        Assert.Equal(0, versions.Purge(now.AddHours(23)));
        Assert.Equal(35, versions.List(id).Count);
        Assert.Equal(5, versions.Purge(now.AddHours(24)));

        var left = versions.List(id);
        Assert.Equal(30, left.Count);
        Assert.Equal("v34", left[0].Name);
        Assert.Equal("v5", left[^1].Name);
    }

    [Fact]
    public void Purge_RemovesVersionsOlderThan90Days_OnlyAfterASecondPurge()
    {
        var versions = new ShortcutVersions(Paths.VersionsFolder);
        var id = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
        var old = Make("old"); old.Id = id;
        var recent = Make("recent"); recent.Id = id;
        versions.Keep(id, Bytes(old), now.AddDays(-100));
        versions.Keep(id, Bytes(recent), now.AddDays(-1));

        Assert.Equal(0, versions.Purge(now));
        Assert.Equal(2, versions.List(id).Count);
        Assert.Equal(1, versions.Purge(now.AddHours(25)));
        Assert.Equal("recent", Assert.Single(versions.List(id)).Name);
    }

    [Fact]
    public void Purge_AClockThatJumpsAheadOnce_RemovesNothing()
    {
        var versions = new ShortcutVersions(Paths.VersionsFolder);
        var id = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
        var s = Make("A"); s.Id = id;
        versions.Keep(id, Bytes(s), now);

        Assert.Equal(0, versions.Purge(now.AddDays(200)));
        Assert.Equal(0, versions.Purge(now.AddDays(1)));
        Assert.Equal(0, versions.Purge(now.AddDays(2)));
        Assert.Single(versions.List(id));
    }

    [Fact]
    public void Purge_NeverTouchesOtherFiles()
    {
        var versions = new ShortcutVersions(Paths.VersionsFolder);
        var id = Guid.NewGuid();
        var dir = Path.Combine(Paths.VersionsFolder, id.ToString("D"));
        var other = Path.Combine(Paths.VersionsFolder, "other");
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(other);
        var keep = new[]
        {
            Path.Combine(dir, "20200101T000000000Z.txt"),
            Path.Combine(dir, "notes.json"),
            Path.Combine(other, "20200101T000000000Z.json"),
            Path.Combine(Paths.VersionsFolder, "20200101T000000000Z.json"),
        };
        foreach (var f in keep) File.WriteAllText(f, "{}");
        var now = DateTimeOffset.Now;

        versions.Purge(now);
        versions.Purge(now.AddDays(2));

        Assert.All(keep, f => Assert.True(File.Exists(f)));
    }

    [Fact]
    public void RestoreVersion_KeepsTheCurrentVersionFirst_AndKeepsCurrentSettings()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var s = Make("Backup photos", 1);
        lib.Save(s);
        lib.SaveAutomation(new Automation { ShortcutId = s.Id, Trigger = new TriggerInstance { TriggerId = "time.daily" } });
        s.Actions.Add(ActionInstance.Create("flow.nothing"));
        s.Hotkey = "Ctrl+Alt+K";
        s.Alias = "bp";
        lib.Save(s);
        var v1 = Assert.Single(lib.Versions.List(s.Id));

        var result = lib.RestoreVersion(v1);

        Assert.Equal(s.Id, result.Shortcut.Id);
        var now = lib.GetById(s.Id)!;
        Assert.Single(now.Actions);
        Assert.Equal("Ctrl+Alt+K", now.Hotkey);
        Assert.Equal("bp", now.Alias);
        Assert.Single(lib.Automations, a => a.ShortcutId == s.Id);
        var list = lib.Versions.List(s.Id);
        Assert.Equal(2, list.Count);
        Assert.Equal(2, list[0].StepCount);

        lib.RestoreVersion(list[0]);
        Assert.Equal(2, lib.GetById(s.Id)!.Actions.Count);
        Assert.Equal(3, lib.Versions.List(s.Id).Count);
    }

    [Fact]
    public void RestoreVersion_WithoutKeepingSettings_BringsBackTheOldOnes()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var s = Make("A");
        s.Hotkey = "Ctrl+Alt+J";
        lib.Save(s);
        s.Hotkey = null;
        s.Actions.Add(ActionInstance.Create("flow.nothing"));
        lib.Save(s);

        lib.RestoreVersion(Assert.Single(lib.Versions.List(s.Id)), keepCurrentSettings: false);

        Assert.Equal("Ctrl+Alt+J", lib.GetById(s.Id)!.Hotkey);
    }

    [Fact]
    public void OpenVersionAsCopy_AddsANewShortcut_AndLeavesTheCurrentOneAlone()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var s = Make("Rename files");
        s.Hotkey = "Ctrl+Alt+R";
        lib.Save(s);
        s.Actions.Add(ActionInstance.Create("flow.nothing"));
        lib.Save(s);
        var before = File.ReadAllBytes(Paths.ShortcutFile(s.Id));
        var versionsBefore = lib.Versions.List(s.Id).Count;

        var result = lib.OpenVersionAsCopy(Assert.Single(lib.Versions.List(s.Id)));

        Assert.NotEqual(s.Id, result.Shortcut.Id);
        Assert.Equal("Rename files (older version)", result.Shortcut.Name);
        Assert.Null(result.Shortcut.Hotkey);
        Assert.Single(result.Shortcut.Actions);
        Assert.Equal(2, lib.All.Count);
        Assert.Equal(before, File.ReadAllBytes(Paths.ShortcutFile(s.Id)));
        Assert.Equal(versionsBefore, lib.Versions.List(s.Id).Count);
    }

    [Fact]
    public void Save_StillWorks_WhenTheVersionCantBeWritten()
    {
        Directory.CreateDirectory(Paths.LocalRoot);
        File.WriteAllText(Paths.VersionsFolder, "in the way");
        var reports = new List<string>();
        void OnError(string message, Exception? _) { lock (reports) reports.Add(message); }
        StoreDiagnostics.Error += OnError;
        try
        {
            using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
            var s = Make("A");
            lib.Save(s);
            s.Actions.Add(ActionInstance.Create("flow.nothing"));
            lib.Save(s);

            Assert.Equal(2, lib.GetById(s.Id)!.Actions.Count);
            using var reread = new JsonShortcutLibrary(Paths, null, watch: false);
            Assert.Equal(2, reread.GetById(s.Id)!.Actions.Count);
            Assert.Empty(reread.Versions.List(s.Id));
            lock (reports) Assert.Contains(reports, m => m.Contains("previous version", StringComparison.Ordinal));
        }
        finally { StoreDiagnostics.Error -= OnError; }
    }

    [Fact]
    public void History_SurvivesDeleteAndRestore()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var s = Make("A");
        lib.Save(s);
        s.Actions.Add(ActionInstance.Create("flow.nothing"));
        lib.Save(s);
        Assert.Single(lib.Versions.List(s.Id));

        lib.Delete(s.Id);
        Assert.Single(lib.Versions.List(s.Id));
        lib.Versions.Purge(DateTimeOffset.Now);
        lib.Versions.Purge(DateTimeOffset.Now.AddDays(2));
        Assert.Single(lib.Versions.List(s.Id));

        var restored = lib.Restore(Assert.Single(lib.RecentlyDeleted.List()));
        Assert.False(restored.AsCopy);
        Assert.Single(lib.Versions.List(s.Id));

        lib.RestoreVersion(lib.Versions.List(s.Id)[0]);
        Assert.Single(lib.GetById(s.Id)!.Actions);
        Assert.Equal(2, lib.Versions.List(s.Id).Count);
    }

    [Fact]
    public void VersionsFolder_IsLocal_AndNeverRoams()
    {
        Assert.StartsWith(Paths.LocalRoot + Path.DirectorySeparatorChar, Paths.VersionsFolder, StringComparison.OrdinalIgnoreCase);
        Assert.False(Paths.VersionsFolder.StartsWith(Paths.RoamingRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ManyQuickSaves_KeepOnlyTheVersionFromBeforeTheSession()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var s = Make("Session", 1);
        lib.Save(s);
        for (var i = 0; i < 25; i++)
        {
            s.Actions.Add(ActionInstance.Create("flow.nothing"));
            s.Name = "Session " + i;
            lib.Save(s);
        }
        var only = Assert.Single(lib.Versions.List(s.Id));
        Assert.Equal(1, only.StepCount);
        Assert.Equal("Session", only.Name);
    }

    [Fact]
    public void Keep_Merge_KeepsAtMostOneVersionPerWindow()
    {
        var versions = new ShortcutVersions(Paths.VersionsFolder);
        var id = Guid.NewGuid();
        var t0 = new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);
        byte[] V(string name) { var s = Make(name); s.Id = id; return Bytes(s); }

        Assert.True(versions.Keep(id, V("a"), t0, merge: true));
        Assert.False(versions.Keep(id, V("b"), t0.AddMinutes(5), merge: true));
        Assert.False(versions.Keep(id, V("c"), t0.AddHours(-3), merge: true));
        Assert.False(versions.Keep(id, V("d"), t0.AddHours(-3).AddMinutes(9), merge: true));
        Assert.True(versions.Keep(id, V("e"), t0.AddMinutes(11), merge: true));
        Assert.True(versions.Keep(id, V("f"), t0.AddMinutes(12)));
        Assert.Equal(new[] { "f", "e", "a" }, versions.List(id).Select(v => v.Name));
    }

    [Fact]
    public void DuplicateCheck_CountsUnknownFields_AndIgnoresOnlyTheLibraryPlace()
    {
        var id = Guid.NewGuid();
        var s = Make("A"); s.Id = id;
        var plain = Bytes(s);
        var node = System.Text.Json.Nodes.JsonNode.Parse(plain)!.AsObject();
        node["fieldFromANewerVersion"] = 42;
        var withExtra = new UTF8Encoding(false).GetBytes(node.ToJsonString());
        Assert.NotEqual(ShortcutVersions.ContentHash(plain), ShortcutVersions.ContentHash(withExtra));

        var moved = s.Clone(); moved.SortOrder = 9; moved.Pinned = true; moved.FolderId = Guid.NewGuid(); moved.ModifiedAt = moved.ModifiedAt.AddDays(3);
        Assert.Equal(ShortcutVersions.ContentHash(plain), ShortcutVersions.ContentHash(Bytes(moved)));

        var versions = new ShortcutVersions(Paths.VersionsFolder);
        var t = new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);
        Assert.True(versions.Keep(id, withExtra, t, replacedBy: plain));
        Assert.True(versions.Keep(id, plain, t.AddMinutes(1)));
        Assert.Equal(2, versions.List(id).Count);
    }

    [Fact]
    public void DuplicateOfAnOldVersion_GetsAFreshCopy()
    {
        var versions = new ShortcutVersions(Paths.VersionsFolder);
        var id = Guid.NewGuid();
        var s = Make("A"); s.Id = id;
        var now = new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

        Assert.True(versions.Keep(id, Bytes(s), now.AddDays(-89.5)));
        Assert.True(versions.Keep(id, Bytes(s), now));
        Assert.False(versions.Keep(id, Bytes(s), now.AddDays(10)));
        Assert.Equal(2, versions.List(id).Count);

        versions.Purge(now.AddDays(1));
        Assert.Equal(1, versions.Purge(now.AddDays(2)));
        Assert.Equal(now, Assert.Single(versions.List(id)).SavedAt);
    }

    [Fact]
    public void Purge_SizeCap_RemovesTheOldestInTwoSteps()
    {
        var id = Guid.NewGuid();
        byte[] V(int i) { var s = Make("v" + i); s.Id = id; return Bytes(s); }
        var size = V(0).Length;
        var versions = new ShortcutVersions(Paths.VersionsFolder) { MaxTotalBytes = 3L * size + size / 2 };
        var t0 = DateTimeOffset.Now.AddDays(-2);
        for (var i = 0; i < 5; i++) Assert.True(versions.Keep(id, V(i), t0.AddMinutes(i)));
        var now = DateTimeOffset.Now;

        Assert.Equal(0, versions.Purge(now));
        Assert.Equal(5, versions.List(id).Count);
        Assert.Equal(2, versions.Purge(now.AddHours(25)));
        Assert.Equal(new[] { "v4", "v3", "v2" }, versions.List(id).Select(v => v.Name));
    }

    [Fact]
    public void RemovedForGood_FromRecentlyDeleted_PurgesThatShortcutsVersions()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var gone = Make("Gone");
        var stays = Make("Stays");
        foreach (var s in new[] { gone, stays })
        {
            lib.Save(s);
            s.Actions.Add(ActionInstance.Create("flow.nothing"));
            lib.Save(s);
        }
        lib.Delete(gone.Id);
        lib.Delete(stays.Id);
        var now = DateTimeOffset.Now;

        Assert.Equal(1, lib.RecentlyDeleted.Empty(lib.RecentlyDeleted.List().Where(d => d.ShortcutId == gone.Id)));
        Assert.Single(lib.Versions.List(gone.Id));
        Assert.Equal(0, lib.Versions.Purge(now));
        Assert.Single(lib.Versions.List(gone.Id));
        Assert.Equal(1, lib.Versions.Purge(now.AddHours(25)));

        Assert.Empty(lib.Versions.List(gone.Id));
        Assert.Single(lib.Versions.List(stays.Id));
        Assert.Equal(0, lib.Versions.Purge(now.AddHours(50)));
        Assert.Single(lib.Versions.List(stays.Id));
    }

    [Fact]
    public void RemovedForGood_ButBackInTheLibrary_KeepsItsVersions()
    {
        var versions = new ShortcutVersions(Paths.VersionsFolder);
        var id = Guid.NewGuid();
        var s = Make("A"); s.Id = id;
        versions.Keep(id, Bytes(s), DateTimeOffset.Now);
        versions.MarkRemoved(id, DateTimeOffset.Now);
        var back = new ShortcutVersions(Paths.VersionsFolder) { InUse = x => x == id };

        var now = DateTimeOffset.Now;
        back.Purge(now);
        Assert.Equal(0, back.Purge(now.AddHours(25)));
        Assert.Single(back.List(id));
    }

    [Fact]
    public void RestoreVersion_WhenTheCurrentVersionCantBeKept_ChangesNothing()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        var s = Make("A", 1);
        lib.Save(s);
        s.Actions.Add(ActionInstance.Create("flow.nothing"));
        lib.Save(s);
        var v = Assert.Single(lib.Versions.List(s.Id));
        var before = File.ReadAllBytes(Paths.ShortcutFile(s.Id));

        var dir = new DirectoryInfo(Path.Combine(Paths.VersionsFolder, s.Id.ToString("D")));
        var rule = new System.Security.AccessControl.FileSystemAccessRule(System.Security.Principal.WindowsIdentity.GetCurrent().User!,
            System.Security.AccessControl.FileSystemRights.CreateFiles, System.Security.AccessControl.AccessControlType.Deny);
        var acl = dir.GetAccessControl();
        acl.AddAccessRule(rule);
        dir.SetAccessControl(acl);
        try
        {
            var ex = Assert.Throws<LibraryException>(() => lib.RestoreVersion(v));
            Assert.Contains("nothing was restored", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            acl = dir.GetAccessControl();
            acl.RemoveAccessRule(rule);
            dir.SetAccessControl(acl);
        }
        Assert.Equal(before, File.ReadAllBytes(Paths.ShortcutFile(s.Id)));
        Assert.Equal(2, lib.GetById(s.Id)!.Actions.Count);
        Assert.Single(lib.Versions.List(s.Id));
    }

    private static void Junction(string link, string target)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{link}\" \"{target}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
        p!.WaitForExit();
        Assert.True(Directory.Exists(link));
    }

    [Fact]
    public void Purge_ThroughAJunction_RemovesNothing()
    {
        var id = Guid.NewGuid();
        var outside = Directory.CreateDirectory(Path.Combine(_root, "outside", id.ToString("D"))).FullName;
        var victims = Enumerable.Range(0, 35).Select(i => Path.Combine(outside, $"2020010{1 + i % 9}T0000{i:00}000Z.json")).ToList();
        foreach (var f in victims) File.WriteAllText(f, "{}");

        // The shortcut's own folder is a junction.
        Directory.CreateDirectory(Paths.VersionsFolder);
        var idLink = Path.Combine(Paths.VersionsFolder, id.ToString("D"));
        Junction(idLink, outside);
        // The whole versions folder is a junction.
        var rootLink = Path.Combine(_root, "versions-link");
        Junction(rootLink, Path.Combine(_root, "outside"));
        try
        {
            var versions = new ShortcutVersions(Paths.VersionsFolder);
            var linked = new ShortcutVersions(rootLink);
            var now = DateTimeOffset.Now;
            foreach (var t in new[] { now, now.AddDays(2), now.AddDays(4) })
            {
                versions.Purge(t);
                try { linked.Purge(t); } catch (LibraryException) { }
            }
            Assert.All(victims, f => Assert.True(File.Exists(f)));
            Assert.Throws<LibraryException>(() => linked.Purge(now));
        }
        finally
        {
            Directory.Delete(idLink);
            Directory.Delete(rootLink);
        }
    }
}
