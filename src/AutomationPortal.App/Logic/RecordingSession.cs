using System.Globalization;
using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

/// <summary>The macro recorder as the recording bar sees it. The real one wraps <c>TriggerHost.Recorder</c>; tests and <c>--fake-recorder</c> use a stand-in that creates no input hooks.</summary>
public interface IMacroRecorderSource
{
    string StopHotkey { get; }
    bool DropWaits { get; set; }
    /// <summary>Throws <see cref="InvalidOperationException"/> with a plain message when Windows won't let the app watch the keyboard and mouse.</summary>
    void Start();
    void Pause();
    void Resume();
    IReadOnlyList<ActionInstance> Stop();
    /// <summary>Recording ended, by <see cref="Stop"/> or by the stop shortcut (may be raised on another thread).</summary>
    event EventHandler<IReadOnlyList<ActionInstance>>? Stopped;
}

/// <summary>State of one recording for the floating bar: elapsed time (not counting pauses), pause, "Ignore waits", and the finished steps. No UI.</summary>
public sealed class RecordingSession
{
    private readonly IMacroRecorderSource _source;
    private readonly Func<DateTime> _now;
    private readonly TaskCompletionSource<IReadOnlyList<ActionInstance>> _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private DateTime _runningSince;
    private TimeSpan _before;
    private bool _paused, _started, _stopped;

    public RecordingSession(IMacroRecorderSource source, Func<DateTime>? clock = null)
    {
        _source = source;
        _now = clock ?? (() => DateTime.UtcNow);
        _source.Stopped += OnStopped;
    }

    public string StopHotkey => _source.StopHotkey;
    public bool IsPaused { get { lock (_gate) return _paused; } }
    public bool IsRunning { get { lock (_gate) return _started && !_stopped; } }
    public string? StartError { get; private set; }

    /// <summary>Completes with the recorded steps when recording ends.</summary>
    public Task<IReadOnlyList<ActionInstance>> Finished => _finished.Task;

    public bool DropWaits
    {
        get => _source.DropWaits;
        set => _source.DropWaits = value;
    }

    public TimeSpan Elapsed
    {
        get { lock (_gate) return _started ? _before + (_paused || _stopped ? TimeSpan.Zero : _now() - _runningSince) : TimeSpan.Zero; }
    }

    public bool Start()
    {
        lock (_gate)
        {
            if (_started) return true;
            try { _source.Start(); }
            catch (InvalidOperationException ex) { StartError = ex.Message; _source.Stopped -= OnStopped; return false; }
            _started = true;
            _runningSince = _now();
        }
        return true;
    }

    public void TogglePause()
    {
        lock (_gate)
        {
            if (!_started || _stopped) return;
            if (_paused) { _source.Resume(); _paused = false; _runningSince = _now(); }
            else { _before += _now() - _runningSince; _source.Pause(); _paused = true; }
        }
    }

    /// <summary>Ends the recording (also what the bar's Stop button calls). Safe to call more than once.</summary>
    public void Stop()
    {
        lock (_gate) { if (!_started || _stopped) return; }
        var steps = _source.Stop();
        Complete(steps);
    }

    private void OnStopped(object? sender, IReadOnlyList<ActionInstance> steps) => Complete(steps);

    private void Complete(IReadOnlyList<ActionInstance> steps)
    {
        lock (_gate)
        {
            if (_stopped) return;
            if (!_paused) _before += _now() - _runningSince;
            _stopped = true;
        }
        _source.Stopped -= OnStopped;
        _finished.TrySetResult(steps);
    }

    /// <summary>"0:07", "12:34", "1:02:03".</summary>
    public static string FormatElapsed(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : string.Create(CultureInfo.InvariantCulture, $"{t.Minutes}:{t.Seconds:00}");
}
