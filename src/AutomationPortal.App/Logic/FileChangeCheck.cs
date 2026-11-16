using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

/// <summary>Decides when a shortcut that changes files gets a one-time check before it runs from the library.</summary>
public static class FileChangeCheck
{
    private static readonly HashSet<string> FileChangingIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "file.move", "file.rename", "file.delete",
    };

    /// <summary>Titles of the steps that move, rename, delete or replace files.</summary>
    public static IReadOnlyList<string> FileChangingSteps(Shortcut shortcut, ActionRegistry actions) =>
        shortcut.Actions
            .Where(x => x.Flow is not { Role: not FlowRole.Start })
            .Select(x => actions.Describe(x.ActionId) is { } d
                && (FileChangingIds.Contains(x.ActionId) || (d.Category == ActionCategories.FilesAndFolders && d.IsDestructiveFor(x)))
                    ? d.Title : null)
            .OfType<string>().Distinct().ToList();

    public static bool ShouldAsk(AppSettings settings, Guid shortcutId, IReadOnlyCollection<string> steps) =>
        steps.Count > 0 && settings.AskBeforeFileChangingRuns && !settings.FileChangeCheckedShortcutIds.Contains(shortcutId);

    public static string Message(string name, IReadOnlyCollection<string> steps) =>
        $"\"{name}\" changes files on this PC. Steps that do: {string.Join(", ", steps)}.\n\nCheck its steps first if you are not sure which folders it works on.";
}
