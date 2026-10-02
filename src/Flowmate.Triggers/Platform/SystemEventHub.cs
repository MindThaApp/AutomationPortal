using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Flowmate.Triggers.Native;

namespace Flowmate.Triggers.Platform;

/// <summary>
/// One hidden window on its own thread that receives everything the shell broadcasts: hotkeys, lock/unlock, wake,
/// power changes, device and display changes, clipboard changes and foreground-window changes.
/// It is a hidden top-level window rather than a message-only window, because message-only windows never receive
/// broadcast messages (display change, device change, power broadcasts).
/// Events are raised on the hub thread, so handlers must return quickly.
/// </summary>
internal sealed class SystemEventHub : IDisposable
{
    private const int InvokeMessage = Win32.WM_APP + 1;
    private readonly ConcurrentQueue<Action> _work = new();
    private readonly ManualResetEventSlim _ready = new();
    private readonly string _className = "FlowmateEventWindow_" + Guid.NewGuid().ToString("N");
    private Thread? _thread;
    private IntPtr _hwnd;
    private uint _threadId;
    private Win32.WndProc? _wndProc;
    private Win32.WinEventProc? _winEventProc;
    private IntPtr _winEventHook, _suspendResume;
    private readonly List<IntPtr> _powerHandles = new();
    private DateTime _lastResume = DateTime.MinValue;
    private bool _disposed;
    private Exception? _startError;

    public event Action<int>? HotkeyPressed;
    public event Action<bool>? SessionLockChanged;
    public event Action? Resumed;
    public event Action? PowerSettingChanged;
    public event Action? DeviceChanged;
    public event Action? DisplayChanged;
    public event Action? ClipboardChanged;
    public event Action<IntPtr>? ForegroundChanged;

    public bool IsRunning => _thread is { IsAlive: true } && _hwnd != IntPtr.Zero;
    public IntPtr WindowHandle => _hwnd;

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "Flowmate.EventWindow" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
        if (_startError is not null) throw new InvalidOperationException("Flowmate couldn't start listening for system events.", _startError);
    }

    private void Run()
    {
        try
        {
            _threadId = Win32.GetCurrentThreadId();
            _wndProc = WndProc;
            var inst = Win32.GetModuleHandle(null);
            var cls = new Win32.WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEX>(), lpfnWndProc = _wndProc, hInstance = inst, lpszClassName = _className };
            if (Win32.RegisterClassEx(ref cls) == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, WS_POPUP, never shown.
            _hwnd = Win32.CreateWindowEx(0x80 | 0x08000000, _className, "Flowmate events", unchecked((int)0x80000000), 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            Win32.WTSRegisterSessionNotification(_hwnd, 0);
            Win32.AddClipboardFormatListener(_hwnd);
            _suspendResume = Win32.RegisterSuspendResumeNotification(_hwnd, 0);
            foreach (var g in new[] { Win32.GUID_ACDC_POWER_SOURCE, Win32.GUID_BATTERY_PERCENTAGE_REMAINING, Win32.GUID_POWER_SAVING_STATUS })
            {
                var guid = g;
                var h = Win32.RegisterPowerSettingNotification(_hwnd, ref guid, 0);
                if (h != IntPtr.Zero) _powerHandles.Add(h);
            }
            _winEventProc = (_, _, hwnd, idObject, _, _, _) =>
            {
                if (idObject == 0) Raise(() => ForegroundChanged?.Invoke(hwnd));
            };
            _winEventHook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _winEventProc, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
        }
        catch (Exception ex)
        {
            _startError = ex;
            _ready.Set();
            return;
        }
        _ready.Set();

        while (Win32.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }

        try
        {
            if (_winEventHook != IntPtr.Zero) Win32.UnhookWinEvent(_winEventHook);
            foreach (var h in _powerHandles) Win32.UnregisterPowerSettingNotification(h);
            if (_suspendResume != IntPtr.Zero) Win32.UnregisterSuspendResumeNotification(_suspendResume);
            Win32.RemoveClipboardFormatListener(_hwnd);
            Win32.WTSUnRegisterSessionNotification(_hwnd);
            Win32.DestroyWindow(_hwnd);
            Win32.UnregisterClass(_className, Win32.GetModuleHandle(null));
        }
        catch { }
        _hwnd = IntPtr.Zero;
    }

    private static void Raise(Action a)
    {
        try { a(); } catch (Exception ex) { TriggerLog.Write("Event handler failed: " + ex.Message); }
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch ((int)msg)
        {
            case InvokeMessage:
                while (_work.TryDequeue(out var a)) Raise(a);
                return IntPtr.Zero;
            case Win32.WM_HOTKEY:
                { var id = (int)wParam; Raise(() => HotkeyPressed?.Invoke(id)); return IntPtr.Zero; }
            case Win32.WM_WTSSESSION_CHANGE:
                {
                    var code = (int)wParam;
                    if (code == Win32.WTS_SESSION_LOCK) Raise(() => SessionLockChanged?.Invoke(true));
                    else if (code == Win32.WTS_SESSION_UNLOCK) Raise(() => SessionLockChanged?.Invoke(false));
                    return IntPtr.Zero;
                }
            case Win32.WM_POWERBROADCAST:
                {
                    var code = (int)wParam;
                    if (code is Win32.PBT_APMRESUMEAUTOMATIC or Win32.PBT_APMRESUMESUSPEND)
                    {
                        if (DateTime.UtcNow - _lastResume > TimeSpan.FromSeconds(10))
                        {
                            _lastResume = DateTime.UtcNow;
                            Raise(() => Resumed?.Invoke());
                        }
                    }
                    else if (code == Win32.PBT_POWERSETTINGCHANGE) Raise(() => PowerSettingChanged?.Invoke());
                    return (IntPtr)1;
                }
            case Win32.WM_DEVICECHANGE:
                Raise(() => DeviceChanged?.Invoke());
                return (IntPtr)1;
            case Win32.WM_DISPLAYCHANGE:
                Raise(() => DisplayChanged?.Invoke());
                return IntPtr.Zero;
            case Win32.WM_CLIPBOARDUPDATE:
                Raise(() => ClipboardChanged?.Invoke());
                return IntPtr.Zero;
            case Win32.WM_CLOSE:
                Win32.PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return Win32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    /// <summary>Runs <paramref name="func"/> on the hub thread (needed for RegisterHotKey) and returns its result.</summary>
    public T Invoke<T>(Func<T> func)
    {
        if (!IsRunning) throw new InvalidOperationException("Flowmate's keyboard and system listener isn't running.");
        if (Win32.GetCurrentThreadId() == _threadId) return func();
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Enqueue(() =>
        {
            try { tcs.SetResult(func()); } catch (Exception ex) { tcs.SetException(ex); }
        });
        Win32.PostMessage(_hwnd, (uint)InvokeMessage, IntPtr.Zero, IntPtr.Zero);
        if (!tcs.Task.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Flowmate's keyboard listener didn't respond.");
        return tcs.Task.GetAwaiter().GetResult();
    }

    public void Post(Action action)
    {
        if (!IsRunning) return;
        _work.Enqueue(action);
        Win32.PostMessage(_hwnd, (uint)InvokeMessage, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_thread is null) return;
        if (_hwnd != IntPtr.Zero) Win32.PostMessage(_hwnd, (uint)Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        else if (_threadId != 0) Win32.PostThreadMessage(_threadId, 0x0012, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(3));
    }
}
