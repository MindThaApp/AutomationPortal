using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using AutomationPortal.Core;
using AutomationPortal.Core.AppControl;
using AutomationPortal.Interop;
using AutomationPortal.Triggers.Native;

namespace AutomationPortal.Triggers;

/// <summary>The app chosen before recording: only its windows are recorded.</summary>
public sealed record RecordTarget(int ProcessId, string AppName, string WindowTitle);

/// <summary>
/// Records what the user does in one app as steps that repeat it through UI Automation ("Click 'Save' in Notepad").
/// Low-level keyboard and mouse hooks are used only to notice the user's own clicks and key presses while a recording the user
/// started is on; they never change or send input (the stop shortcut is the one key they hold back). Each click or key is tied
/// to the control involved through UI Automation, off the hook thread; screen positions are never stored. Typing into a
/// password box is never kept. Stop with <see cref="Stop"/> or the stop shortcut (Ctrl+Alt+S).
/// </summary>
public sealed class AppRecorder : IDisposable
{
    public const string DefaultStopHotkey = "Ctrl+Alt+S";

    private abstract record Raw;
    private sealed record Down(long Seq, int X, int Y) : Raw;
    private sealed record Up(long Seq, bool IsClick) : Raw;
    private sealed record OtherButton(string Name) : Raw;
    private sealed record Key(int Vk, HotkeyModifiers Mods, string Text) : Raw;
    private sealed record PauseMark : Raw;
    private sealed record EndMark : Raw;

    private readonly RecordTarget _target;
    private readonly Func<IUiDesktop> _desktopFactory;
    private readonly object _gate = new();
    private readonly BlockingCollection<Raw> _queue = new();
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private readonly HotkeyChord _stopChord = HotkeyParser.Parse(DefaultStopHotkey);
    private Thread? _hookThread, _worker;
    private uint _hookThreadId;
    private IntPtr _keyHook, _mouseHook;
    private Win32.HookProc? _keyProc, _mouseProc;
    private UiRecordingBuilder? _builder;
    private IReadOnlyList<ActionInstance> _last = Array.Empty<ActionInstance>();
    private IReadOnlyList<string> _lastLeftOut = Array.Empty<string>();
    private (long Seq, int X, int Y)? _down;
    private long _seq;
    private volatile bool _recording, _paused;
    private TaskCompletionSource<IReadOnlyList<ActionInstance>>? _stopDone;

    public AppRecorder(RecordTarget target, Func<IUiDesktop>? desktop = null)
    {
        _target = target;
        _desktopFactory = desktop ?? (() => new UiaDesktop());
    }

    public RecordTarget Target => _target;
    public string StopHotkey => DefaultStopHotkey;
    public bool IsRecording => _recording;
    public bool IsPaused => _recording && _paused;

    /// <summary>A step was finished (may be raised on the recorder's worker thread).</summary>
    public event EventHandler<ActionInstance>? StepRecorded;
    /// <summary>Recording ended, by <see cref="Stop"/> or the stop shortcut. Carries the steps.</summary>
    public event EventHandler<IReadOnlyList<ActionInstance>>? Stopped;

    /// <summary>What the user did that can't be repeated ("Pressing Enter").</summary>
    public IReadOnlyList<string> LeftOut { get { lock (_gate) return (_builder?.LeftOut ?? _lastLeftOut).Concat(_extraLeftOut).Distinct().ToList(); } }

    public void Start()
    {
        lock (_gate)
        {
            if (_recording) return;
            _last = Array.Empty<ActionInstance>();
            _lastLeftOut = Array.Empty<string>();
            _extraLeftOut.Clear();
            while (_queue.TryTake(out _)) { }
            _down = null;
            _paused = false;
            _recording = true;
            _stopDone = new TaskCompletionSource<IReadOnlyList<ActionInstance>>(TaskCreationOptions.RunContinuationsAsynchronously);

            // The worker makes the UI Automation client on its own MTA thread and keeps using it there. It hands the builder back
            // through a local (not under _gate, which this method holds while it waits).
            var workerReady = new ManualResetEventSlim();
            Exception? workerError = null;
            UiRecordingBuilder? made = null;
            _worker = new Thread(() =>
            {
                IUiDesktop? desktop = null;
                try
                {
                    desktop = _desktopFactory();
                    var builder = new UiRecordingBuilder(desktop, _target.AppName);
                    builder.StepRecorded += step => StepRecorded?.Invoke(this, step);
                    made = builder;
                }
                catch (Exception ex) { workerError = ex; }
                finally { workerReady.Set(); }
                if (workerError is null && desktop is not null) Work(desktop);
            }) { IsBackground = true, Name = "AutomationPortal.AppRecorder.Worker" };
            _worker.SetApartmentState(ApartmentState.MTA);
            _worker.Start();
            workerReady.Wait(TimeSpan.FromSeconds(5));
            _builder = made;
            if (_builder is null)
            {
                _recording = false;
                _queue.Add(new EndMark());
                _stopDone.TrySetResult(Array.Empty<ActionInstance>());
                throw new InvalidOperationException("Windows wouldn't let Automation Portal read the app's controls. Try again." +
                    (workerError is null ? "" : " (" + workerError.Message + ")"));
            }

            var hooksReady = new ManualResetEventSlim();
            _hookThread = new Thread(() => HookThread(hooksReady)) { IsBackground = true, Name = "AutomationPortal.AppRecorder.Hooks" };
            _hookThread.Start();
            hooksReady.Wait(TimeSpan.FromSeconds(3));
            if (_keyHook == IntPtr.Zero && _mouseHook == IntPtr.Zero)
            {
                _recording = false;
                _queue.Add(new EndMark());
                _stopDone.TrySetResult(Array.Empty<ActionInstance>());
                _builder = null;
                throw new InvalidOperationException("Windows wouldn't let Automation Portal notice your clicks and typing. Try again.");
            }
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
        _queue.Add(new PauseMark());
    }

    public void Resume()
    {
        lock (_gate) { if (_recording) _paused = false; }
    }

    /// <summary>Stops recording and returns the steps (the same list again if it was already stopped).</summary>
    public IReadOnlyList<ActionInstance> Stop()
    {
        Thread? hooks, worker;
        Task<IReadOnlyList<ActionInstance>>? stopping = null;
        lock (_gate)
        {
            if (!_recording) stopping = _stopDone?.Task;
        }
        // Stop pressed twice at once (the shortcut and the bar's button): the second waits for the first, so neither loses the steps.
        if (stopping is not null) return stopping.Wait(TimeSpan.FromSeconds(10)) ? stopping.Result : _last;
        lock (_gate)
        {
            if (!_recording) return _last;
            _recording = false;
            _paused = false;
            hooks = _hookThread; worker = _worker;
            _hookThread = _worker = null;
        }
        // WM_QUIT to the recorder's own hook thread ends its message loop. This is a thread message, not input.
        if (_hookThreadId != 0) Win32.PostThreadMessage(_hookThreadId, 0x0012, IntPtr.Zero, IntPtr.Zero);
        hooks?.Join(TimeSpan.FromSeconds(2));
        _queue.Add(new EndMark());
        worker?.Join(TimeSpan.FromSeconds(5));

        IReadOnlyList<ActionInstance> steps;
        lock (_gate)
        {
            steps = _builder?.Finish() ?? Array.Empty<ActionInstance>();
            _lastLeftOut = _builder?.LeftOut ?? Array.Empty<string>();
            _builder = null;
            _last = steps;
        }
        _stopDone?.TrySetResult(steps);
        Stopped?.Invoke(this, steps);
        return steps;
    }

    // ---- hooks: notice only, never change or send input -------------------------------------------

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

    private bool InTarget(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        return pid != _ownPid && pid == (uint)_target.ProcessId;
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
                    var vk = (int)kb.vkCode;
                    if ((kb.flags & Win32.LLKHF_INJECTED) == 0 && kb.vkCode != Win32.VK_PACKET && !HotkeyParser.IsModifierKey(vk))
                    {
                        var mods = CurrentModifiers();
                        if (vk == _stopChord.VirtualKey && mods == _stopChord.Modifiers)
                        {
                            _ = Task.Run(() => Stop());
                            return (IntPtr)1; // the stop shortcut is held back so the app doesn't see it
                        }
                        var fg = Win32.GetForegroundWindow();
                        if (!_paused && InTarget(fg))
                            _queue.Add(new Key(vk, mods, KeyTranslator.ToText(kb.vkCode, kb.scanCode, fg)));
                    }
                }
            }
            catch (Exception ex) { TriggerLog.Write("App recorder keyboard hook: " + ex.Message); }
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
                var ms = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
                if ((ms.flags & Win32.LLMHF_INJECTED) == 0)
                {
                    switch (msg)
                    {
                        case Win32.WM_LBUTTONDOWN:
                            if (InTarget(Win32.GetAncestor(Win32.WindowFromPoint(ms.pt), 2)))
                            {
                                var seq = Interlocked.Increment(ref _seq);
                                _down = (seq, ms.pt.X, ms.pt.Y);
                                // The control is looked up at once (on the worker), before the click changes the window.
                                _queue.Add(new Down(seq, ms.pt.X, ms.pt.Y));
                            }
                            else _down = null;
                            break;
                        case Win32.WM_LBUTTONUP when _down is { } d:
                            _down = null;
                            // A press that moved far is a drag, which can't be repeated through a control: leave it out.
                            _queue.Add(new Up(d.Seq, Math.Abs(ms.pt.X - d.X) <= 6 && Math.Abs(ms.pt.Y - d.Y) <= 6));
                            break;
                        case Win32.WM_RBUTTONDOWN or Win32.WM_MBUTTONDOWN:
                            if (InTarget(Win32.GetAncestor(Win32.WindowFromPoint(ms.pt), 2)))
                                _queue.Add(new OtherButton(msg == Win32.WM_RBUTTONDOWN ? "A right-click" : "A middle-click"));
                            break;
                    }
                }
            }
            catch (Exception ex) { TriggerLog.Write("App recorder mouse hook: " + ex.Message); }
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

    // ---- worker: ties each click or key to its control through UI Automation -----------------------

    private void Work(IUiDesktop desktop)
    {
        (long Seq, IUiElement? Element)? pressed = null;
        foreach (var raw in _queue.GetConsumingEnumerable())
        {
            if (raw is EndMark) break;
            try
            {
                switch (raw)
                {
                    case PauseMark:
                        lock (_gate) _builder?.Pause();
                        pressed = null;
                        break;
                    case Down d:
                        pressed = (d.Seq, desktop.ElementAt(d.X, d.Y));
                        break;
                    case Up u:
                        if (pressed is { } p && p.Seq == u.Seq && u.IsClick) lock (_gate) _builder?.Click(p.Element);
                        else if (!u.IsClick) Note("Dragging with the mouse");
                        pressed = null;
                        break;
                    case OtherButton o:
                        Note(o.Name);
                        break;
                    case Key k:
                        var focused = desktop.Focused();
                        if (focused is not null && UiSelectorBuilder.WindowOf(focused).ProcessId != _target.ProcessId && focused.ProcessId != _target.ProcessId)
                            break;
                        var printable = k.Text.Length > 0 && k.Text.All(c => !char.IsControl(c));
                        lock (_gate)
                            _builder?.Key(focused, printable ? k.Text : "", HotkeyParser.Format(k.Mods, k.Vk),
                                isBackspace: k.Vk == Win32.VK_BACK && k.Mods == HotkeyModifiers.None, isTab: k.Vk == Win32.VK_TAB);
                        break;
                }
            }
            catch (Exception ex) { TriggerLog.Write("App recorder couldn't read a control: " + ex.Message); }
        }
    }

    private void Note(string what)
    {
        lock (_gate) _builder?.Key(null, "", "");
        lock (_gate) _extraLeftOut.Add(what);
    }

    private readonly HashSet<string> _extraLeftOut = new();


    public void Dispose()
    {
        if (_recording) Stop();
        _queue.Dispose();
    }
}
