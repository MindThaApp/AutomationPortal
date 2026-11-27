using System.Runtime.InteropServices;
using AutomationPortal.Core;
using AutomationPortal.Triggers.Native;
using AutomationPortal.Triggers.Platform;

namespace AutomationPortal.Triggers;

/// <summary>Result of registering a keyboard shortcut. <see cref="Reason"/> is a plain-English message when it failed.</summary>
public sealed record HotkeyResult(bool Success, string? Reason)
{
    public static HotkeyResult Ok { get; } = new(true, null);
}

/// <summary>One registered shortcut as shown to the UI.</summary>
public sealed record HotkeyRegistration(string OwnerId, string Hotkey, string? Label, string? OnlyInApp);

/// <summary>
/// Global keyboard shortcuts through RegisterHotKey. Owners (a shortcut, an automation, Quick Run …) register one
/// shortcut each under a stable owner id. Several owners may share one combination only when each is limited to a
/// different front app; when none of them applies, the keystroke is passed on to the app so it still works there.
/// Callbacks run on the thread pool.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private sealed class Handler
    {
        public required string Owner { get; init; }
        public string? Label { get; init; }
        public required Action Callback { get; init; }
        public AppValue? OnlyInApp { get; init; }
    }

    private sealed class Chord
    {
        public int Id { get; init; }
        public HotkeyChord Key { get; init; }
        public List<Handler> Handlers { get; } = new();
    }

    private readonly SystemEventHub _hub;
    private readonly object _gate = new();
    private readonly Dictionary<int, Chord> _byId = new();
    private readonly Dictionary<HotkeyChord, Chord> _byChord = new();
    private readonly Dictionary<string, Chord> _byOwner = new(StringComparer.OrdinalIgnoreCase);
    private int _nextId = 0x100;
    private volatile bool _disposed;
    // Read by the hub thread without taking _gate (Register waits on the hub thread while holding _gate).
    private volatile Dictionary<int, (HotkeyChord Key, Handler[] Handlers)> _snapshot = new();

    private void Publish() => _snapshot = _byId.ToDictionary(kv => kv.Key, kv => (kv.Value.Key, kv.Value.Handlers.ToArray()));

    private readonly bool _disabled;

    internal HotkeyService(SystemEventHub hub, bool globalInputDisabled = false)
    {
        _hub = hub;
        _disabled = globalInputDisabled;
        _hub.HotkeyPressed += OnHotkey;
    }

    public HotkeyResult Register(string ownerId, string hotkey, Action callback, string? label = null, AppValue? onlyInApp = null)
    {
        if (_disabled) return HotkeyResult.Ok; // global input is off: nothing is registered with Windows
        if (!HotkeyParser.TryParse(hotkey, out var chord))
            return new(false, Strings.Format("Hotkey_Error_NotUnderstood", hotkey));
        if (HotkeyParser.IsReservedEditChord(chord))
            return new(false, Strings.Format("Hotkey_Error_ReservedEdit", HotkeyParser.Format(chord)));
        if (!HotkeyParser.IsSafeGlobal(chord))
            return new(false, Strings.Format("Hotkey_Error_Unsafe", HotkeyParser.Format(chord)));
        var pretty = HotkeyParser.Format(chord);

        lock (_gate)
        {
            if (_disposed) return new(false, Strings.Get("Hotkey_Error_ShuttingDown"));
            UnregisterLocked(ownerId);

            var handler = new Handler { Owner = ownerId, Label = label, Callback = callback, OnlyInApp = onlyInApp };
            if (_byChord.TryGetValue(chord, out var existing))
            {
                var clash = existing.Handlers.FirstOrDefault(h => h.OnlyInApp is null || onlyInApp is null || SameApp(h.OnlyInApp, onlyInApp));
                if (clash is not null)
                    return new(false, Strings.Format("Hotkey_Error_AlreadyUsed", pretty, clash.Label ?? Strings.Get("Hotkey_Label_AnotherFeature")));
                existing.Handlers.Add(handler);
                _byOwner[ownerId] = existing;
                Publish();
                return HotkeyResult.Ok;
            }

            var entry = new Chord { Id = _nextId++, Key = chord };
            entry.Handlers.Add(handler);
            try
            {
                var ok = _hub.Invoke(() =>
                {
                    if (Win32.RegisterHotKey(_hub.WindowHandle, entry.Id, HotkeyParser.ToNativeModifiers(chord.Modifiers) | Win32.MOD_NOREPEAT, (uint)chord.VirtualKey))
                        return 0;
                    return Marshal.GetLastWin32Error();
                });
                if (ok != 0)
                    return new(false, ok == Win32.ERROR_HOTKEY_ALREADY_REGISTERED
                        ? Strings.Format("Hotkey_Error_OtherApp", pretty)
                        : Strings.Format("Hotkey_Error_WindowsRefused", pretty, ok));
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
            {
                return new(false, Strings.Get("Hotkey_Error_RestartNeeded"));
            }
            _byId[entry.Id] = entry;
            _byChord[chord] = entry;
            _byOwner[ownerId] = entry;
            Publish();
            return HotkeyResult.Ok;
        }
    }

    public bool Unregister(string ownerId)
    {
        lock (_gate) return UnregisterLocked(ownerId);
    }

    private bool UnregisterLocked(string ownerId)
    {
        if (!_byOwner.Remove(ownerId, out var chord)) return false;
        chord.Handlers.RemoveAll(h => string.Equals(h.Owner, ownerId, StringComparison.OrdinalIgnoreCase));
        if (chord.Handlers.Count == 0)
        {
            _byId.Remove(chord.Id);
            _byChord.Remove(chord.Key);
            try { _hub.Invoke(() => Win32.UnregisterHotKey(_hub.WindowHandle, chord.Id)); } catch { }
        }
        Publish();
        return true;
    }

    public bool IsRegistered(string ownerId) { lock (_gate) return _byOwner.ContainsKey(ownerId); }

    public IReadOnlyList<HotkeyRegistration> Registrations
    {
        get
        {
            lock (_gate)
                return _byChord.Values.SelectMany(c => c.Handlers.Select(h => new HotkeyRegistration(h.Owner, HotkeyParser.Format(c.Key), h.Label, h.OnlyInApp?.Name ?? h.OnlyInApp?.Id))).ToList();
        }
    }

    private static bool SameApp(AppValue a, AppValue b) =>
        string.Equals(a.Id, b.Id, StringComparison.OrdinalIgnoreCase) || (a.Name is not null && string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

    private void OnHotkey(int id)
    {
        if (!_snapshot.TryGetValue(id, out var chord)) return;
        var handlers = chord.Handlers;
        RunningApp? front = null;
        if (handlers.Any(h => h.OnlyInApp is not null)) front = AppInfo.Foreground();

        var toRun = handlers.Where(h => h.OnlyInApp is null || (front is not null && AppMatcher.Matches(h.OnlyInApp, front))).ToList();
        if (toRun.Count == 0)
        {
            PassThrough(id, chord.Key);
            return;
        }
        foreach (var h in toRun)
        {
            var handler = h;
            Task.Run(() =>
            {
                try { handler.Callback(); }
                catch (Exception ex) { TriggerLog.Write($"Keyboard shortcut handler '{handler.Owner}' failed: {ex.Message}"); }
            });
        }
    }

    /// <summary>The shortcut isn't for the front app, so give the keystroke back to it: release our claim, press the key, claim it again.</summary>
    private void PassThrough(int id, HotkeyChord key)
    {
        try
        {
            Win32.UnregisterHotKey(_hub.WindowHandle, id);
            InputSender.PressKey(key.VirtualKey);
        }
        finally
        {
            Task.Delay(120).ContinueWith(_ =>
            {
                if (_disposed || !_snapshot.ContainsKey(id)) return;
                try
                {
                    _hub.Post(() =>
                    {
                        if (!_disposed && _snapshot.ContainsKey(id))
                            Win32.RegisterHotKey(_hub.WindowHandle, id, HotkeyParser.ToNativeModifiers(key.Modifiers) | Win32.MOD_NOREPEAT, (uint)key.VirtualKey);
                    });
                }
                catch { }
            });
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _hub.HotkeyPressed -= OnHotkey;
            var ids = _byId.Keys.ToList();
            try
            {
                if (_hub.IsRunning) _hub.Invoke(() => { foreach (var id in ids) Win32.UnregisterHotKey(_hub.WindowHandle, id); return 0; });
            }
            catch { }
            _byId.Clear(); _byChord.Clear(); _byOwner.Clear();
            _snapshot = new();
        }
    }
}
