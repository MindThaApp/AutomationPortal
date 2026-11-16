using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

/// <summary>
/// Decides when a shortcut that changes files, or that clicks and types in other apps, gets a one-time check before it runs from
/// the library. The same classification feeds the prompt shown when another app or a link asks to run it.
/// </summary>
public static class FileChangeCheck
{
    /// <summary>Blocks that only shape the flow (their steps are checked one by one); "Run Shortcut" is not one of them.</summary>
    private static readonly HashSet<string> FlowOnlyIds = new(StringComparer.OrdinalIgnoreCase)
    {
        FlowIds.Comment, FlowIds.If, FlowIds.Menu, FlowIds.Repeat, FlowIds.RepeatEach,
    };

    /// <summary>
    /// A step changes files unless it is known not to: it only reads (<see cref="FilePreview.ReadOnlyActionIds"/>), it is marked as
    /// not touching files (<see cref="FilePreview.DoesNotChangeFiles"/>), or it only shapes the flow. Anything new or unknown counts.
    /// </summary>
    public static bool ChangesFiles(string actionId) =>
        !FilePreview.ReadOnlyActionIds.Contains(actionId) && !FilePreview.DoesNotChangeFiles(actionId) && !FlowOnlyIds.Contains(actionId);

    /// <summary>Titles of the steps that may create, change, move, rename, delete or replace files.</summary>
    public static IReadOnlyList<string> FileChangingSteps(Shortcut shortcut, ActionRegistry actions) =>
        shortcut.Actions
            .Where(x => x.Flow is not { Role: not FlowRole.Start })
            .Where(x => ChangesFiles(x.ActionId))
            // Steps that click and type in other apps are listed as such (see AppControlSteps), not twice.
            .Where(x => actions.Describe(x.ActionId) is not { } d || !d.ImportRiskOf(x).HasFlag(ImportRisk.ControlsApps))
            .Select(x => actions.Describe(x.ActionId)?.Title ?? x.ActionId)
            .Distinct().ToList();

    /// <summary>Titles of the steps that click and type in other apps (they count like file-changing steps).</summary>
    public static IReadOnlyList<string> AppControlSteps(Shortcut shortcut, ActionRegistry actions) =>
        shortcut.Actions
            .Where(x => x.Flow is not { Role: not FlowRole.Start })
            .Select(x => actions.Describe(x.ActionId) is { } d && d.ImportRiskOf(x).HasFlag(ImportRisk.ControlsApps) ? d.Title : null)
            .OfType<string>().Distinct().ToList();

    public static bool ControlsApps(Shortcut shortcut, ActionRegistry actions) => AppControlSteps(shortcut, actions).Count > 0;

    /// <summary>The steps the one-time check lists: those that change files and those that control other apps.</summary>
    public static IReadOnlyList<string> CheckedSteps(Shortcut shortcut, ActionRegistry actions) =>
        FileChangingSteps(shortcut, actions).Concat(AppControlSteps(shortcut, actions)).Distinct().ToList();

    public static bool ShouldAsk(AppSettings settings, Guid shortcutId, IReadOnlyCollection<string> steps) =>
        steps.Count > 0 && settings.AskBeforeFileChangingRuns && !settings.FileChangeCheckedShortcutIds.Contains(shortcutId);

    public static string Message(string name, IReadOnlyCollection<string> steps) =>
        $"\"{name}\" changes files on this PC. Steps that do: {string.Join(", ", steps)}.\n\nCheck its steps first if you are not sure which folders it works on.";

    /// <summary>The check's text, also covering steps that click and type in other apps.</summary>
    public static string Message(string name, IReadOnlyCollection<string> steps, bool controlsApps, bool changesFiles) =>
        !controlsApps ? Message(name, steps)
        : $"\"{name}\" {(changesFiles ? "changes files and clicks and types in other apps" : "clicks and types in other apps")} on this PC. Steps that do: {string.Join(", ", steps)}.\n\n"
          + "Check its steps first if you are not sure which apps and folders it works on. Leave the apps alone while it runs.";
}
