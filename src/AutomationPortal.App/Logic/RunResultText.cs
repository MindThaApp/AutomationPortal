using AutomationPortal.Actions.Documents;
using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

/// <summary>
/// What the result bar (and the toast when the window is hidden) says after a run that changed files.
/// <see cref="CanUndo"/> is true only when this run's own record has changes that undo can put back by itself.
/// </summary>
public sealed record RunResultBarModel(string RunId, string ShortcutName, string Summary, string? Message, bool CanUndo)
{
    /// <summary>"Tidy Downloads" moved 14 files.</summary>
    public string Title => $"\"{ShortcutName}\" {Summary}.";
}

/// <summary>The sentences for the result bar after a run, and for what an Undo from it did.</summary>
public static class RunResultText
{
    /// <summary>
    /// The bar for a run that finished: null when the run's record has no file changes, so there is nothing to undo or report.
    /// </summary>
    /// <param name="canRecycle">Whether the drive of a path has a Recycle Bin. Tests pass their own.</param>
    public static RunResultBarModel? ForRun(string shortcutName, string runId, JournaledRun? run, Func<string, bool>? canRecycle = null)
    {
        if (run is null || run.Changes.Count == 0 || !string.Equals(run.RunId, runId, StringComparison.Ordinal)) return null;
        var plan = canRecycle is null ? FileUndo.Plan(run) : FileUndo.Plan(run, canRecycle);
        var canUndo = FileUndo.CanUndo(run) && plan.Any(s => s.Automatic);
        string? message = null;
        if (run.UndoneAt is not null) message = Strings.Get("RunResultText_TheseChangesWereAlready");
        else if (!canUndo) message = Strings.Get("RunResultText_TheseChangesCantBe");
        else if (plan.Any(s => !s.Automatic)) message = Strings.Get("RunResultText_UndoPutsBackWhat");
        return new RunResultBarModel(runId, shortcutName, Summary(run.Changes), message, canUndo);
    }

    /// <summary>Runs that report their file changes with a result bar or toast. The editor has its own results; nested and command-line runs report to their caller.</summary>
    public static bool Reports(RunSource source) => source is RunSource.Library or RunSource.Hotkey or RunSource.QuickRun or RunSource.Automation
        or RunSource.SendTo or RunSource.Link or RunSource.Reminder;

    /// <summary>"moved 14 files and renamed 2 files": what a run's changes amount to, in the order they first happened.</summary>
    public static string Summary(IReadOnlyList<FileChange> changes)
    {
        var parts = changes.GroupBy(c => c.Kind).Select(g => Phrase(g.Key, g.Count())).ToList();
        return parts.Count switch
        {
            0 => "didn't change any files",
            1 => parts[0],
            _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
        };
    }

    private static string Files(int n) => n == 1 ? "1 file" : $"{n} files";

    private static string Phrase(FileChangeKind kind, int n) => kind switch
    {
        FileChangeKind.Created => $"made {Files(n)}",
        FileChangeKind.Moved => $"moved {Files(n)}",
        FileChangeKind.Renamed => $"renamed {Files(n)}",
        FileChangeKind.Recycled => $"sent {Files(n)} to the Recycle Bin",
        FileChangeKind.Deleted => $"deleted {Files(n)} for good",
        FileChangeKind.Replaced => $"replaced {Files(n)}",
        _ => $"changed {Files(n)}",
    };

    /// <summary>How many lines of "not put back" the bar lists before pointing to the details.</summary>
    public const int MaxListed = 4;

    /// <summary>
    /// What an undo did, item by item: everything put back, or exactly which items were and weren't, and why.
    /// </summary>
    public static (string Title, string Message) UndoReport(IReadOnlyList<UndoOutcome> results)
    {
        var done = results.Count(r => r.Done);
        if (results.Count > 0 && done == results.Count)
            return ("Undone", results.Count == 1 ? "The change was put back." : $"All {results.Count} changes were put back.");

        var title = done == 0 ? "Nothing was put back" : $"Put back {done} of {results.Count} changes";
        var missed = results.Where(r => !r.Done).ToList();
        var lines = new List<string> { "Not put back:" };
        foreach (var r in missed.Take(MaxListed))
            lines.Add("• " + (r.Step.Automatic ? $"{ItemName(r.Step.Change)}: {r.Problem}" : r.Step.Description));
        if (missed.Count > MaxListed) lines.Add($"…and {missed.Count - MaxListed} more. See details for the full list.");
        lines.Add(Strings.Get("RunResultText_NothingWasOverwritten"));
        return (title, string.Join("\n", lines));
    }

    private static string ItemName(FileChange c)
    {
        var name = Path.GetFileName(c.Path.TrimEnd('\\', '/'));
        return name.Length == 0 ? c.Path : $"'{name}'";
    }
}
