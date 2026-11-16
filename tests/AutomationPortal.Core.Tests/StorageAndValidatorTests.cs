using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using Xunit;

namespace AutomationPortal.Core.Tests;

public class StorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "automationportal-tests", Guid.NewGuid().ToString("N")[..8]);
    private AutomationPortalPaths Paths => AutomationPortalPaths.ForRoot(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static Shortcut Make(string name, params string[] actionIds)
    {
        var b = new ShortcutBuilder(name);
        foreach (var id in actionIds) b.Add(id);
        return b.Build();
    }

    [Fact]
    public void Library_SaveReload_Folders_Automations_Snippets()
    {
        var reg = ActionRegistry.CreateWithBuiltIns();
        using (var lib = new JsonShortcutLibrary(Paths, reg, watch: false))
        {
            var folder = new ShortcutFolder { Name = "Work" };
            lib.SaveFolder(folder);
            var s = Make("One", "flow.nothing");
            s.FolderId = folder.Id;
            lib.Save(s);
            lib.SaveAutomation(new Automation { ShortcutId = s.Id, Trigger = new TriggerInstance { TriggerId = "time.daily", Parameters = { ["at"] = ParamValue.Time("07:30") } } });
            lib.SaveSnippet(new Snippet { Abbreviation = ";hi", Text = "Hello" });
        }
        using var again = new JsonShortcutLibrary(Paths, reg, watch: false);
        Assert.Single(again.All);
        Assert.Equal("Work", again.Folders[0].Name);
        Assert.Equal(again.Folders[0].Id, again.All[0].FolderId);
        Assert.Equal("time.daily", again.Automations[0].Trigger.TriggerId);
        Assert.Equal("07:30", again.Automations[0].Trigger.Parameters["at"].ToLiteralText());
        Assert.Equal(";hi", again.Snippets[0].Abbreviation);
        Assert.Empty(Directory.GetFiles(Paths.ShortcutsFolder, "*.tmp"));
    }

    [Fact]
    public void Library_NameMustBeUnique_CaseInsensitive()
    {
        var lib = new InMemoryShortcutLibrary();
        lib.Save(Make("Alpha"));
        var ex = Assert.Throws<LibraryException>(() => lib.Save(Make("ALPHA")));
        Assert.Contains("already exists", ex.Message);
        Assert.NotNull(lib.FindByName("alpha"));
    }

    [Fact]
    public void Library_RaisesChanged_AndDeleteFolderMovesShortcuts()
    {
        var lib = new InMemoryShortcutLibrary();
        var raised = 0;
        lib.Changed += (_, _) => raised++;
        var f = new ShortcutFolder { Name = "F" };
        lib.SaveFolder(f);
        var s = Make("A"); s.FolderId = f.Id;
        lib.Save(s);
        lib.DeleteFolder(f.Id);
        Assert.Null(lib.GetById(s.Id)!.FolderId);
        Assert.True(raised >= 3);
    }

    [Fact]
    public void Library_ReloadsWhenFilesChangeExternally()
    {
        using var lib = new JsonShortcutLibrary(Paths, null, watch: true);
        var changed = new ManualResetEventSlim();
        lib.Changed += (_, e) => { if (e.External) changed.Set(); };
        var other = Make("From elsewhere");
        File.WriteAllText(Paths.ShortcutFile(other.Id), AutomationPortalJson.Serialize(other));
        Assert.True(changed.Wait(TimeSpan.FromSeconds(10)));
        Assert.NotNull(lib.FindByName("From elsewhere"));
    }

    [Fact]
    public void ExportImport_NewId_RenameOnClash_AndWarnings()
    {
        var reg = ActionRegistry.CreateWithBuiltIns();
        var lib = new InMemoryShortcutLibrary(reg);
        var original = Make("Tidy", "flow.nothing", "bogus.action");
        original.Actions[0].Uid = "keepme";
        lib.Save(original);

        var json = lib.Export(original.Id);
        Assert.Contains("\"format\": \"autoportal.shortcut\"", json);

        var r1 = lib.ImportJson(json);
        Assert.NotEqual(original.Id, r1.Shortcut.Id);
        Assert.Equal("Tidy (2)", r1.Shortcut.Name);
        Assert.Equal("Tidy", r1.RenamedFrom);
        Assert.Equal("keepme", r1.Shortcut.Actions[0].Uid);
        Assert.Contains(r1.Warnings, w => w.Kind == ImportWarningKind.Unknown && w.ActionId == "bogus.action");
        Assert.Equal("Tidy (3)", lib.ImportJson(json).Shortcut.Name);
        Assert.Throws<LibraryException>(() => lib.ImportJson("{\"nope\":1}"));
        Assert.Throws<LibraryException>(() => lib.ImportJson("not json"));
    }

    [Fact]
    public void ImportAnswers_FillParameters()
    {
        var s = new ShortcutBuilder("Q").Also(b => b.Add(ActionInstance.Create("content.text").WithUid("u1"))).Build();
        s.ImportQuestions.Add(new ImportQuestion { ActionUid = "u1", ParameterKey = "text", Question = "Say what?" });
        s.ApplyImportAnswers(new ParamValue?[] { "answer" });
        Assert.Equal("answer", s.Actions[0].Parameters["text"].ToLiteralText());
    }

    [Fact]
    public void SettingsStore_Persists()
    {
        new JsonSettingsStore(Paths).Update(s => { s.Theme = ThemeSetting.Dark; s.QuickRunHotkey = "Ctrl+Q"; });
        var again = new JsonSettingsStore(Paths).Current;
        Assert.Equal(ThemeSetting.Dark, again.Theme);
        Assert.Equal("Ctrl+Q", again.QuickRunHotkey);
    }

    [Fact]
    public void SecretStore_EncryptsOnDisk()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new DpapiSecretStore(Paths);
        store.Set("ai.apiKey", "sk-secret-123");
        Assert.Equal("sk-secret-123", new DpapiSecretStore(Paths).Get("ai.apiKey"));
        Assert.DoesNotContain("sk-secret-123", File.ReadAllText(Paths.SecretsFile));
        store.Remove("ai.apiKey");
        Assert.Null(store.Get("ai.apiKey"));
    }

    [Fact]
    public void KeyValueStore_PersistsTypedValues()
    {
        var kv = new JsonKeyValueStore(Paths);
        kv.Set("n", ContentValue.Number(5));
        kv.Set("d", ContentValue.Date(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        var again = new JsonKeyValueStore(Paths);
        Assert.Equal(ContentKind.Number, again.Get("n")!.Kind);
        Assert.Equal(ContentKind.Date, again.Get("d")!.Kind);
    }

    [Fact]
    public void RunHistory_AppendsQueriesAndKeeps1000()
    {
        var h = new JsonlRunHistory(Paths);
        var id = Guid.NewGuid();
        var t0 = DateTimeOffset.Now.AddHours(-5);
        for (var i = 0; i < 1150; i++)
            h.Add(new RunRecord { RunId = i.ToString(), ShortcutId = i % 2 == 0 ? id : Guid.Empty, ShortcutName = "S" + i, Started = t0.AddSeconds(i), Status = RunStatus.Succeeded });
        var all = h.Query(new RunHistoryFilter { Limit = 5000 });
        Assert.Equal(1000, all.Count);
        Assert.Equal("1149", all[0].RunId);
        Assert.Equal("150", all[^1].RunId);
        Assert.All(h.Query(new RunHistoryFilter { ShortcutId = id, Limit = 5000 }), r => Assert.Equal(id, r.ShortcutId));
        h.Clear();
        Assert.Empty(h.Query());
    }

    [Fact]
    public async Task Reminders_Persist_AndFireWhenDue()
    {
        var store = new ReminderStore(Paths);
        var fired = new TaskCompletionSource<Reminder>();
        store.Due += (_, r) => fired.TrySetResult(r);
        store.Schedule(new Reminder { Title = "later", When = DateTimeOffset.Now.AddDays(1) });
        store.Schedule(new Reminder { Title = "soon", When = DateTimeOffset.Now.AddMilliseconds(300) });
        Assert.Equal(2, new ReminderStore(Paths).List().Count);
        store.Start();
        var r = await fired.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("soon", r.Title);
        Assert.Single(store.List());
        store.Dispose();
    }
}

public class ValidatorTests
{
    private readonly ActionRegistry _reg = ActionRegistry.CreateWithBuiltIns();

    [Fact]
    public void Reports_UnknownAction_RemovedAndLaterVariables_MissingRequired()
    {
        var b = new ShortcutBuilder("V");
        b.Add("nope.nothing");
        b.Add("content.text", ("text", ParamValue.Output("gone")));
        var laterUid = ActionInstance.NewUid();
        b.Add("content.text", ("text", ParamValue.Output(laterUid)));
        b.Add(ActionInstance.Create("content.text", ("text", "x")).WithUid(laterUid));
        b.Add("var.set");                                                  // name is required
        var codes = ShortcutValidator.Validate(b.Build(), _reg).Select(i => i.Code).ToList();
        Assert.Contains("unknownAction", codes);
        Assert.Contains("removedVariable", codes);
        Assert.Contains("laterVariable", codes);
        Assert.Contains("missingParameter", codes);
    }

    [Fact]
    public void Reports_BrokenGroups_AndRepeatItemOutsideRepeat()
    {
        var b = new ShortcutBuilder("V");
        b.Add("content.text", ("text", ParamValue.Var(VarRef.RepeatItem)));
        b.Add(ActionInstance.Create(FlowIds.If).WithFlow("g", FlowRole.End));
        var issues = ShortcutValidator.Validate(b.Build(), _reg);
        Assert.Contains(issues, i => i.Code == "brokenGroup");
        Assert.Contains(issues, i => i.Code == "outsideRepeat" && i.Severity == IssueSeverity.Warning);
    }

    [Fact]
    public void CleanShortcut_HasNoIssues()
    {
        var b = new ShortcutBuilder("Clean");
        var t = b.Add("content.text", ("text", "hi"));
        b.Repeat(2, s => s.Add("content.text", ("text", ParamValue.Text(VarRef.Output(t), VarRef.RepeatIndex))));
        Assert.Empty(ShortcutValidator.Validate(b.Build(), _reg));
    }
}

public class SnippetTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 9, 5, 0, TimeSpan.Zero);

    [Fact]
    public void Expand_Placeholders()
    {
        var (text, cursor) = SnippetExpander.Expand("On {date:yyyy-MM-dd} at {time:HH:mm}: {clipboard}|{cursor} bye {input:Name}", Now, "CLIP",
            new Dictionary<string, string> { ["Name"] = "Sam" });
        Assert.Equal("On 2026-10-02 at 09:05: CLIP| bye Sam", text);
        Assert.Equal(" bye Sam".Length, cursor);
    }

    [Fact]
    public void Expand_NoCursor_IsNull()
    {
        var (text, cursor) = SnippetExpander.Expand("plain {unknown}", Now, null);
        Assert.Equal("plain {unknown}", text);
        Assert.Null(cursor);
    }

    [Fact]
    public void FillInLabels_AreDistinct() =>
        Assert.Equal(new[] { "A", "B" }, SnippetExpander.FillInLabels("{input:A} {input:B} {input:A}"));

    [Theory]
    [InlineData(";addr", "my address", "my address")]
    [InlineData(";Addr", "my address", "My address")]
    [InlineData(";ADDR", "my address", "MY ADDRESS")]
    public void ApplyTypedCase(string typed, string text, string expected) =>
        Assert.Equal(expected, SnippetExpander.ApplyTypedCase(typed, text));
}
