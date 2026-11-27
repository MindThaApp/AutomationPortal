using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AutomationPortal.Core;

public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly AutomationPortalPaths _paths;
    private readonly object _gate = new();
    private AppSettings _current;
    private bool _unreadable;

    /// <summary>Set when settings.json exists but couldn't be read: defaults are used in memory and the file is left untouched.</summary>
    public string? LoadError { get; private set; }

    public JsonSettingsStore(AutomationPortalPaths paths)
    {
        _paths = paths;
        _current = Load();
    }

    public AppSettings Current { get { lock (_gate) return _current; } }
    public event EventHandler<AppSettings>? Changed;

    private AppSettings Load()
    {
        var r = StoreFile.ReadText(_paths.SettingsFile);
        _unreadable = false; LoadError = null;
        switch (r.Status)
        {
            case StoreReadStatus.Unreadable:
                _unreadable = true;
                LoadError = Strings.Format("Stores_Settings_Unreadable", r.Error);
                StoreDiagnostics.Report(LoadError);
                return new AppSettings { SettingsVersion = AppSettings.CurrentVersion };
            case StoreReadStatus.Ok:
                try
                {
                    var loaded = AutomationPortalJson.Deserialize<AppSettings>(r.Text!) ?? new AppSettings();
                    if (Migrate(loaded)) { try { StoreFile.WriteText(_paths.SettingsFile, AutomationPortalJson.Serialize(loaded, indented: true)); } catch (IOException) { } }
                    return loaded;
                }
                catch (JsonException ex)
                {
                    StoreFile.Quarantine(_paths.SettingsFile);
                    LoadError = Strings.Format("Stores_Settings_Damaged", ex.Message);
                    StoreDiagnostics.Report(LoadError, ex);
                    break;
                }
        }
        return new AppSettings { SettingsVersion = AppSettings.CurrentVersion };
    }
    /// <summary>Moves defaults forward, but only for people still on the old default. Returns true when something changed.</summary>
    private static bool Migrate(AppSettings s)
    {
        if (s.SettingsVersion >= AppSettings.CurrentVersion) return false;
        if (string.Equals(s.QuickRunHotkey?.Replace(" ", ""), AppSettings.OldQuickRunHotkey, StringComparison.OrdinalIgnoreCase))
            s.QuickRunHotkey = AppSettings.DefaultQuickRunHotkey;
        s.SettingsVersion = AppSettings.CurrentVersion;
        return true;
    }

    public void Update(Action<AppSettings> change)
    {
        AppSettings snapshot;
        lock (_gate)
        {
            if (_unreadable)
            {
                // The real file may hold the library location and more; only write once we have actually read it.
                var again = Load();
                if (!_unreadable) _current = again;
            }
            change(_current);
            if (!_unreadable) StoreFile.WriteText(_paths.SettingsFile, AutomationPortalJson.Serialize(_current, indented: true));
            snapshot = _current;
        }
        Changed?.Invoke(this, snapshot);
    }
}

/// <summary>Secrets protected with Windows DPAPI for the current user (secrets.dat).</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore : ISecretStore
{
    // Stays as it was before the rename: changing it would make saved keys unreadable.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Flowmate.Secrets.v1");
    private readonly string _file;
    private readonly object _gate = new();

    public DpapiSecretStore(AutomationPortalPaths paths) { _file = paths.SecretsFile; }

    /// <summary>Null = the file exists but couldn't be read (never write over it).</summary>
    private Dictionary<string, string>? Read()
    {
        var r = StoreFile.ReadBytes(_file);
        if (r.Status == StoreReadStatus.Missing) return new();
        if (r.Status == StoreReadStatus.Unreadable) { StoreDiagnostics.Report($"secrets.dat can't be read right now ({r.Error})."); return null; }
        try
        {
            var plain = ProtectedData.Unprotect(r.Bytes!, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? new();
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            StoreFile.Quarantine(_file);
            StoreDiagnostics.Report("secrets.dat couldn't be decrypted (a different Windows account?). A copy was kept next to it.", ex);
            return new();
        }
    }

    private Dictionary<string, string> ReadForWrite() =>
        Read() ?? throw new LibraryException(Strings.Get("Stores_Keys_Unreadable"));

    private void Write(Dictionary<string, string> data)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(data);
        StoreFile.WriteBytes(_file, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
    }
    public string? Get(string name) { lock (_gate) return Read() is { } d && d.TryGetValue(name, out var v) ? v : null; }

    public void Set(string name, string value) { lock (_gate) { var d = ReadForWrite(); d[name] = value; Write(d); } }

    public void Remove(string name) { lock (_gate) { var d = ReadForWrite(); if (d.Remove(name)) Write(d); } }
}

public sealed class JsonKeyValueStore : IKeyValueStore
{
    private readonly string _file;
    private readonly object _gate = new();
    private readonly Dictionary<string, ContentValue> _data;

    private bool _unreadable;

    public JsonKeyValueStore(AutomationPortalPaths paths)
    {
        _file = paths.SavedValuesFile;
        _data = new(StringComparer.OrdinalIgnoreCase);
        LoadFromDisk();
    }

    private void LoadFromDisk()
    {
        var r = StoreFile.ReadText(_file);
        _unreadable = r.Status == StoreReadStatus.Unreadable;
        if (_unreadable) { StoreDiagnostics.Report($"saved-values.json can't be read right now ({r.Error})."); return; }
        if (r.Status != StoreReadStatus.Ok) return;
        try
        {
            foreach (var (k, v) in AutomationPortalJson.Deserialize<Dictionary<string, ContentValue>>(r.Text!) ?? new())
                _data.TryAdd(k, v);
        }
        catch (JsonException ex)
        {
            StoreFile.Quarantine(_file);
            StoreDiagnostics.Report("saved-values.json is damaged. A copy was kept next to it.", ex);
        }
    }
    public ContentValue? Get(string key) { lock (_gate) return _data.TryGetValue(key, out var v) ? v : null; }
    public IReadOnlyList<string> Keys { get { lock (_gate) return _data.Keys.ToList(); } }

    public void Set(string key, ContentValue value)
    {
        lock (_gate) { _data[key] = value; Save(); }
    }

    public bool Remove(string key)
    {
        lock (_gate) { var r = _data.Remove(key); if (r) Save(); return r; }
    }

    private void Save()
    {
        if (_unreadable)
        {
            LoadFromDisk();
            if (_unreadable) throw new LibraryException(Strings.Get("Stores_Values_Unreadable"));
        }
        StoreFile.WriteText(_file, AutomationPortalJson.Serialize(_data, indented: true));
    }
}

/// <summary>Run history as monthly JSONL files; keeps the newest <see cref="MaxRecords"/> runs.</summary>
public sealed class JsonlRunHistory : IRunHistory
{
    public const int MaxRecords = 1000;
    private readonly AutomationPortalPaths _paths;
    private readonly object _gate = new();
    private int _count = -1;

    public JsonlRunHistory(AutomationPortalPaths paths) { _paths = paths; }

    private IEnumerable<string> Files() =>
        Directory.Exists(_paths.HistoryFolder) ? Directory.EnumerateFiles(_paths.HistoryFolder, "runs-*.jsonl").OrderBy(f => f, StringComparer.Ordinal) : Enumerable.Empty<string>();

    private static IEnumerable<RunRecord> ReadFile(string file)
    {
        foreach (var line in File.ReadLines(file))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            RunRecord? r = null;
            try { r = AutomationPortalJson.Deserialize<RunRecord>(line); } catch (JsonException) { }
            if (r is not null) yield return r;
        }
    }

    public void Add(RunRecord record)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_paths.HistoryFolder);
            if (_count < 0) _count = Files().Sum(f => File.ReadLines(f).Count(l => l.Length > 0));
            File.AppendAllText(_paths.HistoryFile(record.Started), AutomationPortalJson.Serialize(record) + "\n");
            if (++_count > MaxRecords + 100) Trim();
        }
    }

    private void Trim()
    {
        var all = Files().SelectMany(f => ReadFile(f)).OrderBy(r => r.Started).ToList();
        var keep = all.Skip(Math.Max(0, all.Count - MaxRecords)).ToList();
        foreach (var f in Files()) File.Delete(f);
        foreach (var g in keep.GroupBy(r => _paths.HistoryFile(r.Started)))
            File.WriteAllText(g.Key, string.Concat(g.Select(r => AutomationPortalJson.Serialize(r) + "\n")));
        _count = keep.Count;
    }

    public IReadOnlyList<RunRecord> Query(RunHistoryFilter? filter = null)
    {
        filter ??= new RunHistoryFilter();
        lock (_gate)
        {
            var result = new List<RunRecord>();
            foreach (var file in Files().Reverse())
            {
                foreach (var r in ReadFile(file).Reverse())
                {
                    if (filter.ShortcutId is { } id && r.ShortcutId != id) continue;
                    if (filter.Status is { } st && r.Status != st) continue;
                    if (filter.Since is { } since && r.Started < since) continue;
                    if (!string.IsNullOrWhiteSpace(filter.Text) && !r.ShortcutName.Contains(filter.Text, StringComparison.CurrentCultureIgnoreCase)) continue;
                    result.Add(r);
                }
            }
            return result.OrderByDescending(r => r.Started).Take(Math.Min(filter.Limit, MaxRecords)).ToList();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            foreach (var f in Files()) File.Delete(f);
            _count = 0;
        }
    }
}

/// <summary>Reminders persisted to reminders.json, with a timer that raises <see cref="Due"/>. Call <see cref="Start"/> once the host is ready to show them.</summary>
public sealed class ReminderStore : IReminderService, IDisposable
{
    private readonly string _file;
    private readonly object _gate = new();
    private List<Reminder> _items;
    private Timer? _timer;
    private bool _started;

    public ReminderStore(AutomationPortalPaths paths)
    {
        _file = paths.RemindersFile;
        _items = new();
        LoadFromDisk();
    }

    private bool _unreadable;

    private void LoadFromDisk()
    {
        var r = StoreFile.ReadText(_file);
        _unreadable = r.Status == StoreReadStatus.Unreadable;
        if (_unreadable) { StoreDiagnostics.Report($"reminders.json can't be read right now ({r.Error})."); return; }
        if (r.Status != StoreReadStatus.Ok) return;
        try { _items = AutomationPortalJson.Deserialize<List<Reminder>>(r.Text!) ?? new(); }
        catch (JsonException ex)
        {
            StoreFile.Quarantine(_file);
            StoreDiagnostics.Report("reminders.json is damaged. A copy was kept next to it.", ex);
        }
    }

    public event EventHandler<Reminder>? Due;

    public Reminder Schedule(Reminder reminder)
    {
        if (reminder.Id == Guid.Empty) reminder.Id = Guid.NewGuid();
        lock (_gate)
        {
            _items.RemoveAll(r => r.Id == reminder.Id);
            _items.Add(reminder);
            Save();
            Arm();
        }
        return reminder;
    }

    public bool Cancel(Guid id)
    {
        lock (_gate)
        {
            var removed = _items.RemoveAll(r => r.Id == id) > 0;
            if (removed) { Save(); Arm(); }
            return removed;
        }
    }

    public IReadOnlyList<Reminder> List() { lock (_gate) return _items.OrderBy(r => r.When).ToList(); }

    /// <summary>Starts the timer. Reminders that came due while Automation Portal was closed fire immediately.</summary>
    public void Start() { lock (_gate) { _started = true; Arm(); } }

    private void Save()
    {
        if (_unreadable)
        {
            var mine = _items;
            _items = new();
            LoadFromDisk();
            if (_unreadable) { _items = mine; throw new LibraryException(Strings.Get("Stores_Reminders_Unreadable")); }
            foreach (var r in mine) if (_items.All(x => x.Id != r.Id)) _items.Add(r);
        }
        StoreFile.WriteText(_file, AutomationPortalJson.Serialize(_items, indented: true));
    }

    private void Arm()
    {
        _timer?.Dispose();
        _timer = null;
        if (!_started || _items.Count == 0) return;
        var next = _items.Min(r => r.When);
        var wait = next - DateTimeOffset.Now;
        if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
        if (wait > TimeSpan.FromHours(1)) wait = TimeSpan.FromHours(1);
        _timer = new Timer(_ => Tick(), null, wait, Timeout.InfiniteTimeSpan);
    }

    // Timer callback: nothing may escape (it would take the whole app down) and the timer must always be re-armed.
    private void Tick()
    {
        List<Reminder> due = new();
        try
        {
            lock (_gate)
            {
                try
                {
                    var now = DateTimeOffset.Now;
                    due = _items.Where(r => r.When <= now).OrderBy(r => r.When).ToList();
                    if (due.Count > 0)
                    {
                        _items.RemoveAll(r => due.Contains(r));
                        try { Save(); }
                        catch (Exception ex) { StoreDiagnostics.Report("Couldn't save reminders after one came due.", ex); }
                    }
                }
                finally { try { Arm(); } catch (Exception ex) { StoreDiagnostics.Report("Couldn't schedule the next reminder.", ex); } }
            }
        }
        catch (Exception ex) { StoreDiagnostics.Report("Reminder timer failed.", ex); }
        foreach (var r in due)
        {
            try { Due?.Invoke(this, r); } catch { }
        }
    }
    public void Dispose() { lock (_gate) { _timer?.Dispose(); _timer = null; } }
}
