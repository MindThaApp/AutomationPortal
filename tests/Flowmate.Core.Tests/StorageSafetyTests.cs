using Flowmate.Core;
using Flowmate.Core.Testing;
using Xunit;

namespace Flowmate.Core.Tests;

public class StorageSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "flowmate-tests", Guid.NewGuid().ToString("N")[..8]);
    private FlowmatePaths Paths => FlowmatePaths.ForRoot(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static Shortcut Make(string name) { var b = new ShortcutBuilder(name); b.Add("flow.nothing"); return b.Build(); }
    private static Automation Auto(Guid sid) => new() { ShortcutId = sid, Trigger = new TriggerInstance { TriggerId = "time.daily" } };

    [Fact]
    public void LockedAutomationsFile_IsNotTreatedAsEmpty_AndIsNeverOverwritten()
    {
        var s = Make("A");
        using (var lib = new JsonShortcutLibrary(Paths, null, watch: false))
        {
            lib.Save(s);
            lib.SaveAutomation(Auto(s.Id)); lib.SaveAutomation(Auto(s.Id));
        }
        using var l2 = new JsonShortcutLibrary(Paths, null, watch: false);
        Assert.Equal(2, l2.Automations.Count);
        using (new FileStream(Paths.AutomationsFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            l2.Reload();
            Assert.Equal(2, l2.Automations.Count);
            Assert.NotEmpty(l2.LoadErrors);
            Assert.Throws<LibraryException>(() => l2.SaveAutomation(Auto(s.Id)));
            Assert.Equal(2, l2.Automations.Count);
        }
        l2.Reload();
        l2.SaveAutomation(Auto(s.Id));
        Assert.Equal(3, l2.Automations.Count);
        Assert.True(File.Exists(Paths.AutomationsFile + ".bak"));
    }

    [Fact]
    public void LockedShortcutFile_KeepsPreviousVersionOnReload()
    {
        var s = Make("Keep");
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        lib.Save(s);
        using (new FileStream(Paths.ShortcutFile(s.Id), FileMode.Open, FileAccess.Read, FileShare.None))
            lib.Reload();
        Assert.NotNull(lib.FindByName("Keep"));
    }

    [Fact]
    public void MissingShortcutsFolder_DoesNotThrowOnReload()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        lib.Save(Make("X"));
        Directory.Delete(Paths.ShortcutsFolder, true);
        lib.Reload();
        Assert.Empty(lib.All);
    }

    [Fact]
    public void CorruptListAtStartup_IsQuarantinedBeforeAnySave()
    {
        Directory.CreateDirectory(Paths.LibraryFolder);
        File.WriteAllText(Paths.AutomationsFile, "{ not json");
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        Assert.Empty(lib.Automations);
        Assert.NotEmpty(Directory.GetFiles(Paths.LibraryFolder, "automations.json.corrupt-*"));
    }

    [Fact]
    public void DuplicateIds_AreReported_AndDeleteDoesNotResurrect()
    {
        var s = Make("Dup");
        using var lib = new JsonShortcutLibrary(Paths, null, watch: false);
        lib.Save(s);
        File.Copy(Paths.ShortcutFile(s.Id), Path.Combine(Paths.ShortcutsFolder, s.Id + "-PC2.json"));
        lib.Reload();
        Assert.Single(lib.All);
        Assert.Contains(lib.LoadErrors, e => e.Contains("same id"));
        lib.Delete(s.Id);
        lib.Reload();
        Assert.Empty(lib.All);
    }

    [Fact]
    public void ExternalEditRightAfterOurOwnWrite_IsStillLoaded()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: true);
        var s = Make("Mine");
        lib.Save(s);
        var changed = new ManualResetEventSlim();
        lib.Changed += (_, e) => { if (e.External) changed.Set(); };
        var other = Make("Theirs");
        File.WriteAllText(Paths.ShortcutFile(other.Id), FlowmateJson.Serialize(other));
        Assert.True(changed.Wait(TimeSpan.FromSeconds(10)));
        Assert.NotNull(lib.FindByName("Theirs"));
    }

    [Fact]
    public void SecretStore_Locked_DoesNotWipeOtherKeys()
    {
        var store = new DpapiSecretStore(Paths);
        store.Set("a", "1"); store.Set("b", "2");
        using (new FileStream(Paths.SecretsFile, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Throws<LibraryException>(() => store.Set("c", "3"));
        Assert.Equal("1", store.Get("a"));
        Assert.Equal("2", store.Get("b"));
    }

    [Fact]
    public void SettingsStore_Locked_UsesDefaultsButNeverOverwrites()
    {
        Directory.CreateDirectory(Paths.RoamingRoot);
        var original = new JsonSettingsStore(Paths);
        original.Update(s => s.LibraryFolder = @"D:\MyLibrary");
        var text = File.ReadAllText(Paths.SettingsFile);
        JsonSettingsStore locked;
        using (new FileStream(Paths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            locked = new JsonSettingsStore(Paths);
            Assert.NotNull(locked.LoadError);
            locked.Update(s => s.QuickRunHotkey = "Ctrl+Shift+K");
        }
        Assert.Equal(text, File.ReadAllText(Paths.SettingsFile));
        locked.Update(s => s.QuickRunHotkey = "Ctrl+Shift+K");
        Assert.Equal(@"D:\MyLibrary", locked.Current.LibraryFolder);
    }

    [Fact]
    public void SavedValues_Locked_RefusesToWrite()
    {
        var kv = new JsonKeyValueStore(Paths);
        kv.Set("a", ContentValue.Text("1"));
        var kv2Locked = default(JsonKeyValueStore);
        using (new FileStream(Paths.SavedValuesFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            kv2Locked = new JsonKeyValueStore(Paths);
            Assert.Throws<LibraryException>(() => kv2Locked.Set("b", ContentValue.Text("2")));
        }
        kv2Locked.Set("b", ContentValue.Text("2"));
        Assert.NotNull(kv2Locked.Get("a"));
    }

    [Fact]
    public void ReminderStore_SaveFailureOnTimerThread_DoesNotThrowAndRearms()
    {
        var store = new ReminderStore(Paths);
        var fired = new CountdownEvent(2);
        store.Due += (_, _) => fired.Signal();
        store.Start();
        var soon = DateTimeOffset.Now.AddMilliseconds(300);
        store.Schedule(new Reminder { Title = "one", When = soon });
        store.Schedule(new Reminder { Title = "two", When = soon.AddSeconds(2) });
        using (new FileStream(Paths.RemindersFile, FileMode.Open, FileAccess.Read, FileShare.None))
            Thread.Sleep(900);
        Assert.True(fired.Wait(TimeSpan.FromSeconds(10)));
        store.Dispose();
    }
}
