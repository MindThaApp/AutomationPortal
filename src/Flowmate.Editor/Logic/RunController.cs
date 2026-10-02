using System.IO;
using Flowmate.Core;

namespace Flowmate.Editor.Logic;

public enum StepState { Idle, Running, Succeeded, Failed }

public sealed class StepInfo
{
    public StepState State { get; internal set; }
    public TimeSpan Duration { get; internal set; }
    public string? Error { get; internal set; }
}

/// <summary>
/// Runs the shortcut being edited and records live feedback (per-step state, outputs, named variables).
/// Observer callbacks arrive on worker threads: UI code must marshal the events.
/// </summary>
public sealed class RunController : IRunObserver
{
    private readonly object _gate = new();
    private readonly Dictionary<string, StepInfo> _steps = new();
    private readonly Dictionary<string, ContentValue> _outputs = new();
    private readonly Dictionary<string, ContentValue> _named = new();
    private readonly Dictionary<string, Dictionary<string, ContentValue>> _namedAfter = new();
    private readonly List<string> _keptTemp = new();
    private CancellationTokenSource? _cts;

    public bool IsRunning { get; private set; }
    public RunResult? LastResult { get; private set; }
    public string? CurrentUid { get; private set; }

    public event Action<string>? StepChanged;
    public event Action? RunStateChanged;
    public event Action? VariablesChanged;

    public StepInfo? StepOf(string uid) { lock (_gate) return _steps.TryGetValue(uid, out var s) ? s : null; }

    /// <summary>Outputs of the steps that ran (uid to value). Copy.</summary>
    public Dictionary<string, ContentValue> Outputs() { lock (_gate) return new(_outputs); }
    public Dictionary<string, ContentValue> NamedVariables() { lock (_gate) return new(_named); }

    /// <summary>Seed outputs for "Run from here": the last outputs of every step above the top-level block containing <paramref name="startIndex"/>.</summary>
    public Dictionary<string, ContentValue> BuildSeed(IReadOnlyList<ActionInstance> actions, int startIndex)
    {
        var first = FlowGroups.TopLevelStart(actions, startIndex);
        var before = actions.Take(first).Select(a => a.Uid).ToHashSet();
        lock (_gate) return _outputs.Where(o => before.Contains(o.Key)).ToDictionary(o => o.Key, o => o.Value);
    }

    /// <summary>Named variables as they stood after the last step above the start point that has a snapshot.</summary>
    public Dictionary<string, ContentValue> BuildVariableSeed(IReadOnlyList<ActionInstance> actions, int startIndex)
    {
        var first = FlowGroups.TopLevelStart(actions, startIndex);
        lock (_gate)
        {
            for (var i = first - 1; i >= 0; i--)
                if (_namedAfter.TryGetValue(actions[i].Uid, out var snap)) return new(snap);
            return new();
        }
    }

    /// <summary>Deletes the temp folders kept for "Run from here" (their files are the seeded outputs).</summary>
    public void DiscardTemp(bool keepLatest = false)
    {
        List<string> doomed;
        lock (_gate)
        {
            var keep = keepLatest && _keptTemp.Count > 0 ? 1 : 0;
            doomed = _keptTemp.Take(_keptTemp.Count - keep).ToList();
            _keptTemp.RemoveRange(0, doomed.Count);
        }
        foreach (var d in doomed)
            try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void Cancel() { try { _cts?.Cancel(); } catch (ObjectDisposedException) { } }

    /// <summary>Clears the recorded results of all steps (e.g. after a big edit).</summary>
    public void ClearResults()
    {
        lock (_gate) { _steps.Clear(); _outputs.Clear(); _named.Clear(); _namedAfter.Clear(); LastResult = null; }
        DiscardTemp();
        RunStateChanged?.Invoke();
        VariablesChanged?.Invoke();
    }

    public async Task<RunResult> RunAsync(Shortcut working, IShortcutRunner runner, int? startIndex = null, ContentValue? input = null)
    {
        if (IsRunning) throw new InvalidOperationException("A run is already in progress.");
        Dictionary<string, ContentValue>? seed = null, seedVars = null;
        string? startUid = null;
        List<string> resetUids;
        if (startIndex is { } si && si >= 0 && si < working.Actions.Count)
        {
            startUid = working.Actions[si].Uid;
            seed = BuildSeed(working.Actions, si);
            seedVars = BuildVariableSeed(working.Actions, si);
            var first = FlowGroups.TopLevelStart(working.Actions, si);
            resetUids = working.Actions.Skip(first).Select(a => a.Uid).ToList();
        }
        else
        {
            resetUids = working.Actions.Select(a => a.Uid).ToList();
        }

        lock (_gate)
        {
            if (startUid is null) { _steps.Clear(); _outputs.Clear(); _namedAfter.Clear(); }
            else foreach (var u in resetUids) { _steps.Remove(u); _outputs.Remove(u); _namedAfter.Remove(u); }
            _named.Clear();
            if (seedVars is not null) foreach (var (k, v) in seedVars) _named[k] = v;
            LastResult = null;
            CurrentUid = null;
        }
        _cts = new CancellationTokenSource();
        IsRunning = true;
        RunStateChanged?.Invoke();
        VariablesChanged?.Invoke();
        foreach (var u in resetUids) StepChanged?.Invoke(u);

        RunResult result;
        try
        {
            var options = new RunOptions
            {
                Source = RunSource.Editor,
                CancellationToken = _cts.Token,
                Observer = this,
                StartAtUid = startUid,
                SeedOutputs = seed,
                SeedVariables = seedVars,
                KeepTemp = true,
            };
            result = await runner.RunAsync(working, input ?? ContentValue.Empty, options).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = new RunResult
            {
                Status = RunStatus.Failed, ShortcutId = working.Id, ShortcutName = working.Name,
                ErrorMessage = "Something went wrong while running: " + ex.Message, Started = DateTimeOffset.Now,
            };
        }
        finally
        {
            IsRunning = false;
            CurrentUid = null;
        }
        LastResult = result;
        if (result.TempFolder is { } temp) lock (_gate) _keptTemp.Add(temp);
        // A full run replaces every output, so the folders of earlier runs are no longer referenced.
        if (startUid is null) DiscardTemp(keepLatest: true);
        RunStateChanged?.Invoke();
        return result;
    }

    // ---- IRunObserver ------------------------------------------------------------------------

    public void OnStepStarted(string uid)
    {
        lock (_gate)
        {
            CurrentUid = uid;
            if (!_steps.TryGetValue(uid, out var s)) _steps[uid] = s = new StepInfo();
            s.State = StepState.Running; s.Error = null;
        }
        StepChanged?.Invoke(uid);
    }

    public void OnStepFinished(string uid, ContentValue output, TimeSpan duration, string? error)
    {
        lock (_gate)
        {
            if (!_steps.TryGetValue(uid, out var s)) _steps[uid] = s = new StepInfo();
            s.State = error is null ? StepState.Succeeded : StepState.Failed;
            s.Duration = duration; s.Error = error;
            _outputs[uid] = output;
            _namedAfter[uid] = new(_named);
        }
        StepChanged?.Invoke(uid);
        VariablesChanged?.Invoke();
    }

    public void OnVariableSet(string name, ContentValue value)
    {
        lock (_gate) _named[name] = value;
        VariablesChanged?.Invoke();
    }

    public void OnLog(string message) { }
}
