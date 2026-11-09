using Flowmate.App.Logic;
using Flowmate.Core;
using Flowmate.Triggers;

namespace Flowmate.App.Services;

/// <summary>The real macro recorder (low-level keyboard and mouse hooks) behind <see cref="IMacroRecorderSource"/>.</summary>
public sealed class TriggerRecorderSource : IMacroRecorderSource
{
    private readonly MacroRecorder _recorder;
    public TriggerRecorderSource(MacroRecorder recorder) { _recorder = recorder; }

    public string StopHotkey => _recorder.StopHotkey;
    public bool DropWaits { get => _recorder.DropWaits; set => _recorder.DropWaits = value; }
    public void Start() => _recorder.Start();
    public void Pause() => _recorder.Pause();
    public void Resume() => _recorder.Resume();
    public IReadOnlyList<ActionInstance> Stop() => _recorder.Stop();

    public event EventHandler<IReadOnlyList<ActionInstance>>? Stopped
    {
        add => _recorder.Stopped += value;
        remove => _recorder.Stopped -= value;
    }
}

/// <summary>
/// A recorder that watches nothing and records a fixed set of steps: used by <c>--fake-recorder</c> to try the recording bar and the
/// editor wiring without any real keyboard or mouse capture.
/// </summary>
public sealed class FakeRecorderSource : IMacroRecorderSource
{
    private bool _recording;
    public string StopHotkey => MacroRecorder.DefaultStopHotkey;
    public bool DropWaits { get; set; }
    public bool Paused { get; private set; }
    public void Start() { _recording = true; Paused = false; }
    public void Pause() => Paused = true;
    public void Resume() => Paused = false;

    public IReadOnlyList<ActionInstance> Stop()
    {
        if (!_recording) return Array.Empty<ActionInstance>();
        _recording = false;
        var steps = new List<ActionInstance>
        {
            ActionInstance.Create("app.switch", ("app", ParamValue.App("notepad", "Notepad"))),
            ActionInstance.Create("input.click", ("x", 120), ("y", 80), ("relativeTo", ParamValue.Choice("window")), ("app", ParamValue.App("notepad", "Notepad")),
                ("button", ParamValue.Choice("left")), ("clicks", 1)),
            ActionInstance.Create("input.type", ("text", "Hello from the fake recorder"), ("delayMs", 20)),
        };
        Stopped?.Invoke(this, steps);
        return steps;
    }

    public event EventHandler<IReadOnlyList<ActionInstance>>? Stopped;
}
