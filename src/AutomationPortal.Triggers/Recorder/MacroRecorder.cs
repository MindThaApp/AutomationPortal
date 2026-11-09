using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Flowmate.Core;
using Flowmate.Triggers.Native;

namespace Flowmate.Triggers;

/// <summary>
/// Records what the user does (clicks, typing, key presses, pauses) into Flowmate steps while recording is on.
/// Clicks are stored relative to the window they landed in, and an "app.switch" step is added whenever the app changes,
/// so the recording still works when windows have moved. Keystrokes are only kept in memory until <see cref="Stop"/>.
/// Stop with <see cref="Stop"/> or the stop shortcut (default Ctrl+Alt+Esc, which is swallowed so the app doesn't see it).
/// </summary>
public sealed class MacroRecorder : IDisposable
{
    public const string DefaultStopHotkey = "Ctrl+Alt+Esc";

    private sealed record RawEvent(bool IsClick, DateTime At, IntPtr Hwnd, uint Pid, Win32.RECT Rect, int Vk, uint Scan, HotkeyModifiers Mods, string Text, int X, int Y, string Button);

    private readonly object _gate = new();
    private readonly BlockingCollection<RawEvent> _queue = new();
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private Thread? _hookThread, _worker;
    private uint _hookThreadId;
    private IntPtr _keyHook, _mouseHook;
    private Win32.HookProc? _keyProc, _mouseProc;
    private RecorderStepBuilder? _builder;
    private IReadOnlyList<ActionInstance> _last = Array.Empty<ActionInstance>();
    private HotkeyChord _stopChord = HotkeyParser.Parse(DefaultStopHotkey);
    private (int X, int Y, DateTime At, IntPtr Hwnd, uint Pid, Win32.RECT Rect, string Button)? _down;
    private volatile bool _recording;
    private volatile bool _paused;
    private bool _dropWaits;

    public bool IsRecording => _recording;
    /// <summary>True while paused: clicks and keys are ignored (the stop shortcut still works) and the pause isn't recorded as a wait.</summary>
    public bool IsPaused => _recording && _paused;

    /// <summary>Shortcut that stops recording, in the form "Ctrl+Alt+Esc". Takes effect for the next recording.</summary>
    public string StopHotkey
    {
        get => HotkeyParser.Format(_stopChord);
        set => _stopChord = HotkeyParser.TryParse(value, out var c) ? c : throw new ArgumentException("That isn't a keyboard shortcut Flowmate understands. Use a combination like Ctrl+Alt+Esc.");
    }

    /// <summary>Leave pauses out of the recording. Can be changed while recording.</summary>
    public bool DropWaits
    {
        get => _dropWaits;
        set { lock (_gate) { _dropWaits = value; if (_builder is not null) _builder.Options.DropWaits = value; } }
    }

    /// <summary>A step was finished. Typing counts as finished when something else happens.</summary>
    public event EventHandler<ActionInstance>? StepRecorded;
    /// <summary>Recording ended (by <see cref="Stop"/> or by the stop shortcut). Carries the steps.</summary>
    public event EventHandler<IReadOnlyList<ActionInstance>>? Stopped;

    /// <summary>The steps so far, including the one still being built.</summary>
    public IReadOnlyList<ActionInstance> CurrentSteps { get { lock (_gate) return _builder?.Steps ?? _last; } }

    public void Start()
    {
        lock (_gate)
        {
            if (_recording) return;
            _builder = new RecorderStepBuilder(new RecorderOptions { DropWaits = _dropWaits });
            _builder.StepRecorded += step => StepRecorded?.Invoke(this, step);
            _last = Array.Empty<ActionInstance>();
            while (_queue.TryTake(out _)) { }
            _down = null;
            _paused = false;
            _recording = true;

            var ready = new ManualResetEventSlim();
            _hookThread = new Thread(() => HookThread(ready)) { IsBackground = true, Name = "Flowmate.Recorder.Hooks" };
            _hookThread.Start();
            ready.Wait(TimeSpan.FromSeconds(3));
            if (_keyHook == IntPtr.Zero && _mouseHook == IntPtr.Zero)
            {
                _recording = false;
                _builder = null;
                throw new InvalidOperationException("Windows wouldn't let Flow Mate watch the keyboard and mouse. Try again.");
            }
            _worker = new Thread(Work) { IsBackground = true, Name = "Flowmate.Recorder.Worker" };
            _worker.Start();
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (!_recording || _paused) return;
            _paused = true;
            _down = null;
        }
        _queue.Add(new RawEvent(false, DateTime.MinValue, IntPtr.Zero, 0, default, -2, 0, 0, "", 0, 0, ""));
    }

    public void Resume()
    {
        lock (_gate) { if (_recording) _paused = false; }
    }

    /// <summary>Stops recording and returns the recorded steps (the same list again if it was already stopped).</summary>
    public IReadOnlyList<ActionInstance> Stop()
    {
        Thread? hooks, worker;
        lock (_gate)
        {
            if (!_recording) return _last;
            _recording = false;
            _paused = false;
            hooks = _hookThread; worker = _worker;
            _hookThread = _worker = null;
        }
        if (_hookThreadId != 0) Win32.PostThreadMessage(_hookThreadId, 0x0012, IntPtr.Zero, IntPtr.Zero);
        hooks?.Join(TimeSpan.FromSeconds(2));
        _queue.Add(new RawEvent(false, DateTime.MinValue, IntPtr.Zero, 0, default, -1, 0, 0, "", 0, 0, ""));
        worker?.Join(TimeSpan.FromSeconds(3));

        IReadOnlyList<ActionInstance> steps;
        lock (_gate)
        {
            steps = _builder?.Finish() ?? Array.Empty<ActionInstance>();
            _builder = null;
            _last = steps;
        }
        Stopped?.Invoke(this, steps);
        return steps;
    }

    // ---- hooks -------------------------------------------------------------------------------

    private void HookThread(ManualResetEventSlim ready)
    {
        _hookThreadId = Win32.GetCurrentThreadId();
        _keyProc = KeyCallback;
        _mouseProc = MouseCallback;
        var module = Win32.GetModuleHandle(null);
        _keyHook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _keyProc, module, 0);
        _mouseHook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _mouseProc, module, 0);
        ready.Set();
        if (_keyHook == IntPtr.Zero && _mouseHook == IntPtr.Zero) return;
        while (Win32.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }
        if (_keyHook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_keyHook);
        if (_mouseHook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_mouseHook);
        _keyHook = _mouseHook = IntPtr.Zero;
    }

    private IntPtr KeyCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && _recording)
        {
            try
            {
                var msg = (int)wParam;
                if (msg is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN)
                {
                    var kb = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
                    if ((kb.flags & Win32.LLKHF_INJECTED) == 0 && kb.vkCode != Win32.VK_PACKET)
                    {
                        var vk = (int)kb.vkCode;
                        if (!HotkeyParser.IsModifierKey(vk))
                        {
                            var mods = CurrentModifiers();
                            if (vk == _stopChord.VirtualKey && mods == _stopChord.Modifiers)
                            {
                                _ = Task.Run(() => Stop());
                                return (IntPtr)1;
                            }
                            if (_paused) return Win32.CallNextHookEx(_keyHook, code, wParam, lParam);
                            var fg = Win32.GetForegroundWindow();
                            Win32.GetWindowThreadProcessId(fg, out var pid);
                            if (pid != _ownPid)
                            {
                                var text = KeyTranslator.ToText(kb.vkCode, kb.scanCode, fg);
                                var named = !HotkeyParser.KeyName(vk).StartsWith("Key", StringComparison.Ordinal) || text.Length > 0;
                                if (named)
                                {
                                    Win32.GetWindowRect(fg, out var rect);
                                    _queue.Add(new RawEvent(false, DateTime.UtcNow, fg, pid, rect, vk, kb.scanCode, mods, text, 0, 0, ""));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { TriggerLog.Write("Recorder keyboard hook: " + ex.Message); }
        }
        return Win32.CallNextHookEx(_keyHook, code, wParam, lParam);
    }

    private IntPtr MouseCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && _recording && !_paused)
        {
            try
            {
                var msg = (int)wParam;
                var button = msg switch
                {
                    Win32.WM_LBUTTONDOWN or Win32.WM_LBUTTONUP => "left",
                    Win32.WM_RBUTTONDOWN or Win32.WM_RBUTTONUP => "right",
                    Win32.WM_MBUTTONDOWN or Win32.WM_MBUTTONUP => "middle",
                    _ => null,
                };
                if (button is not null)
                {
                    var ms = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
                    if ((ms.flags & Win32.LLMHF_INJECTED) == 0)
                    {
                        var isDown = msg is Win32.WM_LBUTTONDOWN or Win32.WM_RBUTTONDOWN or Win32.WM_MBUTTONDOWN;
                        if (isDown)
                        {
                            var hwnd = Win32.GetAncestor(Win32.WindowFromPoint(ms.pt), 2);
                            Win32.GetWindowThreadProcessId(hwnd, out var pid);
                            Win32.GetWindowRect(hwnd, out var rect);
                            _down = (ms.pt.X, ms.pt.Y, DateTime.UtcNow, hwnd, pid, rect, button);
                        }
                        else if (_down is { } d && d.Button == button)
                        {
                            _down = null;
                            // A press that moved far is a drag, which a simple click step can't describe: leave it out.
                            if (Math.Abs(ms.pt.X - d.X) <= 6 && Math.Abs(ms.pt.Y - d.Y) <= 6 && d.Pid != _ownPid)
                                _queue.Add(new RawEvent(true, DateTime.UtcNow, d.Hwnd, d.Pid, d.Rect, 0, 0, 0, "", d.X, d.Y, button));
                        }
                    }
                }
            }
            catch (Exception ex) { TriggerLog.Write("Recorder mouse hook: " + ex.Message); }
        }
        return Win32.CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private static HotkeyModifiers CurrentModifiers()
    {
        var m = HotkeyModifiers.None;
        if (InputSender.IsDown(Win32.VK_CONTROL)) m |= HotkeyModifiers.Ctrl;
        if (InputSender.IsDown(Win32.VK_MENU)) m |= HotkeyModifiers.Alt;
        if (InputSender.IsDown(Win32.VK_SHIFT)) m |= HotkeyModifiers.Shift;
        if (InputSender.IsDown(Win32.VK_LWIN) || InputSender.IsDown(Win32.VK_RWIN)) m |= HotkeyModifiers.Win;
        return m;
    }

    // ---- worker: looks up the app behind each window off the hook thread ---------------------

    private void Work()
    {
        var apps = new Dictionary<uint, (string Id, string Name)>();
        foreach (var raw in _queue.GetConsumingEnumerable())
        {
            if (raw.Vk == -1 && raw.At == DateTime.MinValue) break;
            if (raw.Vk == -2) { lock (_gate) _builder?.Pause(); continue; }
            try
            {
                RecordedWindow? window = null;
                if (raw.Hwnd != IntPtr.Zero)
                {
                    if (!apps.TryGetValue(raw.Pid, out var app))
                    {
                        var info = AppInfo.FromWindow(raw.Hwnd);
                        app = info is null ? ("", "") : (info.ExePath ?? info.ProcessName, info.DisplayName);
                        apps[raw.Pid] = app;
                    }
                    if (app.Id.Length > 0)
                        window = new RecordedWindow(raw.Hwnd.ToInt64(), app.Id, app.Name, raw.Rect.Left, raw.Rect.Top, raw.Rect.Right - raw.Rect.Left, raw.Rect.Bottom - raw.Rect.Top);
                }
                RecordedEvent ev = raw.IsClick
                    ? new RecordedClick(raw.At, window, raw.X, raw.Y, raw.Button)
                    : IsSecretKey(raw)
                        ? new RecordedKey(raw.At, window, raw.Vk, raw.Mods, "", Secret: true)
                        : new RecordedKey(raw.At, window, raw.Vk, raw.Mods, raw.Text);
                lock (_gate) _builder?.Add(ev);
            }
            catch (Exception ex) { TriggerLog.Write("Recorder couldn't process an event: " + ex.Message); }
        }
    }

    private (IntPtr Hwnd, DateTime At, bool Password) _passwordCache;

    // Typed text (and Backspace) in a password box must never reach the saved shortcut. Checked here, off the hook thread.
    // When Windows doesn't answer in time the box counts as a password box.
    private bool IsSecretKey(RawEvent raw)
    {
        if (raw.Text.Length == 0 && raw.Vk != Win32.VK_BACK) return false;
        if (_passwordCache.Hwnd == raw.Hwnd && DateTime.UtcNow - _passwordCache.At < TimeSpan.FromMilliseconds(300)) return _passwordCache.Password;
        var password = IsPasswordFieldFocused();
        _passwordCache = (raw.Hwnd, DateTime.UtcNow, password);
        return password;
    }

    /// <summary>Overridable for tests.</summary>
    internal Func<bool> PasswordProbe { get; set; } = TypedPhraseService.IsPasswordFieldFocused;
    private bool IsPasswordFieldFocused() => PasswordProbe();

    public void Dispose()
    {
        if (_recording) Stop();
        _queue.Dispose();
    }
}
