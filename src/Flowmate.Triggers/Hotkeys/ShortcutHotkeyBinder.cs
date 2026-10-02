using Flowmate.Core;

namespace Flowmate.Triggers;

/// <summary>
/// Keeps every shortcut's keyboard shortcut (<see cref="Shortcut.Hotkey"/>) registered while the library changes, and runs the
/// shortcut when the keys are pressed. Shortcuts whose keys can't be registered are listed in <see cref="Conflicts"/> with a plain-English reason.
/// </summary>
public sealed class ShortcutHotkeyBinder : IDisposable
{
    private readonly FlowmateServices _services;
    private readonly HotkeyService _hotkeys;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, string> _registered = new();
    private readonly Dictionary<Guid, string> _conflicts = new();
    private Timer? _debounce;
    private bool _started, _disposed;

    public ShortcutHotkeyBinder(FlowmateServices services, HotkeyService hotkeys)
    {
        _services = services;
        _hotkeys = hotkeys;
    }

    /// <summary>Shortcut id to the reason its keyboard shortcut isn't working ("Another app already uses Ctrl+Alt+M…").</summary>
    public IReadOnlyDictionary<Guid, string> Conflicts { get { lock (_gate) return new Dictionary<Guid, string>(_conflicts); } }

    /// <summary>The reason a shortcut's keyboard shortcut isn't working, or null when it is fine.</summary>
    public string? ConflictFor(Guid shortcutId) { lock (_gate) return _conflicts.GetValueOrDefault(shortcutId); }

    public event EventHandler? ConflictsChanged;

    public static string OwnerId(Guid shortcutId) => "shortcut:" + shortcutId.ToString("N");

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
        }
        _services.Library.Changed += OnChanged;
        Sync();
    }

    private void OnChanged(object? sender, LibraryChangedEventArgs e)
    {
        if (!e.What.HasFlag(LibraryChange.Shortcuts)) return;
        lock (_gate)
        {
            if (_disposed) return;
            _debounce?.Dispose();
            _debounce = new Timer(_ => Sync(), null, 200, Timeout.Infinite);
        }
    }

    /// <summary>Makes the registered keys match the library right now (also retries shortcuts that conflicted before).</summary>
    public void Sync()
    {
        var conflictsChanged = false;
        lock (_gate)
        {
            if (_disposed) return;
            var wanted = new Dictionary<Guid, (string Hotkey, string Name)>();
            foreach (var s in _services.Library.All)
                if (!string.IsNullOrWhiteSpace(s.Hotkey)) wanted[s.Id] = (s.Hotkey.Trim(), s.Name);

            foreach (var id in _registered.Keys.ToList())
            {
                if (wanted.TryGetValue(id, out var w) && string.Equals(HotkeyParser.Normalize(w.Hotkey), _registered[id], StringComparison.OrdinalIgnoreCase)) continue;
                _hotkeys.Unregister(OwnerId(id));
                _registered.Remove(id);
            }
            foreach (var id in _conflicts.Keys.Where(id => !wanted.ContainsKey(id)).ToList()) { _conflicts.Remove(id); conflictsChanged = true; }

            foreach (var (id, w) in wanted)
            {
                if (_registered.ContainsKey(id)) continue;
                var shortcutId = id;
                var result = _hotkeys.Register(OwnerId(id), w.Hotkey, () => Run(shortcutId), $"the shortcut \"{w.Name}\"");
                if (result.Success)
                {
                    _registered[id] = HotkeyParser.Normalize(w.Hotkey) ?? w.Hotkey;
                    if (_conflicts.Remove(id)) conflictsChanged = true;
                }
                else if (!_conflicts.TryGetValue(id, out var old) || old != result.Reason)
                {
                    _conflicts[id] = result.Reason ?? "This keyboard shortcut can't be used.";
                    conflictsChanged = true;
                }
            }
        }
        if (conflictsChanged) ConflictsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Run(Guid shortcutId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var shortcut = _services.Library.GetById(shortcutId);
                if (shortcut is null) return;
                var result = await _services.Runner.RunAsync(shortcutId, ContentValue.Empty, new RunOptions { Source = RunSource.Hotkey });
                if (result.Status == RunStatus.Failed)
                    _services.Notifications.Show($"{shortcut.Name} didn't finish", result.ErrorMessage ?? "Something went wrong. Open Run History for details.");
                else if (result.Status == RunStatus.Succeeded && _services.Settings.Current.NotifyOnHotkeyRuns)
                    _services.Notifications.Show(shortcut.Name, "Done.");
            }
            catch (Exception ex) { TriggerLog.Write($"Keyboard shortcut run failed: {ex.Message}"); }
        });
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _debounce?.Dispose();
            foreach (var id in _registered.Keys) _hotkeys.Unregister(OwnerId(id));
            _registered.Clear();
        }
        if (_started) _services.Library.Changed -= OnChanged;
    }
}
