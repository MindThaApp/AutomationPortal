using AutomationPortal.App.Logic;
using Xunit;

namespace AutomationPortal.App.Tests;

public class TrayMenuTests
{
    private static DateTimeOffset Local(int y, int mo, int d, int h, int mi)
    {
        var dt = new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt));
    }

    [Fact]
    public void Timed_choices_add_the_interval()
    {
        var now = Local(2026, 6, 10, 12, 0);
        Assert.Equal(now.AddMinutes(30), TrayMenuLogic.PauseUntil(PauseChoice.ThirtyMinutes, now));
        Assert.Equal(now.AddHours(1), TrayMenuLogic.PauseUntil(PauseChoice.OneHour, now));
        Assert.Null(TrayMenuLogic.PauseUntil(PauseChoice.UntilResumed, now));
    }

    [Theory]
    [InlineData(2, 6, 10, 6)]
    [InlineData(6, 0, 11, 6)]
    [InlineData(23, 30, 11, 6)]
    public void Until_tomorrow_is_the_next_six_in_the_morning(int h, int m, int expectedDay, int expectedHour)
    {
        var until = TrayMenuLogic.PauseUntil(PauseChoice.UntilTomorrow, Local(2026, 6, 10, h, m))!.Value.LocalDateTime;
        Assert.Equal(new DateTime(2026, 6, expectedDay, expectedHour, 0, 0), until);
    }

    [Fact]
    public void Expired_pause_counts_as_resumed()
    {
        var now = Local(2026, 6, 10, 12, 0);
        Assert.False(TrayMenuLogic.IsPaused(true, now.AddMinutes(-1), now));
        Assert.True(TrayMenuLogic.IsPaused(true, now.AddMinutes(1), now));
        Assert.True(TrayMenuLogic.IsPaused(true, null, now));
        Assert.False(TrayMenuLogic.IsPaused(false, null, now));
    }

    [Fact]
    public void Tooltip_and_status_show_the_end_time()
    {
        var now = Local(2026, 6, 10, 12, 0);
        var m = TrayMenuLogic.Build(true, Local(2026, 6, 10, 14, 30), now, 0, null, []);
        Assert.Equal("Paused until 14:30", m.PauseStatus);
        Assert.Equal("Automation Portal - Paused until 14:30", m.Tooltip);
        Assert.Equal("Paused until you resume", TrayMenuLogic.Build(true, null, now, 0, null, []).PauseStatus);
    }

    [Fact]
    public void Stop_item_only_while_something_runs()
    {
        var now = Local(2026, 6, 10, 12, 0);
        Assert.Null(TrayMenuLogic.Build(false, null, now, 0, null, []).StopHeader);
        Assert.Equal("Stop running shortcuts (2)", TrayMenuLogic.Build(false, null, now, 2, null, []).StopHeader);
    }

    [Fact]
    public void Pinned_overflow_adds_more_item()
    {
        var now = Local(2026, 6, 10, 12, 0);
        var ids = Enumerable.Range(0, 12).Select(_ => Guid.NewGuid()).ToList();
        var m = TrayMenuLogic.Build(false, null, now, 0, null, ids);
        Assert.Equal(10, m.PinnedShown.Count);
        Assert.True(m.ShowMorePinned);
        Assert.False(TrayMenuLogic.Build(false, null, now, 0, null, ids.Take(10).ToList()).ShowMorePinned);
    }

    [Fact]
    public void Failure_clears_on_later_success_of_the_same_shortcut_only()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var f = TrayMenuLogic.NextFailure(null, a, "A", true, false, "r1");
        Assert.Equal("Didn't finish: A", TrayMenuLogic.Build(false, null, DateTimeOffset.Now, 0, f, []).FailedHeader);
        Assert.Same(f, TrayMenuLogic.NextFailure(f, b, "B", false, true, "r2"));
        Assert.Null(TrayMenuLogic.NextFailure(f, a, "A", false, true, "r3"));
        Assert.Equal(b, TrayMenuLogic.NextFailure(f, b, "B", true, false, "r4")!.ShortcutId);
    }
}
