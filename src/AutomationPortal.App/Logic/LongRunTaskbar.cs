using AutomationPortal.App.Services;
using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

public enum TaskbarProgressKind { None, Indeterminate, Normal }

/// <summary>What the taskbar button shows: nothing, a moving bar with no end, or a bar filled to <see cref="Value"/> (0 to 1).</summary>
public readonly record struct TaskbarProgressState(TaskbarProgressKind Kind, double Value)
{
    public static readonly TaskbarProgressState None = new(TaskbarProgressKind.None, 0);
}

/// <summary>
/// Follows the runs the person started and says what the taskbar button should show. A run counts only once it is long-running
/// (the same delay as the step line on the tile), so quick runs never flash the taskbar. Runs that start by themselves are ignored.
/// </summary>
public sealed class LongRunTaskbar
{
    private readonly Dictionary<Guid, RunSource> _sources = new();
    private readonly Dictionary<Guid, (int Done, int? Total)?> _long = new();

    public void OnState(Guid id, RunSource source, TileRunState state)
    {
        if (state == TileRunState.Running) { _sources[id] = source; return; }
        _sources.Remove(id);
        _long.Remove(id);
    }

    /// <summary>Raised once a run has lasted long enough to report where it is.</summary>
    public void OnProgress(Guid id, (int Done, int? Total)? count)
    {
        if (_sources.TryGetValue(id, out var source) && RunSounds.StartedByPerson(source)) _long[id] = count;
    }

    public TaskbarProgressState Current => For(_long.Values);

    /// <summary>One long run with a known total fills the bar; anything else long-running is a bar that just moves.</summary>
    public static TaskbarProgressState For(IEnumerable<(int Done, int? Total)?> longRuns)
    {
        var runs = longRuns.ToList();
        if (runs.Count == 0) return TaskbarProgressState.None;
        if (runs.Count == 1 && runs[0] is { Total: > 0 } c) return new(TaskbarProgressKind.Normal, Math.Clamp((double)c.Done / c.Total!.Value, 0, 1));
        return new(TaskbarProgressKind.Indeterminate, 0);
    }
}
