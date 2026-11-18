using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

/// <summary>When a library tile asks before it runs: on a click, and when items are dropped on it.</summary>
public static class TileRunGate
{
    /// <summary>A drop of more items than this always asks first.</summary>
    public const int ConfirmAboveItems = 100;

    /// <summary>A click: the one-time check comes first when it is due (see <see cref="FileChangeCheck.ShouldAsk"/>); otherwise it runs.</summary>
    public static void Start(Shortcut shortcut, AppSettings settings, ActionRegistry actions, Action<IReadOnlyList<string>> checkFirst, Action run)
    {
        var steps = FileChangeCheck.CheckedSteps(shortcut, actions);
        if (FileChangeCheck.ShouldAsk(settings, shortcut.Id, steps)) checkFirst(steps);
        else run();
    }

    /// <summary>
    /// A drop asks first every time the shortcut changes files or controls other apps, whatever the one-time check or Settings say,
    /// and also for a large drop or one from a network location. Only a small, local drop on a shortcut that only reads runs straight away.
    /// </summary>
    public static bool DropAsksFirst(Shortcut shortcut, ActionRegistry actions, DroppedSelection drop) =>
        FileChangeCheck.CheckedSteps(shortcut, actions).Count > 0 || drop.Accepted.Count > ConfirmAboveItems || drop.HasNetworkPath;
}
