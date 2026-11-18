using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

/// <summary>
/// How a library tile starts a run, for a click and for files dropped on it alike. When the one-time check before a run that changes
/// files or controls other apps is due, that check (with its preview) comes first and nothing runs until the person says so; otherwise
/// the run starts straight away. Either way it is a normal library run, so its result bar offers Undo.
/// </summary>
public static class TileRunGate
{
    /// <param name="input">Dropped files, or null for a click.</param>
    /// <param name="checkFirst">Shows the check for these steps and runs with <paramref name="input"/> only if the person agrees.</param>
    /// <param name="run">Runs with <paramref name="input"/>.</param>
    public static void Start(Shortcut shortcut, ContentValue? input, AppSettings settings, ActionRegistry actions,
        Action<IReadOnlyList<string>, ContentValue?> checkFirst, Action<ContentValue?> run)
    {
        var steps = FileChangeCheck.CheckedSteps(shortcut, actions);
        if (FileChangeCheck.ShouldAsk(settings, shortcut.Id, steps)) checkFirst(steps, input);
        else run(input);
    }
}
