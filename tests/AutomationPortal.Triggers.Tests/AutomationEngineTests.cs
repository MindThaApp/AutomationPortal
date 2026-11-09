using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using AutomationPortal.Triggers;

namespace AutomationPortal.Triggers.Tests;

public class AutomationEngineTests
{
    [Fact]
    public async Task Fire_runs_the_shortcut_with_the_trigger_output_as_input()
    {
        using var f = new EngineFixture();
        var a = f.Add();
        f.Engine.Reload();

        var outcome = await f.FireAsync(a, "from trigger");

        Assert.True(outcome.Ran);
        Assert.Equal("from trigger", outcome.Result!.Output.AsText());
        Assert.Equal(RunSource.Automation, outcome.Result.Source);
        Assert.Contains(f.Host.History.Records, r => r.Source == RunSource.Automation && r.ShortcutId == f.Shortcut.Id);
    }

    [Fact]
    public async Task Fire_updates_LastRun_and_raises_AutomationRan()
    {
        using var f = new EngineFixture();
        var a = f.Add();
        f.Engine.Reload();
        AutomationRanEventArgs? seen = null;
        f.Engine.AutomationRan += (_, e) => seen = e;

        await f.FireAsync(a, description: "Spotify opened");

        Assert.Equal(f.Now, f.Latest(a).LastRun);
        Assert.NotNull(seen);
        Assert.False(seen!.IsTest);
        Assert.Equal("Spotify opened", seen.Description);
        Assert.Equal(f.Shortcut.Id, seen.ShortcutId);
    }

    [Fact]
    public void Enabled_automation_starts_its_provider_and_disabled_one_does_not()
    {
        using var f = new EngineFixture();
        f.Add();
        f.Add(a => a.Enabled = false);
        f.Engine.Reload();
        Assert.Equal(1, f.Trigger.Started);
    }

    [Fact]
    public void Removing_an_automation_stops_its_provider()
    {
        using var f = new EngineFixture();
        var a = f.Add();
        f.Engine.Reload();
        f.Host.Library.DeleteAutomation(a.Id);
        f.Engine.Reload();
        Assert.Equal(1, f.Trigger.Stopped);
    }

    [Fact]
    public void Unrelated_changes_do_not_restart_a_provider_but_trigger_changes_do()
    {
        using var f = new EngineFixture();
        var a = f.Add();
        f.Engine.Reload();

        var copy = f.Latest(a).Clone();
        copy.CooldownSeconds = 30;
        copy.LastRun = f.Now;
        f.Host.Library.SaveAutomation(copy);
        f.Engine.Reload();
        Assert.Equal(1, f.Trigger.Started);
        Assert.Equal(0, f.Trigger.Stopped);

        copy.Trigger.Parameters["x"] = ParamValue.Text("changed");
        f.Host.Library.SaveAutomation(copy);
        f.Engine.Reload();
        Assert.Equal(2, f.Trigger.Started);
        Assert.Equal(1, f.Trigger.Stopped);
    }

    [Fact]
    public async Task Pausing_stops_providers_and_blocks_fires_and_resuming_restarts_them()
    {
        using var f = new EngineFixture();
        var a = f.Add();
        f.Engine.Reload();

        f.Host.Settings.Update(s => s.AutomationsPaused = true);
        f.Engine.Reload();
        Assert.Equal(1, f.Trigger.Stopped);
        var blocked = await f.FireAsync(a);
        Assert.False(blocked.Ran);
        Assert.Empty(f.Host.History.Records);

        f.Host.Settings.Update(s => s.AutomationsPaused = false);
        f.Engine.Reload();
        Assert.Equal(2, f.Trigger.Started);
        Assert.True((await f.FireAsync(a)).Ran);
    }

    [Fact]
    public async Task Cooldown_skips_runs_until_it_has_passed()
    {
        using var f = new EngineFixture();
        var a = f.Add(x => x.CooldownSeconds = 300);
        f.Engine.Reload();

        Assert.True((await f.FireAsync(a)).Ran);
        f.Now += TimeSpan.FromSeconds(100);
        var second = await f.FireAsync(a);
        Assert.False(second.Ran);
        Assert.Contains("5 minutes", second.SkipReason);

        f.Now += TimeSpan.FromSeconds(250);
        Assert.True((await f.FireAsync(a)).Ran);
    }

    [Fact]
    public async Task Ask_first_runs_only_when_the_user_says_yes()
    {
        using var f = new EngineFixture();
        var a = f.Add(x => x.RunMode = RunMode.AskFirst);
        f.Engine.Reload();

        f.Host.Notifications.AskToRunAnswer = false;
        var no = await f.FireAsync(a);
        Assert.False(no.Ran);
        Assert.Empty(f.Host.History.Records);
        Assert.Null(f.Latest(a).LastRun);

        f.Host.Notifications.AskToRunAnswer = true;
        Assert.True((await f.FireAsync(a)).Ran);
        Assert.Single(f.Host.History.Records);
    }

    [Fact]
    public async Task Conditions_must_all_be_met()
    {
        using var f = new EngineFixture();
        var a = f.Add(x => x.Conditions.Add(new ConditionInstance { ConditionId = "test.cond" }));
        f.Engine.Reload();

        f.Condition.Result = false;
        Assert.False((await f.FireAsync(a)).Ran);
        f.Condition.Result = true;
        Assert.True((await f.FireAsync(a)).Ran);
    }

    [Fact]
    public async Task A_condition_that_throws_or_is_unknown_means_do_not_run()
    {
        using var f = new EngineFixture();
        var throwing = f.Add(x => x.Conditions.Add(new ConditionInstance { ConditionId = "test.cond" }));
        var unknown = f.Add(x => x.Conditions.Add(new ConditionInstance { ConditionId = "nope" }));
        f.Engine.Reload();

        f.Condition.Throw = true;
        Assert.False((await f.FireAsync(throwing)).Ran);
        Assert.False((await f.FireAsync(unknown)).Ran);
    }

    [Fact]
    public async Task Notify_when_run_shows_a_notification_and_failures_always_do()
    {
        using var f = new EngineFixture();
        var quiet = f.Add();
        var loud = f.Add(x => x.NotifyWhenRun = true);
        f.Engine.Reload();

        await f.FireAsync(quiet, description: "Spotify opened");
        Assert.Empty(f.Host.Notifications.Shown);

        await f.FireAsync(loud, description: "Spotify opened");
        var shown = Assert.Single(f.Host.Notifications.Shown);
        Assert.Equal("Echo", shown.Title);
        Assert.Contains("spotify opened", shown.Body);

        f.Host.Notifications.Shown.Clear();
        var broken = new Shortcut { Name = "Broken" };
        broken.Actions.Add(ActionInstance.Create("nonexistent.action"));
        f.Host.Library.Save(broken);
        var b = f.Add(x => x.ShortcutId = broken.Id);
        f.Engine.Reload();
        var outcome = await f.FireAsync(b);
        Assert.Equal(RunStatus.Failed, outcome.Result!.Status);
        Assert.Contains(f.Host.Notifications.Shown, n => n.Title.Contains("Broken"));
    }

    [Fact]
    public void A_provider_that_cannot_start_is_reported_and_does_not_stop_others()
    {
        using var f = new EngineFixture();
        var bad = new FakeTrigger("test.bad") { ThrowOnStart = new InvalidOperationException("Another app already uses Ctrl+Alt+M.") };
        f.Triggers.Register(bad);
        var good = f.Add();
        var broken = f.Add(triggerId: "test.bad");

        f.Engine.Reload();

        Assert.Equal("Another app already uses Ctrl+Alt+M.", f.Engine.GetStatus(broken.Id).Error);
        Assert.False(f.Engine.GetStatus(broken.Id).IsRunning);
        Assert.True(f.Engine.GetStatus(good.Id).IsRunning);
        Assert.Null(f.Engine.GetStatus(good.Id).Error);
        Assert.Equal(1, f.Trigger.Started);
    }

    [Fact]
    public void An_unexpected_provider_exception_gets_a_friendly_message()
    {
        using var f = new EngineFixture();
        f.Triggers.Register(new FakeTrigger("test.crash") { ThrowOnStart = new NullReferenceException("oops") });
        var a = f.Add(triggerId: "test.crash");
        f.Engine.Reload();
        Assert.StartsWith("Automation Portal couldn't start this trigger", f.Engine.GetStatus(a.Id).Error);
    }

    [Fact]
    public void Unknown_trigger_and_deleted_shortcut_are_shown_as_errors()
    {
        using var f = new EngineFixture();
        var unknown = f.Add(triggerId: "gone.trigger");
        var orphan = f.Add(x => x.ShortcutId = Guid.NewGuid());
        f.Engine.Reload();
        Assert.Contains("isn't available", f.Engine.GetStatus(unknown.Id).Error);
        Assert.Contains("deleted", f.Engine.GetStatus(orphan.Id).Error);
        Assert.Equal(0, f.Trigger.Started);
    }

    [Fact]
    public void Provider_problems_show_up_and_clear()
    {
        using var f = new EngineFixture();
        var a = f.Add();
        f.Engine.Reload();
        var sink = f.Trigger.Sinks.Single();

        sink.ReportProblem("Couldn't read that feed.");
        Assert.Equal("Couldn't read that feed.", f.Engine.GetStatus(a.Id).Error);
        sink.ReportProblem(null);
        Assert.Null(f.Engine.GetStatus(a.Id).Error);
    }

    [Fact]
    public async Task The_sink_fire_path_runs_the_shortcut()
    {
        using var f = new EngineFixture();
        f.Add();
        f.Engine.Reload();
        f.Trigger.Sinks.Single().Fire(ContentValue.Text("via sink"), "test");

        for (var i = 0; i < 100 && f.Host.History.Records.Count == 0; i++) await Task.Delay(20);
        Assert.Single(f.Host.History.Records);
    }

    [Fact]
    public async Task One_shot_triggers_switch_the_automation_off_after_running()
    {
        using var f = new EngineFixture();
        var a = f.Add(triggerId: "test.once");
        f.Engine.Reload();
        Assert.True((await f.FireAsync(a)).Ran);
        Assert.False(f.Latest(a).Enabled);
    }

    [Fact]
    public async Task Test_now_runs_with_sample_input_and_ignores_pause_cooldown_and_conditions()
    {
        using var f = new EngineFixture();
        var a = f.Add(x =>
        {
            x.CooldownSeconds = 3600;
            x.RunMode = RunMode.AskFirst;
            x.Conditions.Add(new ConditionInstance { ConditionId = "test.cond" });
        });
        f.Condition.Result = false;
        f.Host.Settings.Update(s => s.AutomationsPaused = true);
        f.Host.Notifications.AskToRunAnswer = false;
        AutomationRanEventArgs? seen = null;
        f.Engine.AutomationRan += (_, e) => seen = e;

        var result = await f.Engine.TestNowAsync(a.Id);

        Assert.Equal(RunStatus.Succeeded, result.Status);
        Assert.Equal("Sample text", result.Output.AsText());
        Assert.True(seen!.IsTest);
        Assert.Null(f.Latest(a).LastRun);
    }

    [Fact]
    public async Task Test_now_with_a_deleted_shortcut_fails_politely()
    {
        using var f = new EngineFixture();
        var a = f.Add(x => x.ShortcutId = Guid.NewGuid());
        var result = await f.Engine.TestNowAsync(a.Id);
        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Contains("deleted", result.ErrorMessage);
    }

    [Fact]
    public async Task Fires_for_unknown_or_disabled_automations_do_nothing()
    {
        using var f = new EngineFixture();
        var off = f.Add(x => x.Enabled = false);
        Assert.False((await f.Engine.HandleFireAsync(Guid.NewGuid(), ContentValue.Empty, null)).Ran);
        Assert.False((await f.FireAsync(off)).Ran);
    }

    [Fact]
    public async Task Fire_is_ignored_while_the_same_automation_is_still_busy()
    {
        using var f = new EngineFixture();
        var a = f.Add(x => x.RunMode = RunMode.AskFirst);
        f.Engine.Reload();
        var gate = new TaskCompletionSource<bool>();
        var slow = new SlowAskNotifications(gate.Task);
        f.Host.Services.Notifications = slow;

        var first = f.FireAsync(a);
        await Task.Delay(50);
        var second = await f.FireAsync(a);
        Assert.False(second.Ran);
        Assert.Contains("busy", second.SkipReason);

        gate.SetResult(true);
        Assert.True((await first).Ran);
    }

    private sealed class SlowAskNotifications : INotificationService
    {
        private readonly Task<bool> _answer;
        public SlowAskNotifications(Task<bool> answer) { _answer = answer; }
        public void Show(string title, string body, string? imagePath = null, Action? openOnClick = null, bool silent = false) { }
        public Task<bool> AskToRunAsync(string shortcutName, string reason, CancellationToken ct = default) => _answer;
        public Task<NotificationResponse> ShowInteractiveAsync(string title, string body, IReadOnlyList<string> buttons, bool allowReply, string? imagePath = null, TimeSpan? timeout = null, CancellationToken ct = default) =>
            Task.FromResult(new NotificationResponse(null, null, true));
    }

    [Fact]
    public async Task Fire_and_wait_returns_the_shortcut_output_for_text_expansion()
    {
        using var f = new EngineFixture();
        f.Add();
        f.Engine.Reload();
        var extras = (ITriggerSinkExtras)f.Trigger.Sinks.Single();

        var output = await extras.FireAndWaitAsync(ContentValue.Text(";sig"), "typed");

        Assert.Equal(";sig", output!.AsText());
    }
}
