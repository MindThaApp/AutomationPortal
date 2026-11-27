using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using AutomationPortal.Core.AppControl;
using AutomationPortal.Triggers;

namespace AutomationPortal.App.Services;

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
/// Record and repeat: records the user's clicks and typing in one chosen app as steps that replay through UI Automation.
/// The pauses between steps are never recorded (each step waits for its control instead), so "Ignore waits" has nothing to do.
/// </summary>
public sealed class AppRecorderSource : IMacroRecorderSource, IDisposable
{
    private readonly AppRecorder _recorder;
    public AppRecorderSource(RecordTarget target, bool globalInputDisabled = false) { _recorder = new AppRecorder(target, null, globalInputDisabled); }

    public string StopHotkey => _recorder.StopHotkey;
    public bool DropWaits { get => true; set { } }
    public void Start() => _recorder.Start();
    public void Pause() => _recorder.Pause();
    public void Resume() => _recorder.Resume();
    public IReadOnlyList<ActionInstance> Stop() => _recorder.Stop();
    public IReadOnlyList<string> LeftOut => _recorder.LeftOut;

    public event EventHandler<IReadOnlyList<ActionInstance>>? Stopped
    {
        add => _recorder.Stopped += value;
        remove => _recorder.Stopped -= value;
    }

    public event EventHandler<ActionInstance>? StepRecorded
    {
        add => _recorder.StepRecorded += value;
        remove => _recorder.StepRecorded -= value;
    }

    public void Dispose() => _recorder.Dispose();
}

/// <summary>
/// A recorder that watches nothing and records a fixed set of steps: used by <c>--fake-recorder</c> to try the recording bar, the
/// review and the editor wiring without any real keyboard or mouse capture.
/// </summary>
public sealed class FakeRecorderSource : IMacroRecorderSource
{
    private bool _recording;
    public string StopHotkey => AppRecorder.DefaultStopHotkey;
    public bool DropWaits { get; set; }
    public bool Paused { get; private set; }
    public IReadOnlyList<string> LeftOut { get; private set; } = Array.Empty<string>();
    public void Start() { _recording = true; Paused = false; }
    public void Pause() => Paused = true;
    public void Resume() => Paused = false;

    public IReadOnlyList<ActionInstance> Stop()
    {
        if (!_recording) return Array.Empty<ActionInstance>();
        _recording = false;
        var search = new UiSelector("notepad", "* - Notepad", new[] { new UiNode("Edit", "SearchBox", "Search", "Edit") });
        var save = new UiSelector("notepad", "* - Notepad", new[] { new UiNode("Pane", ClassName: "Toolbar"), new UiNode("Button", "SaveBtn", "Save", "Button") });
        var steps = new List<ActionInstance>
        {
            UiSteps.WaitWindow("notepad", "* - Notepad", "Notepad"),
            UiSteps.Type(search, "Notepad", "Search", Strings.Get("RecorderSources_HelloFromTheFake")),
            UiSteps.Click(save, "Notepad", "Save"),
        };
        LeftOut = new[] { Strings.Get("RecorderSources_PressingEnter") };
        foreach (var s in steps) StepRecorded?.Invoke(this, s);
        Stopped?.Invoke(this, steps);
        return steps;
    }

    public event EventHandler<IReadOnlyList<ActionInstance>>? Stopped;
    public event EventHandler<ActionInstance>? StepRecorded;
}
