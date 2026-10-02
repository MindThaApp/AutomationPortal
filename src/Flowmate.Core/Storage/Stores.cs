using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Flowmate.Core;

public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly FlowmatePaths _paths;
    private readonly object _gate = new();
    private AppSettings _current;

    public JsonSettingsStore(FlowmatePaths paths)
    {
        _paths = paths;
        _current = Load();
    }

    public AppSettings Current { get { lock (_gate) return _current; } }
    public event EventHandler<AppSettings>? Changed;

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_paths.SettingsFile))
                return FlowmateJson.Deserialize<AppSettings>(File.ReadAllText(_paths.SettingsFile)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException) { }
        return new AppSettings();
    }

    public void Update(Action<AppSettings> change)
    {
        AppSettings snapshot;
        lock (_gate)
        {
            change(_current);
            AtomicFile.WriteAllText(_paths.SettingsFile, FlowmateJson.Serialize(_current, indented: true));
            snapshot = _current;
        }
        Changed?.Invoke(this, snapshot);
    }
}

/// <summary>Secrets protected with Windows DPAPI for the current user (secrets.dat).</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore : ISecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Flowmate.Secrets.v1");
    private readonly string _file;
    private readonly object _gate = new();

    public DpapiSecretStore(FlowmatePaths paths) { _file = paths.SecretsFile; }

    private Dictionary<string, string> Read()
    {
        if (!File.Exists(_file)) return new();
        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(_file), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(plain) ?? new();
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException) { return new(); }
    }

    private void Write(Dictionary<string, string> data)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(data);
        AtomicFile.WriteAllBytes(_file, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
    }

    public string? Get(string name) { lock (_gate) return Read().TryGetValue(name, out var v) ? v : null; }

    public void Set(string name, string value) { lock (_gate) { var d = Read(); d[name] = value; Write(d); } }

    public void Remove(string name) { lock (_gate) { var d = Read(); if (d.Remove(name)) Write(d); } }
}

public sealed class JsonKeyValueStore : IKeyValueStore
{
    private readonly string _file;
    private readonly object _gate = new();
    private readonly Dictionary<string, ContentValue> _data;

    public JsonKeyValueStore(FlowmatePaths paths)
    {
        _file = paths.SavedValuesFile;
        _data = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(_file))
                foreach (var (k, v) in FlowmateJson.Deserialize<Dictionary<string, ContentValue>>(File.ReadAllText(_file)) ?? new())
                    _data[k] = v;
        }
        catch (Exception ex) when (ex is JsonException or IOException) { }
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

    private void Save() => AtomicFile.WriteAllText(_file, FlowmateJson.Serialize(_data, indented: true));
}

/// <summary>Run history as monthly JSONL files; keeps the newest <see cref="MaxRecords"/> runs.</summary>
public sealed class JsonlRunHistory : IRunHistory
{
    public const int MaxRecords = 1000;
    private readonly FlowmatePaths _paths;
    private readonly object _gate = new();
    private int _count = -1;

    public JsonlRunHistory(FlowmatePaths paths) { _paths = paths; }

    private IEnumerable<string> Files() =>
        Directory.Exists(_paths.HistoryFolder) ? Directory.EnumerateFiles(_paths.HistoryFolder, "runs-*.jsonl").OrderBy(f => f, StringComparer.Ordinal) : Enumerable.Empty<string>();

    private static IEnumerable<RunRecord> ReadFile(string file)
    {
        foreach (var line in File.ReadLines(file))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            RunRecord? r = null;
            try { r = FlowmateJson.Deserialize<RunRecord>(line); } catch (JsonException) { }
            if (r is not null) yield return r;
        }
    }

    public void Add(RunRecord record)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_paths.HistoryFolder);
            if (_count < 0) _count = Files().Sum(f => File.ReadLines(f).Count(l => l.Length > 0));
            File.AppendAllText(_paths.HistoryFile(record.Started), FlowmateJson.Serialize(record) + "\n");
            if (++_count > MaxRecords + 100) Trim();
        }
    }

    private void Trim()
    {
        var all = Files().SelectMany(f => ReadFile(f)).OrderBy(r => r.Started).ToList();
        var keep = all.Skip(Math.Max(0, all.Count - MaxRecords)).ToList();
        foreach (var f in Files()) File.Delete(f);
        foreach (var g in keep.GroupBy(r => _paths.HistoryFile(r.Started)))
            File.WriteAllText(g.Key, string.Concat(g.Select(r => FlowmateJson.Serialize(r) + "\n")));
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
    private readonly List<Reminder> _items;
    private Timer? _timer;
    private bool _started;

    public ReminderStore(FlowmatePaths paths)
    {
        _file = paths.RemindersFile;
        _items = new();
        try
        {
            if (File.Exists(_file)) _items = FlowmateJson.Deserialize<List<Reminder>>(File.ReadAllText(_file)) ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException) { }
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

    /// <summary>Starts the timer. Reminders that came due while Flowmate was closed fire immediately.</summary>
    public void Start() { lock (_gate) { _started = true; Arm(); } }

    private void Save() => AtomicFile.WriteAllText(_file, FlowmateJson.Serialize(_items, indented: true));

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

    private void Tick()
    {
        List<Reminder> due;
        lock (_gate)
        {
            var now = DateTimeOffset.Now;
            due = _items.Where(r => r.When <= now).OrderBy(r => r.When).ToList();
            if (due.Count > 0) { _items.RemoveAll(r => due.Contains(r)); Save(); }
            Arm();
        }
        foreach (var r in due)
        {
            try { Due?.Invoke(this, r); } catch { }
        }
    }

    public void Dispose() { lock (_gate) { _timer?.Dispose(); _timer = null; } }
}
