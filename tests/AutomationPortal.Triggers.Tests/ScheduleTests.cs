using AutomationPortal.Triggers;

namespace AutomationPortal.Triggers.Tests;

public class ScheduleCalculatorTests
{
    private static readonly IReadOnlyList<DayOfWeek> Weekdays =
        new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };

    // 2026-10-02 is a Friday.
    [Fact]
    public void Daily_picks_later_today_or_the_next_allowed_day()
    {
        var friMorning = new DateTime(2026, 10, 2, 6, 0, 0);
        Assert.Equal(new DateTime(2026, 10, 2, 7, 30, 0), ScheduleCalculator.NextDaily(friMorning, new TimeOnly(7, 30), ScheduleCalculator.EveryDay));

        var friEvening = new DateTime(2026, 10, 2, 20, 0, 0);
        Assert.Equal(new DateTime(2026, 10, 3, 7, 30, 0), ScheduleCalculator.NextDaily(friEvening, new TimeOnly(7, 30), ScheduleCalculator.EveryDay));
        Assert.Equal(new DateTime(2026, 10, 5, 7, 30, 0), ScheduleCalculator.NextDaily(friEvening, new TimeOnly(7, 30), Weekdays));
    }

    [Fact]
    public void Daily_is_strictly_after_the_reference_time()
    {
        var exactly = new DateTime(2026, 10, 2, 7, 30, 0);
        Assert.Equal(new DateTime(2026, 10, 3, 7, 30, 0), ScheduleCalculator.NextDaily(exactly, new TimeOnly(7, 30), ScheduleCalculator.EveryDay));
    }

    [Fact]
    public void Daily_with_no_days_never_fires() =>
        Assert.Null(ScheduleCalculator.NextDaily(DateTime.Now, new TimeOnly(7, 0), Array.Empty<DayOfWeek>()));

    [Fact]
    public void Daily_on_a_single_weekday_can_be_a_week_away()
    {
        var friday = new DateTime(2026, 10, 2, 9, 0, 0);
        Assert.Equal(new DateTime(2026, 10, 9, 8, 0, 0), ScheduleCalculator.NextDaily(friday, new TimeOnly(8, 0), new[] { DayOfWeek.Friday }));
    }

    [Fact]
    public void Monthly_day_number_uses_the_last_day_of_short_months()
    {
        var rule = new MonthlyRule(MonthlyMode.DayOfMonth, Day: 31);
        Assert.Equal(new DateTime(2026, 10, 31, 9, 0, 0), ScheduleCalculator.NextMonthly(new DateTime(2026, 10, 2, 0, 0, 0), rule, new TimeOnly(9, 0)));
        Assert.Equal(new DateTime(2026, 11, 30, 9, 0, 0), ScheduleCalculator.NextMonthly(new DateTime(2026, 10, 31, 10, 0, 0), rule, new TimeOnly(9, 0)));
        Assert.Equal(new DateTime(2027, 2, 28, 9, 0, 0), ScheduleCalculator.NextMonthly(new DateTime(2027, 1, 31, 10, 0, 0), rule, new TimeOnly(9, 0)));
    }

    [Fact]
    public void Monthly_last_day()
    {
        var rule = new MonthlyRule(MonthlyMode.LastDay);
        Assert.Equal(new DateTime(2028, 2, 29, 18, 0, 0), ScheduleCalculator.NextMonthly(new DateTime(2028, 2, 1, 0, 0, 0), rule, new TimeOnly(18, 0)));
    }

    [Fact]
    public void Monthly_first_monday_and_last_friday()
    {
        var firstMonday = new MonthlyRule(MonthlyMode.NthWeekday, Nth: 1, Weekday: DayOfWeek.Monday);
        // October 2026 starts on a Thursday, so the first Monday is the 5th.
        Assert.Equal(new DateTime(2026, 10, 5, 9, 0, 0), ScheduleCalculator.NextMonthly(new DateTime(2026, 10, 2, 0, 0, 0), firstMonday, new TimeOnly(9, 0)));
        Assert.Equal(new DateTime(2026, 11, 2, 9, 0, 0), ScheduleCalculator.NextMonthly(new DateTime(2026, 10, 5, 9, 0, 0), firstMonday, new TimeOnly(9, 0)));

        var lastFriday = new MonthlyRule(MonthlyMode.NthWeekday, Nth: 5, Weekday: DayOfWeek.Friday);
        Assert.Equal(new DateTime(2026, 10, 30, 9, 0, 0), ScheduleCalculator.NextMonthly(new DateTime(2026, 10, 2, 12, 0, 0), lastFriday, new TimeOnly(9, 0)));
        Assert.Equal(new DateTime(2026, 11, 27, 9, 0, 0), ScheduleCalculator.NextMonthly(new DateTime(2026, 10, 30, 9, 0, 0), lastFriday, new TimeOnly(9, 0)));
    }

    [Fact]
    public void Monthly_third_wednesday()
    {
        var rule = new MonthlyRule(MonthlyMode.NthWeekday, Nth: 3, Weekday: DayOfWeek.Wednesday);
        Assert.Equal(new DateTime(2026, 10, 21, 8, 0, 0), ScheduleCalculator.NextMonthly(new DateTime(2026, 10, 1, 0, 0, 0), rule, new TimeOnly(8, 0)));
    }

    [Fact]
    public void Interval_adds_the_interval()
    {
        var t = new DateTime(2026, 10, 2, 10, 0, 0);
        Assert.Equal(t.AddMinutes(30), ScheduleCalculator.NextInterval(t, TimeSpan.FromMinutes(30), null));
    }

    [Fact]
    public void Interval_is_never_shorter_than_a_minute()
    {
        var t = new DateTime(2026, 10, 2, 10, 0, 0);
        Assert.Equal(t.AddMinutes(1), ScheduleCalculator.NextInterval(t, TimeSpan.FromSeconds(5), null));
    }

    [Fact]
    public void Interval_with_active_hours_waits_for_the_window()
    {
        var hours = new ActiveHours(new TimeOnly(9, 0), new TimeOnly(17, 0));
        var interval = TimeSpan.FromHours(1);
        Assert.Equal(new DateTime(2026, 10, 2, 16, 30, 0), ScheduleCalculator.NextInterval(new DateTime(2026, 10, 2, 15, 30, 0), interval, hours));
        Assert.Equal(new DateTime(2026, 10, 3, 9, 0, 0), ScheduleCalculator.NextInterval(new DateTime(2026, 10, 2, 16, 30, 0), interval, hours));
        Assert.Equal(new DateTime(2026, 10, 2, 9, 0, 0), ScheduleCalculator.NextInterval(new DateTime(2026, 10, 2, 6, 0, 0), interval, hours));
    }

    [Fact]
    public void Interval_with_overnight_active_hours()
    {
        var hours = new ActiveHours(new TimeOnly(22, 0), new TimeOnly(6, 0));
        Assert.Equal(new DateTime(2026, 10, 3, 1, 0, 0), ScheduleCalculator.NextInterval(new DateTime(2026, 10, 3, 0, 0, 0), TimeSpan.FromHours(1), hours));
        Assert.Equal(new DateTime(2026, 10, 2, 22, 0, 0), ScheduleCalculator.NextInterval(new DateTime(2026, 10, 2, 12, 0, 0), TimeSpan.FromHours(1), hours));
    }

    [Fact]
    public void Active_hours_contains()
    {
        var day = new ActiveHours(new TimeOnly(9, 0), new TimeOnly(17, 0));
        Assert.True(day.Contains(new TimeOnly(9, 0)));
        Assert.False(day.Contains(new TimeOnly(17, 1)));
        var night = new ActiveHours(new TimeOnly(22, 0), new TimeOnly(6, 0));
        Assert.True(night.Contains(new TimeOnly(23, 0)));
        Assert.True(night.Contains(new TimeOnly(5, 59)));
        Assert.False(night.Contains(new TimeOnly(12, 0)));
    }
}

public class WallClockSchedulerTests
{
    private static WallClockScheduler Make(Func<DateTime> clock, List<DateTime> fired) =>
        new(now => ScheduleCalculator.NextDaily(now, new TimeOnly(8, 0), ScheduleCalculator.EveryDay), fired.Add, clock);

    [Fact]
    public void Fires_once_when_the_time_arrives()
    {
        var now = new DateTime(2026, 10, 2, 7, 59, 50);
        var fired = new List<DateTime>();
        using var s = Make(() => now, fired);
        s.Start();

        Assert.False(s.Tick());
        now = new DateTime(2026, 10, 2, 8, 0, 1);
        Assert.True(s.Tick());
        now = new DateTime(2026, 10, 2, 8, 0, 5);
        Assert.False(s.Tick());
        Assert.Single(fired);
        Assert.Equal(new DateTime(2026, 10, 3, 8, 0, 0), s.Due);
    }

    [Fact]
    public void A_time_missed_while_asleep_still_fires_within_the_grace_period()
    {
        var now = new DateTime(2026, 10, 2, 7, 59, 0);
        var fired = new List<DateTime>();
        using var s = Make(() => now, fired);
        s.Start();
        now = new DateTime(2026, 10, 2, 8, 6, 0);
        Assert.True(s.Tick());
        Assert.Equal(new DateTime(2026, 10, 2, 8, 0, 0), fired.Single());
    }

    [Fact]
    public void A_time_missed_by_hours_is_skipped()
    {
        var now = new DateTime(2026, 10, 2, 7, 59, 0);
        var fired = new List<DateTime>();
        using var s = Make(() => now, fired);
        s.Start();
        now = new DateTime(2026, 10, 2, 13, 0, 0);
        Assert.False(s.Tick());
        Assert.Empty(fired);
        Assert.Equal(new DateTime(2026, 10, 3, 8, 0, 0), s.Due);
    }

    [Fact]
    public void A_failing_callback_does_not_break_the_scheduler()
    {
        var now = new DateTime(2026, 10, 2, 7, 59, 59);
        var count = 0;
        using var s = new WallClockScheduler(n => ScheduleCalculator.NextDaily(n, new TimeOnly(8, 0), ScheduleCalculator.EveryDay), _ => { count++; throw new InvalidOperationException(); }, () => now);
        s.Start();
        now = now.AddSeconds(2);
        Assert.False(s.Tick());
        Assert.Equal(1, count);
        Assert.NotNull(s.Due);
    }

    [Fact]
    public void A_one_time_schedule_fires_once_and_then_stops()
    {
        var target = new DateTime(2026, 10, 2, 12, 0, 0);
        var now = new DateTime(2026, 10, 2, 11, 0, 0);
        var done = false;
        using var s = new WallClockScheduler(_ => done ? null : target, _ => done = true, () => now);
        s.Start();
        now = target.AddSeconds(1);
        Assert.True(s.Tick());
        now = target.AddHours(1);
        Assert.False(s.Tick());
        Assert.Null(s.Due);
    }
}

public class SunCalculatorTests
{
    private static void AssertNear(DateTimeOffset? actual, DateTime expectedUtc, int toleranceMinutes = 4)
    {
        Assert.NotNull(actual);
        var diff = Math.Abs((actual!.Value.UtcDateTime - expectedUtc).TotalMinutes);
        Assert.True(diff <= toleranceMinutes, $"Expected about {expectedUtc:u} but got {actual:u} (off by {diff:0.0} minutes)");
    }

    [Fact]
    public void London_midsummer()
    {
        var t = SunCalculator.Calculate(new DateOnly(2024, 6, 21), 51.5074, -0.1278);
        AssertNear(t.Sunrise, new DateTime(2024, 6, 21, 3, 43, 0));
        AssertNear(t.Sunset, new DateTime(2024, 6, 21, 20, 21, 0));
    }

    [Fact]
    public void New_York_midwinter()
    {
        var t = SunCalculator.Calculate(new DateOnly(2024, 12, 21), 40.7128, -74.0060);
        AssertNear(t.Sunrise, new DateTime(2024, 12, 21, 12, 16, 0));
        AssertNear(t.Sunset, new DateTime(2024, 12, 21, 21, 32, 0));
    }

    [Fact]
    public void Sydney_is_ahead_of_UTC_so_the_local_sunrise_is_the_evening_before_in_UTC()
    {
        var t = SunCalculator.Calculate(new DateOnly(2024, 12, 21), -33.8688, 151.2093);
        AssertNear(t.Sunrise, new DateTime(2024, 12, 20, 18, 41, 0));
        AssertNear(t.Sunset, new DateTime(2024, 12, 21, 9, 5, 0));
    }

    [Fact]
    public void Polar_night_and_midnight_sun_have_no_sunrise()
    {
        var night = SunCalculator.Calculate(new DateOnly(2024, 12, 21), 69.6492, 18.9553);
        Assert.Null(night.Sunrise);
        Assert.Null(night.Sunset);
        var day = SunCalculator.Calculate(new DateOnly(2024, 6, 21), 69.6492, 18.9553);
        Assert.Null(day.Sunrise);
        Assert.Null(day.Sunset);
    }

    [Fact]
    public void Next_event_is_the_first_one_after_now_with_the_offset_applied()
    {
        var utc = TimeZoneInfo.Utc;
        var after = new DateTime(2024, 6, 21, 12, 0, 0);
        var sunset = SunCalculator.NextEvent(after, 51.5074, -0.1278, sunrise: false, TimeSpan.Zero, utc)!.Value;
        Assert.InRange((sunset - new DateTime(2024, 6, 21, 20, 21, 0)).TotalMinutes, -4, 4);

        var before = SunCalculator.NextEvent(after, 51.5074, -0.1278, sunrise: false, TimeSpan.FromMinutes(-30), utc)!.Value;
        Assert.InRange((sunset - before).TotalMinutes, 29.9, 30.1);

        var sunrise = SunCalculator.NextEvent(after, 51.5074, -0.1278, sunrise: true, TimeSpan.Zero, utc)!.Value;
        Assert.Equal(new DateOnly(2024, 6, 22), DateOnly.FromDateTime(sunrise));
    }

    [Fact]
    public void Next_event_skips_polar_days_to_find_the_first_real_one()
    {
        var next = SunCalculator.NextEvent(new DateTime(2024, 12, 21, 12, 0, 0), 69.6492, 18.9553, sunrise: true, TimeSpan.Zero, TimeZoneInfo.Utc);
        Assert.NotNull(next);
        Assert.True(next > new DateTime(2025, 1, 10));
    }
}
