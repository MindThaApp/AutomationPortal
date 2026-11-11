using AutomationPortal.Core;

namespace AutomationPortal.Triggers.Tests;

public class MissedRunTests
{
    private static DateTimeOffset Local(int day, int hour, int minute = 0) => new(new DateTime(2026, 10, day, hour, minute, 0, DateTimeKind.Local));

    private static TriggerInstance Daily(string time = "08:00") =>
        new() { TriggerId = "time.daily", Parameters = new() { ["time"] = ParamValue.Time(time) } };

    private static EngineFixture Fixture(DateTimeOffset now)
    {
        var f = new EngineFixture { Now = now };
        f.Triggers.Register(new DailyTrigger());
        f.Triggers.Register(new IntervalTrigger());
        f.Triggers.Register(new OnceTrigger());
        return f;
    }

    private static Automation AddDaily(EngineFixture f, bool runIfMissed, DateTimeOffset? lastRun, string time = "08:00") =>
        f.Add(a =>
        {
            a.Trigger = Daily(time);
            a.RunIfMissed = runIfMissed;
            a.LastRun = lastRun;
            a.CreatedAt = Local(1, 0);
        });

    [Fact]
    public async Task Option_off_changes_nothing()
    {
        using var f = Fixture(Local(2, 9));
        var a = AddDaily(f, false, Local(1, 8));
        f.Engine.Reload();
        Assert.Equal(0, await f.Engine.CatchUpMissedAsync());
        Assert.Equal(Local(1, 8), f.Latest(a).LastRun);
        Assert.False(new Automation().RunIfMissed);
    }

    [Fact]
    public async Task A_missed_time_runs_once_and_is_logged()
    {
        using var f = Fixture(Local(2, 9));
        var a = AddDaily(f, true, Local(1, 8));
        f.Engine.Reload();

        Assert.Equal(1, await f.Engine.CatchUpMissedAsync());
        Assert.Equal(f.Now, f.Latest(a).LastRun);
        Assert.Contains(f.Engine.Activity.Get(a.Id), e => e.Kind == ActivityKind.Ran && e.Text == "ran (catching up a missed time)");

        Assert.Equal(0, await f.Engine.CatchUpMissedAsync());
    }

    [Fact]
    public async Task Several_missed_times_run_only_once()
    {
        using var f = Fixture(Local(3, 9));
        var a = AddDaily(f, true, Local(1, 8));
        f.Engine.Reload();
        Assert.Equal(1, await f.Engine.CatchUpMissedAsync());
        Assert.Equal(1, f.Host.History.Records.Count(r => r.Source == RunSource.Automation));
    }

    [Fact]
    public async Task Nothing_missed_when_it_already_ran_after_the_last_time()
    {
        using var f = Fixture(Local(2, 9));
        AddDaily(f, true, Local(2, 8, 1));
        f.Engine.Reload();
        Assert.Equal(0, await f.Engine.CatchUpMissedAsync());
    }

    [Fact]
    public async Task Times_older_than_24_hours_are_skipped()
    {
        using var f = Fixture(Local(4, 9));
        f.Add(a =>
        {
            a.Trigger = new TriggerInstance { TriggerId = "time.once", Parameters = new() { ["when"] = ParamValue.Date(Local(3, 8, 59)) } };
            a.RunIfMissed = true; a.CreatedAt = Local(1, 0);
        });
        f.Engine.Reload();
        Assert.Equal(0, await f.Engine.CatchUpMissedAsync());
    }
    [Fact]
    public async Task A_time_just_inside_24_hours_still_runs()
    {
        using var f = Fixture(Local(4, 7, 30));
        AddDaily(f, true, Local(1, 8), time: "07:00");
        f.Engine.Reload();
        Assert.Equal(1, await f.Engine.CatchUpMissedAsync());
    }

    [Fact]
    public async Task Times_before_the_automation_existed_are_not_caught_up()
    {
        using var f = Fixture(Local(2, 9));
        f.Add(a => { a.Trigger = Daily(); a.RunIfMissed = true; a.CreatedAt = Local(2, 8, 30); });
        f.Engine.Reload();
        Assert.Equal(0, await f.Engine.CatchUpMissedAsync());
    }

    [Fact]
    public async Task Paused_automations_are_not_caught_up()
    {
        using var f = Fixture(Local(2, 9));
        AddDaily(f, true, Local(1, 8));
        f.Host.Settings.Update(s => { s.AutomationsPaused = true; s.AutomationsPausedUntil = Local(2, 10); });
        f.Engine.Reload();
        Assert.Equal(0, await f.Engine.CatchUpMissedAsync());
    }

    [Fact]
    public async Task A_time_that_passed_during_a_pause_is_not_caught_up_after_it_ends()
    {
        using var f = Fixture(Local(2, 7));
        AddDaily(f, true, Local(1, 8));
        f.Host.Settings.Update(s => s.AutomationsPaused = true);
        f.Engine.Reload();
        f.Now = Local(2, 9);
        f.Host.Settings.Update(s => { s.AutomationsPaused = false; s.AutomationsPausedUntil = null; });
        f.Engine.Reload();
        f.Now = Local(2, 12);
        Assert.Equal(0, await f.Engine.CatchUpMissedAsync());
    }

    [Fact]
    public async Task Interval_and_one_time_schedules_catch_up_too()
    {
        using var f = Fixture(Local(2, 9));
        var every = f.Add(a =>
        {
            a.Trigger = new TriggerInstance { TriggerId = "time.interval", Parameters = new() { ["every"] = ParamValue.Number(30), ["unit"] = ParamValue.Choice("minutes") } };
            a.RunIfMissed = true; a.LastRun = Local(2, 8); a.CreatedAt = Local(1, 0);
        });
        var once = f.Add(a =>
        {
            a.Trigger = new TriggerInstance { TriggerId = "time.once", Parameters = new() { ["when"] = ParamValue.Date(Local(2, 6)) } };
            a.RunIfMissed = true; a.CreatedAt = Local(1, 0);
        });
        f.Engine.Reload();
        Assert.Equal(2, await f.Engine.CatchUpMissedAsync());
        Assert.False(f.Latest(once).Enabled);
        Assert.NotNull(f.Latest(every).LastRun);
    }

    [Fact]
    public async Task Non_schedule_triggers_are_ignored()
    {
        using var f = Fixture(Local(2, 9));
        f.Add(a => { a.RunIfMissed = true; a.LastRun = Local(1, 8); });
        f.Engine.Reload();
        Assert.Equal(0, await f.Engine.CatchUpMissedAsync());
    }
}
