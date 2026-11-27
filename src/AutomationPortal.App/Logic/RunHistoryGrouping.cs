using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

/// <summary>One line of "Files changed": the item, what happened to it, and the path to show in Explorer.</summary>
public sealed record FileChangeRow(string Name, string What, string Path);

/// <summary>Run History's day groups and the "Files changed" rows.</summary>
public static class RunHistoryGrouping
{
    /// <summary>"Today", "Yesterday", the weekday for the rest of the week, otherwise the date.</summary>
    public static string DayLabel(DateTime day, DateTime today)
    {
        var gap = (today.Date - day.Date).Days;
        if (gap <= 0) return "Today";
        if (gap == 1) return "Yesterday";
        if (gap < 7) return day.ToString("dddd", System.Globalization.CultureInfo.CurrentCulture);
        return day.ToString("d MMMM yyyy", System.Globalization.CultureInfo.CurrentCulture);
    }

    /// <summary>Splits runs (newest first) into day groups in the same order.</summary>
    public static IReadOnlyList<(string Label, IReadOnlyList<T> Items)> ByDay<T>(IEnumerable<T> runs, Func<T, DateTime> day, DateTime today)
    {
        var groups = new List<(string Label, List<T> Items)>();
        foreach (var run in runs)
        {
            var label = DayLabel(day(run), today);
            if (groups.Count == 0 || groups[^1].Label != label) groups.Add((label, new List<T>()));
            groups[^1].Items.Add(run);
        }
        return groups.Select(g => (g.Label, (IReadOnlyList<T>)g.Items)).ToList();
    }

    /// <summary>The first <paramref name="max"/> changes as rows, and how many more there are.</summary>
    public static (IReadOnlyList<FileChangeRow> Shown, int More) FileRows(IReadOnlyList<FileChange> changes, int max)
    {
        var rows = changes.Take(max).Select(c =>
        {
            var name = System.IO.Path.GetFileName(c.Path.TrimEnd('\\', '/'));
            var folder = System.IO.Path.GetDirectoryName(c.Path.TrimEnd('\\', '/')) ?? "";
            var what = c.Kind switch
            {
                FileChangeKind.Moved => Strings.Get("RunHistoryGrouping_MovedTo") + folder,
                FileChangeKind.Renamed => Strings.Get("RunHistoryGrouping_RenamedFrom") + (c.From is { Length: > 0 } f ? System.IO.Path.GetFileName(f) : Strings.Get("RunHistoryGrouping_ItsOldName")),
                FileChangeKind.Created => Strings.Get("RunHistoryGrouping_CreatedIn") + folder,
                FileChangeKind.Recycled => Strings.Get("RunHistoryGrouping_SentToTheRecycle"),
                FileChangeKind.Deleted => "deleted",
                FileChangeKind.Replaced => Strings.Get("RunHistoryGrouping_ReplacedIn") + folder,
                _ => Strings.Get("RunHistoryGrouping_ChangedIn") + folder,
            };
            return new FileChangeRow(string.IsNullOrEmpty(name) ? c.Path : name, what, c.Path);
        }).ToList();
        return (rows, Math.Max(0, changes.Count - max));
    }
}
