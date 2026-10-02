using Flowmate.Core;
using Flowmate.Core.Testing;
using Flowmate.Triggers;
using Flowmate.Triggers.Platform;

namespace Flowmate.Triggers.Tests;

public class ReminderServiceTests
{
    private static readonly DateTimeOffset Due = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    private static (TestHost Host, ReminderService Service, FakeReminderService Store, Func<DateTimeOffset> Clock) Make(DateTimeOffset now)
    {
        var host = new TestHost();
        var store = new FakeReminderService();
        var current = now;
        var service = new ReminderService(host.Services, store, () => current);
        service.Start();
        return (host, service, store, () => current);
    }

    [Fact]
    public void An_on_time_reminder_shows_a_notification()
    {
        var (host, service, store, _) = Make(Due.AddSeconds(5));
        using var _h = host; using var _s = service;
        store.RaiseDue(new Reminder { When = Due, Title = "Call Sam", Body = "About the quote" });
        var shown = Assert.Single(host.Notifications.Shown);
        Assert.Equal("Call Sam", shown.Title);
        Assert.Equal("About the quote", shown.Body);
    }

    [Fact]
    public void A_reminder_that_came_due_while_the_PC_was_off_is_marked_missed()
    {
        var (host, service, store, _) = Make(Due.AddHours(5));
        using var _h = host; using var _s = service;
        store.RaiseDue(new Reminder { When = Due, Title = "Pay rent" });
        var shown = Assert.Single(host.Notifications.Shown);
        Assert.Equal("Pay rent (missed)", shown.Title);
        Assert.Contains("was due", shown.Body);
    }

    [Fact]
    public void A_missed_reminder_fires_exactly_once()
    {
        var (host, service, store, _) = Make(Due.AddHours(5));
        using var _h = host; using var _s = service;
        var fired = new List<(Reminder, bool)>();
        service.Fired += (_, e) => fired.Add(e);
        store.RaiseDue(new Reminder { When = Due, Title = "x" });
        Assert.Single(fired);
        Assert.True(fired[0].Item2);
    }

    [Fact]
    public async Task A_reminder_can_run_a_shortcut()
    {
        var (host, service, store, _) = Make(Due);
        using var _h = host; using var _s = service;
        var shortcut = new Shortcut { Name = "Stretch" };
        shortcut.Actions.Add(ActionInstance.Create("content.text", ("text", "Stand up")));
        host.Library.Save(shortcut);

        store.RaiseDue(new Reminder { When = Due, Title = "Stretch time", ShortcutIdToRun = shortcut.Id });

        for (var i = 0; i < 100 && host.History.Records.Count == 0; i++) await Task.Delay(20);
        var record = Assert.Single(host.History.Records);
        Assert.Equal(RunSource.Reminder, record.Source);
        Assert.Equal(shortcut.Id, record.ShortcutId);
    }

    [Fact]
    public void A_reminder_whose_shortcut_was_deleted_still_notifies_and_says_so()
    {
        var (host, service, store, _) = Make(Due);
        using var _h = host; using var _s = service;
        store.RaiseDue(new Reminder { When = Due, Title = "Stretch", ShortcutIdToRun = Guid.NewGuid() });
        var shown = Assert.Single(host.Notifications.Shown);
        Assert.Contains("was deleted", shown.Body);
        Assert.Empty(host.History.Records);
    }

    [Fact]
    public void Schedule_cancel_and_list_go_to_the_store()
    {
        var (host, service, store, _) = Make(Due);
        using var _h = host; using var _s = service;
        var r = service.Schedule(new Reminder { When = Due.AddHours(1), Title = "Later" });
        Assert.Single(service.List());
        Assert.True(service.Cancel(r.Id));
        Assert.Empty(service.List());
    }

    [Fact]
    public void Reminders_persist_through_Cores_file_store_and_overdue_ones_fire_on_start()
    {
        using var host = new TestHost();
        var overdue = new Reminder { When = DateTimeOffset.Now.AddHours(-3), Title = "While you were away" };
        var first = new ReminderStore(host.Services.Paths);
        first.Schedule(overdue);
        first.Dispose();

        var second = new ReminderStore(host.Services.Paths);
        using var service = new ReminderService(host.Services, second);
        Assert.Single(service.List());
        service.Start();
        SpinWait.SpinUntil(() => host.Notifications.Shown.Count > 0, TimeSpan.FromSeconds(5));
        var shown = Assert.Single(host.Notifications.Shown);
        Assert.Equal("While you were away (missed)", shown.Title);
        Assert.Empty(service.List());
    }
}

[Collection("Hotkeys")]
public class SystemServiceSmokeTests
{
    // These touch Windows for a moment: a hidden window, a hotkey that is registered and released at once,
    // and a keyboard hook that is installed and removed again. Nothing is typed or clicked.
    private const string RareHotkey = "Ctrl+Alt+Shift+F23";

    [Fact]
    public void The_event_window_starts_runs_work_on_its_thread_and_stops_cleanly()
    {
        using var hub = new SystemEventHub();
        hub.Start();
        Assert.True(hub.IsRunning);
        var thread = hub.Invoke(() => Environment.CurrentManagedThreadId);
        Assert.NotEqual(Environment.CurrentManagedThreadId, thread);
        hub.Dispose();
        Assert.False(hub.IsRunning);
    }

    [Fact]
    public void A_hotkey_can_be_registered_reported_and_released()
    {
        using var hub = new SystemEventHub();
        hub.Start();
        using var hotkeys = new HotkeyService(hub);

        var ok = hotkeys.Register("test-a", RareHotkey, () => { }, "test A");
        Assert.True(ok.Success, ok.Reason);
        Assert.True(hotkeys.IsRegistered("test-a"));
        Assert.Contains(hotkeys.Registrations, r => r.OwnerId == "test-a" && r.Hotkey == "Ctrl+Alt+Shift+F23");

        var clash = hotkeys.Register("test-b", RareHotkey, () => { }, "test B");
        Assert.False(clash.Success);
        Assert.Contains("already used by test A", clash.Reason);

        Assert.True(hotkeys.Unregister("test-a"));
        Assert.False(hotkeys.IsRegistered("test-a"));
        Assert.True(hotkeys.Register("test-b", RareHotkey, () => { }, "test B").Success);
    }

    [Fact]
    public void A_shortcut_another_app_already_owns_is_reported_in_plain_English()
    {
        using var hubOne = new SystemEventHub();
        using var hubTwo = new SystemEventHub();
        hubOne.Start(); hubTwo.Start();
        using var one = new HotkeyService(hubOne);
        using var two = new HotkeyService(hubTwo);

        Assert.True(one.Register("a", RareHotkey, () => { }).Success);
        var result = two.Register("b", RareHotkey, () => { });
        Assert.False(result.Success);
        Assert.Equal("Another app already uses Ctrl+Alt+Shift+F23. Choose a different keyboard shortcut.", result.Reason);
    }

    [Fact]
    public void Bad_or_unsafe_shortcuts_are_refused_with_a_reason()
    {
        using var hub = new SystemEventHub();
        hub.Start();
        using var hotkeys = new HotkeyService(hub);
        Assert.Contains("isn't a keyboard shortcut", hotkeys.Register("x", "banana", () => { }).Reason);
        Assert.Contains("stop you typing", hotkeys.Register("x", "M", () => { }).Reason);
        Assert.False(hotkeys.IsRegistered("x"));
    }

    [Fact]
    public void Registering_again_under_the_same_owner_replaces_the_old_shortcut()
    {
        using var hub = new SystemEventHub();
        hub.Start();
        using var hotkeys = new HotkeyService(hub);
        Assert.True(hotkeys.Register("o", RareHotkey, () => { }).Success);
        Assert.True(hotkeys.Register("o", "Ctrl+Alt+Shift+F22", () => { }).Success);
        Assert.Single(hotkeys.Registrations);
        Assert.True(hotkeys.Register("other", RareHotkey, () => { }).Success);
    }

    [Fact]
    public void Shortcuts_limited_to_different_apps_can_share_a_key_combination()
    {
        using var hub = new SystemEventHub();
        hub.Start();
        using var hotkeys = new HotkeyService(hub);
        Assert.True(hotkeys.Register("excel", RareHotkey, () => { }, null, ParamValue.App("excel", "Excel") as AppValue).Success);
        Assert.True(hotkeys.Register("word", RareHotkey, () => { }, null, ParamValue.App("winword", "Word") as AppValue).Success);
        Assert.False(hotkeys.Register("everywhere", RareHotkey, () => { }, "the global one").Success);
        Assert.False(hotkeys.Register("excel2", RareHotkey, () => { }, null, ParamValue.App("excel", "Excel") as AppValue).Success);
    }

    [Fact]
    public void Disposing_the_service_releases_every_shortcut()
    {
        using var hub = new SystemEventHub();
        hub.Start();
        var first = new HotkeyService(hub);
        Assert.True(first.Register("o", RareHotkey, () => { }).Success);
        first.Dispose();
        using var second = new HotkeyService(hub);
        Assert.True(second.Register("o", RareHotkey, () => { }).Success);
    }

    [Fact]
    public void The_typing_hook_only_exists_while_a_phrase_is_registered()
    {
        using var phrases = new TypedPhraseService();
        Assert.False(phrases.IsHookInstalled);
        var reg = phrases.Register(";smoke-test", false, _ => Task.CompletedTask);
        Assert.True(phrases.IsHookInstalled);
        reg.Dispose();
        Assert.False(phrases.IsHookInstalled);
    }

    [Fact]
    public void The_recorder_starts_and_stops_and_returns_its_steps()
    {
        using var recorder = new MacroRecorder();
        recorder.Start();
        Assert.True(recorder.IsRecording);
        var steps = recorder.Stop();
        Assert.False(recorder.IsRecording);
        Assert.NotNull(steps);
        Assert.Same(steps, recorder.Stop());
    }

    [Fact]
    public void The_recorder_stop_shortcut_is_configurable_and_validated()
    {
        using var recorder = new MacroRecorder();
        Assert.Equal("Ctrl+Alt+Esc", recorder.StopHotkey);
        recorder.StopHotkey = "ctrl+shift+f12";
        Assert.Equal("Ctrl+Shift+F12", recorder.StopHotkey);
        Assert.Throws<ArgumentException>(() => recorder.StopHotkey = "nonsense");
    }
}

[Collection("Hotkeys")]
public class ShortcutBindingTests
{
    private static Shortcut Make(TestHost host, string name, string? hotkey)
    {
        var s = new Shortcut { Name = name, Hotkey = hotkey };
        s.Actions.Add(ActionInstance.Create("content.text", ("text", "ran")));
        host.Library.Save(s);
        return s;
    }

    [Fact]
    public void Shortcut_keyboard_shortcuts_follow_the_library()
    {
        using var host = new TestHost();
        using var hub = new SystemEventHub();
        hub.Start();
        using var hotkeys = new HotkeyService(hub);
        using var binder = new ShortcutHotkeyBinder(host.Services, hotkeys);

        var a = Make(host, "A", "Ctrl+Alt+Shift+F23");
        binder.Sync();
        Assert.True(hotkeys.IsRegistered(ShortcutHotkeyBinder.OwnerId(a.Id)));

        var changed = a.Clone();
        changed.Hotkey = "Ctrl+Alt+Shift+F22";
        host.Library.Save(changed);
        binder.Sync();
        Assert.Equal("Ctrl+Alt+Shift+F22", hotkeys.Registrations.Single().Hotkey);

        host.Library.Delete(a.Id);
        binder.Sync();
        Assert.Empty(hotkeys.Registrations);
    }

    [Fact]
    public void A_clash_is_reported_for_the_shortcut_and_clears_when_fixed()
    {
        using var host = new TestHost();
        using var hub = new SystemEventHub();
        hub.Start();
        using var hotkeys = new HotkeyService(hub);
        using var binder = new ShortcutHotkeyBinder(host.Services, hotkeys);
        var raised = 0;
        binder.ConflictsChanged += (_, _) => raised++;

        var first = Make(host, "First", "Ctrl+Alt+Shift+F23");
        var second = Make(host, "Second", "Ctrl+Alt+Shift+F23");
        binder.Sync();

        Assert.Null(binder.ConflictFor(first.Id));
        var reason = binder.ConflictFor(second.Id);
        Assert.NotNull(reason);
        Assert.Contains("already used by the shortcut \"First\"", reason);
        Assert.True(raised >= 1);

        var fixedUp = second.Clone();
        fixedUp.Hotkey = "Ctrl+Alt+Shift+F22";
        host.Library.Save(fixedUp);
        binder.Sync();
        Assert.Empty(binder.Conflicts);
        Assert.Equal(2, hotkeys.Registrations.Count);
    }

    [Fact]
    public void An_invalid_keyboard_shortcut_on_a_shortcut_is_reported()
    {
        using var host = new TestHost();
        using var hub = new SystemEventHub();
        hub.Start();
        using var hotkeys = new HotkeyService(hub);
        using var binder = new ShortcutHotkeyBinder(host.Services, hotkeys);
        var s = Make(host, "Broken", "Ctrl+Banana");
        binder.Sync();
        Assert.Contains("isn't a keyboard shortcut", binder.ConflictFor(s.Id));
    }

    [Fact]
    public void Disposing_the_binder_releases_its_shortcuts()
    {
        using var host = new TestHost();
        using var hub = new SystemEventHub();
        hub.Start();
        using var hotkeys = new HotkeyService(hub);
        var binder = new ShortcutHotkeyBinder(host.Services, hotkeys);
        Make(host, "A", "Ctrl+Alt+Shift+F23");
        binder.Sync();
        Assert.Single(hotkeys.Registrations);
        binder.Dispose();
        Assert.Empty(hotkeys.Registrations);
    }

    [Fact]
    public async Task Snippets_register_the_typing_hook_only_while_some_snippet_is_enabled()
    {
        using var host = new TestHost();
        using var phrases = new TypedPhraseService();
        using var expansion = new SnippetExpansionService(host.Services, phrases);
        var snippet = new Snippet { Abbreviation = ";smoke", Text = "x", Enabled = true };
        host.Library.SaveSnippet(snippet);
        expansion.Start();
        Assert.True(phrases.IsHookInstalled);

        var off = snippet.Clone();
        off.Enabled = false;
        host.Library.SaveSnippet(off);
        for (var i = 0; i < 100 && phrases.IsHookInstalled; i++) await Task.Delay(20);
        Assert.False(phrases.IsHookInstalled);
    }
}

[Collection("Hotkeys")]
public class TriggerHostTests
{
    [Fact]
    public void The_host_starts_and_stops_everything_cleanly_with_no_automations()
    {
        using var host = new TestHost();
        var triggers = new TriggerRegistry();
        var conditions = new ConditionRegistry();
        using var th = new TriggerHost(host.Services, triggers, conditions,
            new TriggerHostOptions { ReminderStore = new FakeReminderService() });

        Assert.NotNull(triggers.Get("time.daily"));
        th.Start();
        th.Start();
        Assert.False(th.Recorder.IsRecording);
        Assert.False(th.TypedPhrases.IsHookInstalled);
        var ok = th.Hotkeys.Register("app:test", "Ctrl+Alt+Shift+F23", () => { });
        Assert.True(ok.Success, ok.Reason);
        th.Dispose();
        th.Dispose();
    }

    [Fact]
    public void A_key_shortcut_automation_registers_through_the_host_and_reports_clashes()
    {
        using var host = new TestHost();
        var shortcut = new Shortcut { Name = "S" };
        shortcut.Actions.Add(ActionInstance.Create("content.text", ("text", "x")));
        host.Library.Save(shortcut);

        Automation Make(string keys) => new()
        {
            ShortcutId = shortcut.Id,
            Trigger = new TriggerInstance { TriggerId = "key.shortcut", Parameters = { ["hotkey"] = ParamValue.Hotkey(keys) } },
        };
        var one = Make("Ctrl+Alt+Shift+F23");
        var two = Make("Ctrl+Alt+Shift+F23");
        host.Library.SaveAutomation(one);
        host.Library.SaveAutomation(two);

        using var th = new TriggerHost(host.Services, new TriggerRegistry(), new ConditionRegistry(),
            new TriggerHostOptions { ReminderStore = new FakeReminderService() });
        th.Start();

        var statuses = new[] { th.Automations.GetStatus(one.Id), th.Automations.GetStatus(two.Id) };
        Assert.Single(statuses, s => s.IsRunning);
        var failed = Assert.Single(statuses, s => !s.IsRunning);
        Assert.Contains("already used by an automation", failed.Error);

        th.Dispose();
        using var hub = new SystemEventHub();
        hub.Start();
        using var after = new HotkeyService(hub);
        Assert.True(after.Register("check", "Ctrl+Alt+Shift+F23", () => { }).Success);
    }
}
