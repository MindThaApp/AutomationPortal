using AutomationPortal.Interop;
using AutomationPortal.Triggers.Native;

namespace AutomationPortal.Triggers;

/// <summary>
/// Watches what the user types (low-level keyboard hook on its own thread) and calls a handler when a registered phrase is
/// completed. The hook only exists while at least one phrase is registered. Injected keystrokes (including Automation Portal's own)
/// are ignored, nothing is recorded to disk, and nothing happens in password fields, in Automation Portal's own windows or while paused.
/// </summary>
public sealed class TypedPhraseService : IDisposable
{
    private const int PasteThreshold = 160;
    private readonly Func<bool> _isPaused;
    private readonly object _gate = new();
    private readonly TypingEngine _engine = new();
    private readonly List<Registration> _registrations = new();
    private readonly SemaphoreSlim _busy = new(1, 1);
    private HookRunner? _runner;
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private volatile bool _disposed;
    private IReadOnlyList<PhraseDef> _defs = Array.Empty<PhraseDef>();

    // One per hook thread: its own thread id and handles, so stopping an old thread can never signal (or clear the state of) a newer one.
    private sealed class HookRunner
    {
        public Thread? Thread;
        public uint ThreadId;
        public IntPtr KeyHook, MouseHook, FocusHook;
        public Win32.HookProc? KeyProc, MouseProc;
        public Win32.WinEventProc? FocusProc;
        public readonly ManualResetEventSlim Ready = new();
        public volatile bool Installed;
    }

    private sealed class Registration
    {
        public required PhraseDef Def { get; init; }
        public required Func<PhraseMatch, Task> Handler { get; init; }
    }

    /// <param name="isPaused">True while automations are paused (typed phrases and text expansion are switched off).</param>
    /// <param name="globalInputDisabled">True for test harnesses: no keyboard hook is ever installed.</param>
    public TypedPhraseService(Func<bool>? isPaused = null, bool globalInputDisabled = false)
    {
        _isPaused = isPaused ?? (() => false);
        _disabled = globalInputDisabled;
    }

    private readonly bool _disabled;

    public bool IsHookInstalled { get { lock (_gate) return _runner is { Installed: true }; } }

    /// <summary>Calls <paramref name="handler"/> (on a worker thread) whenever <paramref name="phrase"/> is typed. Dispose to stop.</summary>
    public IDisposable Register(string phrase, bool matchCase, Func<PhraseMatch, Task> handler)
    {
        if (string.IsNullOrEmpty(phrase)) throw new ArgumentException("Type a phrase first.", nameof(phrase));
        if (_disabled) return new DisposableAction(() => { });
        var reg = new Registration { Def = new PhraseDef(phrase, matchCase, new object()), Handler = handler };
        lock (_gate)
        {
            if (_disposed) return new DisposableAction(() => { });
            _registrations.Add(reg);
            Republish();
            EnsureHook();
        }
        return new DisposableAction(() =>
        {
            bool stop;
            lock (_gate)
            {
                _registrations.Remove(reg);
                Republish();
                stop = _registrations.Count == 0;
            }
            if (stop) StopHook();
        });
    }

    /// <summary>Replaces all phrases of one owner in one go (used by snippets so editing the list doesn't flicker the hook).</summary>
    internal IDisposable RegisterMany(IEnumerable<(string Phrase, bool MatchCase, Func<PhraseMatch, Task> Handler)> items)
    {
        var regs = items.Where(i => !string.IsNullOrEmpty(i.Phrase))
            .Select(i => new Registration { Def = new PhraseDef(i.Phrase, i.MatchCase, new object()), Handler = i.Handler }).ToList();
        lock (_gate)
        {
            if (_disposed || regs.Count == 0) return new DisposableAction(() => { });
            _registrations.AddRange(regs);
            Republish();
            EnsureHook();
        }
        return new DisposableAction(() =>
        {
            bool stop;
            lock (_gate)
            {
                foreach (var r in regs) _registrations.Remove(r);
                Republish();
                stop = _registrations.Count == 0;
            }
            if (stop) StopHook();
        });
    }

    private void Republish()
    {
        _defs = _registrations.Select(r => r.Def).ToList();
        _engine.SetPhrases(_defs);
    }

    // ---- hook thread -------------------------------------------------------------------------

    private void EnsureHook()
    {
        if (_runner is not null) return;
        var runner = new HookRunner();
        runner.Thread = new Thread(() => HookThread(runner)) { IsBackground = true, Name = "AutomationPortal.KeyboardHook" };
        _runner = runner;
        runner.Thread.Start();
        runner.Ready.Wait(TimeSpan.FromSeconds(3));
    }

    // Never call while holding _gate: the hook callback takes _gate, and the hook thread has to finish to exit.
    private void StopHook()
    {
        HookRunner? runner;
        lock (_gate)
        {
            if (_registrations.Count > 0 && !_disposed) return;
            runner = _runner;
            _runner = null;
        }
        if (runner is null) return;
        runner.Installed = false;
        runner.Ready.Wait(TimeSpan.FromSeconds(3));
        if (runner.ThreadId != 0) Win32.PostThreadMessage(runner.ThreadId, 0x0012, IntPtr.Zero, IntPtr.Zero);
        runner.Thread?.Join(TimeSpan.FromSeconds(2));
    }

    private void HookThread(HookRunner r)
    {
        r.ThreadId = Win32.GetCurrentThreadId();
        r.KeyProc = (code, w, l) => HookCallback(r, code, w, l);
        r.MouseProc = (code, w, l) => MouseCallback(r, code, w, l);
        r.FocusProc = (_, _, _, idObject, _, _, _) => { if (idObject == 0) ResetContext(invalidatePending: false); };
        var module = Win32.GetModuleHandle(null);
        r.KeyHook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, r.KeyProc, module, 0);
        r.Installed = r.KeyHook != IntPtr.Zero;
        if (!r.Installed) TriggerLog.Write("Couldn't install the keyboard hook (error " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ").");
        else
        {
            // Clicks and focus moves inside one window don't change the foreground window, but they do move the caret:
            // whatever was typed before no longer sits in front of it.
            r.MouseHook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, r.MouseProc, module, 0);
            r.FocusHook = Win32.SetWinEventHook(Win32.EVENT_OBJECT_FOCUS, Win32.EVENT_OBJECT_FOCUS, IntPtr.Zero, r.FocusProc, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
        }
        r.Ready.Set();
        if (!r.Installed) return;
        while (Win32.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }
        if (r.FocusHook != IntPtr.Zero) Win32.UnhookWinEvent(r.FocusHook);
        if (r.MouseHook != IntPtr.Zero) Win32.UnhookWindowsHookEx(r.MouseHook);
        Win32.UnhookWindowsHookEx(r.KeyHook);
        r.Installed = false;
    }

    private void ResetContext(bool invalidatePending = true)
    {
        try { lock (_gate) _engine.ResetContext(invalidatePending); }
        catch (Exception ex) { TriggerLog.Write("Typing context reset failed: " + ex.Message); }
    }

    private IntPtr MouseCallback(HookRunner r, int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (int)wParam is Win32.WM_LBUTTONDOWN or Win32.WM_RBUTTONDOWN or Win32.WM_MBUTTONDOWN or 0x020B)
        {
            // A click in one of Automation Portal's own windows (a fill-in form) must not cancel the expansion that opened it.
            var own = false;
            try
            {
                var ms = System.Runtime.InteropServices.Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
                Win32.GetWindowThreadProcessId(Win32.GetAncestor(Win32.WindowFromPoint(ms.pt), 2), out var pid);
                own = pid == _ownPid;
            }
            catch { }
            ResetContext(invalidatePending: !own);
        }
        return Win32.CallNextHookEx(r.MouseHook, code, wParam, lParam);
    }

    private IntPtr HookCallback(HookRunner r, int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            try
            {
                var msg = (int)wParam;
                if (msg is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN)
                {
                    var kb = System.Runtime.InteropServices.Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
                    if ((kb.flags & Win32.LLKHF_INJECTED) == 0 && kb.vkCode != Win32.VK_PACKET && HandleKey(kb))
                        return (IntPtr)1;
                }
            }
            catch (Exception ex) { TriggerLog.Write("Keyboard hook error: " + ex.Message); }
        }
        return Win32.CallNextHookEx(r.KeyHook, code, wParam, lParam);
    }
    /// <summary>Returns true to swallow the key (only for the Backspace that undoes an expansion).</summary>
    private bool HandleKey(Win32.KBDLLHOOKSTRUCT kb)
    {
        if (_isPaused()) return false;
        var fg = Win32.GetForegroundWindow();
        Win32.GetWindowThreadProcessId(fg, out var pid);
        if (pid == _ownPid) return false;

        var vk = (int)kb.vkCode;
        var input = Classify(kb, fg);
        TypingResult result;
        Registration? reg = null;
        lock (_gate)
        {
            result = _engine.Process(input, fg);
            if (result.Match is { } m) reg = _registrations.FirstOrDefault(r => r.Def.Phrase == m.Phrase);
        }

        if (result.Undo is { } undo)
        {
            _ = Task.Run(() => UndoAsync(undo, vk));
            return true;
        }
        if (result.Match is { } match && reg is not null)
            _ = Task.Run(() => RunMatchAsync(match, reg.Handler, vk));
        return false;
    }

    private static KeyInput Classify(Win32.KBDLLHOOKSTRUCT kb, IntPtr fg)
    {
        var vk = (int)kb.vkCode;
        if (HotkeyParser.IsModifierKey(vk) || vk is 0x14 or 0x90 or 0x91) return new KeyInput(KeyKind.Ignore);
        if (vk == Win32.VK_BACK) return new KeyInput(KeyKind.Backspace);
        if (vk is Win32.VK_RETURN or Win32.VK_TAB or Win32.VK_ESCAPE or (>= 0x21 and <= 0x28) or 0x2D or 0x2E or (>= 0x70 and <= 0x87))
            return new KeyInput(KeyKind.Reset);
        var text = KeyTranslator.ToText(kb.vkCode, kb.scanCode, fg);
        return text.Length == 0 ? new KeyInput(KeyKind.Reset) : new KeyInput(KeyKind.Text, text);
    }

    // ---- reacting ----------------------------------------------------------------------------

    private async Task RunMatchAsync(PhraseMatch match, Func<PhraseMatch, Task> handler, int triggerKey)
    {
        if (!await _busy.WaitAsync(0)) return;
        try
        {
            InputSender.WaitForKeyReleased(triggerKey);
            await Task.Delay(40);
            if (_isPaused() || IsPasswordFieldFocused()) return;
            await handler(match);
        }
        catch (Exception ex) { TriggerLog.Write("Typed phrase handler failed: " + ex.Message); }
        finally
        {
            lock (_gate) _engine.ClearBuffer();
            _busy.Release();
        }
    }

    private async Task UndoAsync(UndoInfo undo, int backspaceKey)
    {
        if (!await _busy.WaitAsync(TimeSpan.FromSeconds(2))) return;
        try
        {
            InputSender.WaitForKeyReleased(backspaceKey);
            if (!InputSender.WaitForModifiersReleased(800)) return;
            if (undo.CursorBack > 0) InputSender.PressKey(Win32.VK_RIGHT, undo.CursorBack);
            InputSender.PressKey(Win32.VK_BACK, undo.ExpandedLength);
            InputSender.TypeText(undo.Typed);
        }
        catch (Exception ex) { TriggerLog.Write("Undo of text expansion failed: " + ex.Message); }
        finally { _busy.Release(); }
    }

    /// <summary>
    /// Deletes the phrase the user just typed and types <paramref name="text"/> in its place, then moves the caret
    /// <paramref name="cursorBack"/> characters left. Refuses (returns false) when the user typed or switched windows in the meantime.
    /// Long text is pasted through the clipboard, which is restored afterwards. Call from a handler passed to <see cref="Register"/>.
    /// </summary>
    public async Task<bool> ReplaceTypedAsync(PhraseMatch match, string text, int cursorBack = 0)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (!await EnsureWindowAsync(match.Window)) return false;
        lock (_gate)
        {
            if (_engine.Generation != match.Generation) return false;
        }
        if (!InputSender.WaitForModifiersReleased(1000)) return false;

        InputSender.PressKey(Win32.VK_BACK, match.Typed.Length);
        if (text.Length > 0)
        {
            if (text.Length > PasteThreshold && TriggerClipboard.Backend is not null) await PasteAsync(text);
            else InputSender.TypeText(text);
        }
        if (cursorBack > 0) InputSender.PressKey(Win32.VK_LEFT, cursorBack);
        lock (_gate) _engine.NoteExpansion(match.Typed, text.Length, cursorBack, match.Window);
        return true;
    }

    private static async Task PasteAsync(string text)
    {
        var previous = await ClipboardAccess.SetTextTemporarilyAsync(text);
        await Task.Delay(60);
        InputSender.PressChord(new HotkeyChord(HotkeyModifiers.Ctrl, Win32.VK_V));
        await Task.Delay(450);
        await ClipboardAccess.RestoreAsync(previous);
    }

    /// <summary>Brings a window back to the front after a dialog (like a fill-in form) closed. False if it didn't come back.</summary>
    internal static async Task<bool> EnsureWindowAsync(IntPtr window)
    {
        for (var i = 0; i < 8; i++)
        {
            if (Win32.GetForegroundWindow() == window) return true;
            if (i == 1) Win32.SetForegroundWindow(window);
            await Task.Delay(80);
        }
        return Win32.GetForegroundWindow() == window;
    }

    /// <summary>True when the focused control is a password box (UI Automation). Also true when Windows doesn't answer in time, to stay safe.</summary>
    public static bool IsPasswordFieldFocused()
    {
        try
        {
            var task = Task.Run(Uia.FocusedIsPassword);
            return !task.Wait(500) || task.Result;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _registrations.Clear();
            Republish();
        }
        StopHook();
    }
}
