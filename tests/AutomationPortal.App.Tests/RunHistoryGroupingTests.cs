using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.App.Tests;

public class RunHistoryGroupingTests
{
    private static readonly DateTime Today = new(2026, 11, 16);

    [Fact]
    public void Day_labels_name_today_yesterday_and_the_weekday()
    {
        Assert.Equal("Today", RunHistoryGrouping.DayLabel(Today, Today));
        Assert.Equal("Yesterday", RunHistoryGrouping.DayLabel(Today.AddDays(-1), Today));
        Assert.Equal(Today.AddDays(-3).ToString("dddd"), RunHistoryGrouping.DayLabel(Today.AddDays(-3), Today));
        Assert.Contains("2026", RunHistoryGrouping.DayLabel(Today.AddDays(-20), Today));
    }

    [Fact]
    public void Runs_are_grouped_by_day_in_order()
    {
        var runs = new[] { Today.AddHours(18), Today.AddHours(8), Today.AddDays(-1).AddHours(17), Today.AddDays(-1).AddHours(9), Today.AddDays(-9) };
        var groups = RunHistoryGrouping.ByDay(runs, r => r.Date, Today);
        Assert.Equal(new[] { "Today", "Yesterday" }, groups.Take(2).Select(g => g.Label));
        Assert.Equal(new[] { 2, 2, 1 }, groups.Select(g => g.Items.Count));
        Assert.Equal(runs.Length, groups.Sum(g => g.Items.Count));
    }

    [Fact]
    public void File_rows_name_each_change_and_count_the_rest()
    {
        var changes = Enumerable.Range(1, 6).Select(i => new FileChange { Kind = i == 1 ? FileChangeKind.Recycled : FileChangeKind.Moved, Path = @"C:\Users\me\Downloads\Pictures\p" + i + ".jpg", From = @"C:\Users\me\Downloads\p" + i + ".jpg" }).ToList();
        var (shown, more) = RunHistoryGrouping.FileRows(changes, 4);
        Assert.Equal(4, shown.Count);
        Assert.Equal(2, more);
        Assert.Equal("p1.jpg", shown[0].Name);
        Assert.Equal("sent to the Recycle Bin", shown[0].What);
        Assert.Equal(@"moved to C:\Users\me\Downloads\Pictures", shown[1].What);
    }
}
