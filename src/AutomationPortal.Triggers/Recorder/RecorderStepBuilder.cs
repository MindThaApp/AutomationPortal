using System.Globalization;
using System.Text;
using AutomationPortal.Core;

namespace AutomationPortal.Triggers;

/// <summary>The window an event happened in (as seen at the moment of the event).</summary>
public sealed record RecordedWindow(long Handle, string AppId, string AppName, int Left, int Top, int Width, int Height);

public abstract record RecordedEvent(DateTime At, RecordedWindow? Window);

/// <summary>A key pressed. <paramref name="Text"/> is the character it typed ("" for Enter, arrows, Ctrl combinations …). <paramref name="Secret"/>: typed into a password box, so what was typed is not kept.</summary>
public sealed record RecordedKey(DateTime At, RecordedWindow? Window, int VirtualKey, HotkeyModifiers Modifiers, string Text, bool Secret = false) : RecordedEvent(At, Window);

/// <summary>A mouse click at a screen position. <paramref name="Button"/> is "left", "right" or "middle".</summary>
public sealed record RecordedClick(DateTime At, RecordedWindow? Window, int ScreenX, int ScreenY, string Button) : RecordedEvent(At, Window);

public sealed class RecorderOptions
{
    /// <summary>Leave out the pauses between steps.</summary>
    public bool DropWaits { get; set; }
    /// <summary>Pauses longer than this become a Wait step.</summary>
    public TimeSpan WaitThreshold { get; set; } = TimeSpan.FromSeconds(0.5);
    /// <summary>While typing, a pause shorter than this does not split the text into separate steps.</summary>
    public TimeSpan TypingPause { get; set; } = TimeSpan.FromSeconds(2);
    /// <summary>Two clicks closer than this (and within <see cref="DoubleClickPixels"/>) are one double or triple click.</summary>
    public TimeSpan DoubleClickTime { get; set; } = TimeSpan.FromMilliseconds(500);
    public int DoubleClickPixels { get; set; } = 4;
    /// <summary>Milliseconds between typed characters on playback.</summary>
    public int TypeDelayMs { get; set; } = 20;
}

/// <summary>
/// Turns recorded events into Automation Portal steps (the contract the Keyboard &amp; Mouse actions play back):
/// <c>input.click</c> (x, y, relativeTo = window|screen, app, button, clicks), <c>input.keys</c> (keys, times),
/// <c>input.type</c> (text, delayMs), <c>flow.wait</c> (seconds) and <c>app.switch</c> (app).
/// Pure logic, unit-tested with a fake event stream.
/// </summary>
public sealed class RecorderStepBuilder
{
    private readonly RecorderOptions _o;
    private readonly List<ActionInstance> _steps = new();
    private DateTime? _lastAt;
    private string? _currentApp;

    private StringBuilder? _typing;
    private bool _secret;
    private ClickState? _click;
    private KeyState? _keys;

    private sealed class ClickState
    {
        public required RecordedClick First { get; init; }
        public int Count { get; set; } = 1;
    }

    private sealed class KeyState
    {
        public required string Keys { get; init; }
        public int Count { get; set; } = 1;
    }

    public RecorderStepBuilder(RecorderOptions? options = null) { _o = options ?? new RecorderOptions(); }

    /// <summary>The options in use. Changing <see cref="RecorderOptions.DropWaits"/> mid-recording applies to what comes next.</summary>
    public RecorderOptions Options => _o;

    /// <summary>Recording was paused: finish what is in progress, and don't count the time away as a wait.</summary>
    public void Pause()
    {
        FlushPending();
        _lastAt = null;
    }

    /// <summary>Raised when a step is finished (typing is finished when something else happens).</summary>
    public event Action<ActionInstance>? StepRecorded;

    /// <summary>Everything recorded so far, including the step still being built.</summary>
    public IReadOnlyList<ActionInstance> Steps
    {
        get
        {
            var list = _steps.ToList();
            if (_secret) list.AddRange(BuildSecret());
            if (_typing is not null) list.Add(BuildType());
            if (_click is not null) list.Add(BuildClick());
            if (_keys is not null) list.Add(BuildKeys());
            return list;
        }
    }

    public IReadOnlyList<ActionInstance> Finish()
    {
        FlushPending();
        return _steps.ToList();
    }

    public void Add(RecordedEvent ev)
    {
        var gap = _lastAt is { } last ? (ev.At - last) : TimeSpan.Zero;
        var sameApp = ev.Window is null || _currentApp is null || ev.Window.AppId == _currentApp;

        switch (ev)
        {
            case RecordedKey { Secret: true }:
                if (_secret && sameApp) { _lastAt = ev.At; return; }
                break;

            case RecordedKey k when IsPrintable(k):
                if (_typing is not null && sameApp && gap <= _o.TypingPause)
                {
                    _typing.Append(k.Text);
                    _lastAt = ev.At;
                    return;
                }
                break;

            case RecordedKey k:
                if (_keys is not null && sameApp && gap <= _o.WaitThreshold && _keys.Keys == KeysText(k))
                {
                    _keys.Count++;
                    _lastAt = ev.At;
                    return;
                }
                break;

            case RecordedClick c:
                if (_click is not null && sameApp && gap <= _o.DoubleClickTime && _click.Count < 3 && _click.First.Button == c.Button
                    && _click.First.Window?.Handle == c.Window?.Handle
                    && Math.Abs(_click.First.ScreenX - c.ScreenX) <= _o.DoubleClickPixels && Math.Abs(_click.First.ScreenY - c.ScreenY) <= _o.DoubleClickPixels)
                {
                    _click.Count++;
                    _lastAt = ev.At;
                    return;
                }
                break;
        }

        FlushPending();
        if (_lastAt is not null && gap > _o.WaitThreshold && !_o.DropWaits)
            Commit(ActionInstance.Create("flow.wait", ("seconds", ParamValue.Duration(Math.Round(gap.TotalSeconds, 1)))));
        if (ev.Window is { } w && w.AppId != _currentApp)
        {
            Commit(ActionInstance.Create("app.switch", ("app", ParamValue.App(w.AppId, w.AppName))));
            _currentApp = w.AppId;
        }

        switch (ev)
        {
            case RecordedKey { Secret: true }: _secret = true; break;
            case RecordedKey k when IsPrintable(k): _typing = new StringBuilder(k.Text); break;
            case RecordedKey k: _keys = new KeyState { Keys = KeysText(k) }; break;
            case RecordedClick c: _click = new ClickState { First = c }; break;
        }
        _lastAt = ev.At;
    }

    /// <summary>Printable text: typed characters, with Shift and AltGr allowed. Ctrl, Alt or Windows shortcuts are key presses instead.</summary>
    internal static bool IsPrintable(RecordedKey k)
    {
        if (k.Text.Length == 0) return false;
        var ctrl = k.Modifiers.HasFlag(HotkeyModifiers.Ctrl);
        var alt = k.Modifiers.HasFlag(HotkeyModifiers.Alt);
        if (k.Modifiers.HasFlag(HotkeyModifiers.Win)) return false;
        return ctrl == alt; // Ctrl+Alt is AltGr
    }

    private static string KeysText(RecordedKey k) => HotkeyParser.Format(k.Modifiers, k.VirtualKey);

    private void FlushPending()
    {
        if (_secret) { foreach (var s in BuildSecret()) Commit(s); _secret = false; }
        if (_typing is not null) { Commit(BuildType()); _typing = null; }
        if (_click is not null) { Commit(BuildClick()); _click = null; }
        if (_keys is not null) { Commit(BuildKeys()); _keys = null; }
    }

    private void Commit(ActionInstance step)
    {
        _steps.Add(step);
        try { StepRecorded?.Invoke(step); } catch { }
    }

    // The password itself is never stored: playback asks for it and types the answer.
    private static IEnumerable<ActionInstance> BuildSecret()
    {
        yield return ActionInstance.Create("ui.ask", ("prompt", ParamValue.Text("Type the password to enter")));
        yield return ActionInstance.Create("input.type");
    }

    private ActionInstance BuildType() =>
        ActionInstance.Create("input.type", ("text", ParamValue.Text(_typing!.ToString())), ("delayMs", ParamValue.Number(_o.TypeDelayMs)));

    private ActionInstance BuildKeys() =>
        ActionInstance.Create("input.keys", ("keys", ParamValue.Text(_keys!.Keys)), ("times", ParamValue.Number(_keys.Count)));

    private ActionInstance BuildClick()
    {
        var c = _click!.First;
        var step = ActionInstance.Create("input.click");
        if (c.Window is { } w)
        {
            step.Parameters["x"] = ParamValue.Number(c.ScreenX - w.Left);
            step.Parameters["y"] = ParamValue.Number(c.ScreenY - w.Top);
            step.Parameters["relativeTo"] = ParamValue.Choice("window");
            step.Parameters["app"] = ParamValue.App(w.AppId, w.AppName);
        }
        else
        {
            step.Parameters["x"] = ParamValue.Number(c.ScreenX);
            step.Parameters["y"] = ParamValue.Number(c.ScreenY);
            step.Parameters["relativeTo"] = ParamValue.Choice("screen");
        }
        step.Parameters["button"] = ParamValue.Choice(c.Button);
        step.Parameters["clicks"] = ParamValue.Number(_click.Count);
        return step;
    }

    public static string SecondsText(double s) => s.ToString("0.#", CultureInfo.InvariantCulture);
}
