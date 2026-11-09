using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AutomationPortal.Core;

/// <summary>
/// Library stored as files: shortcuts\&lt;id&gt;.json, folders.json, automations.json, snippets.json. Atomic writes; reloads when files change outside the app.
/// A file that exists but can't be read is never treated as empty: the previous in-memory data is kept and writes to that file are refused until a reload can read it.
/// </summary>
public sealed class JsonShortcutLibrary : ShortcutLibraryBase, IDisposable
{
    private const LibraryChange All = LibraryChange.Shortcuts | LibraryChange.Folders | LibraryChange.Automations | LibraryChange.Snippets;

    private readonly AutomationPortalPaths _paths;
    // Last content we wrote per path (hash), "" = we deleted it. External edits differ, so they are never missed.
    private readonly ConcurrentDictionary<string, string> _selfWrites = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _timerGate = new();
    private readonly HashSet<string> _unsafe = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, string> _fileOf = new();
    private readonly Dictionary<Guid, List<string>> _dupFiles = new();
    private long _rev;
    private bool _loadedOnce;
    private int _retries;
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private bool _disposed;

    /// <summary>Problems found while loading (corrupt, locked or duplicate files are skipped, never deleted).</summary>
    public List<string> LoadErrors { get; } = new();

    public JsonShortcutLibrary(AutomationPortalPaths paths, ActionRegistry? registry = null, bool watch = true) : base(registry)
    {
        _paths = paths;
        Directory.CreateDirectory(paths.ShortcutsFolder);
        Load();
        if (watch) { StartWatching(); bool retry; lock (Gate) retry = _unsafe.Count > 0; if (retry) ScheduleReload(5000); }
    }

    private string SnippetsFile => Path.Combine(_paths.LibraryFolder, "snippets.json");

    private sealed class Snapshot
    {
        public Dictionary<Guid, Shortcut>? Map;
        public readonly Dictionary<Guid, string> FileOf = new();
        public readonly Dictionary<Guid, List<string>> Dups = new();
        public readonly HashSet<string> Unsafe = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Errors = new();
        public List<ShortcutFolder>? Folders;
        public List<Automation>? Automations;
        public List<Snippet>? Snippets;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads everything and swaps it in. Returns false when a local save raced the read and the result was discarded.</summary>
    private bool Load()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            long start;
            Dictionary<string, Shortcut> prevByFile;
            bool first;
            lock (Gate)
            {
                start = _rev;
                first = !_loadedOnce;
                prevByFile = new(StringComparer.OrdinalIgnoreCase);
                foreach (var (id, s) in ShortcutMap)
                    if (_fileOf.TryGetValue(id, out var f)) prevByFile[f] = s;
            }

            var snap = ReadAll(prevByFile, first);

            lock (Gate)
            {
                if (_rev != start) continue;
                if (snap.Map is not null)
                {
                    ShortcutMap = snap.Map;
                    _fileOf.Clear(); foreach (var (k, v) in snap.FileOf) _fileOf[k] = v;
                    _dupFiles.Clear(); foreach (var (k, v) in snap.Dups) _dupFiles[k] = v;
                }
                if (snap.Folders is not null) FolderList = snap.Folders;
                if (snap.Automations is not null) AutomationList = snap.Automations;
                if (snap.Snippets is not null) SnippetList = snap.Snippets;
                _unsafe.Clear(); foreach (var u in snap.Unsafe) _unsafe.Add(u);
                LoadErrors.Clear();
                LoadErrors.AddRange(snap.Errors);
                _loadedOnce = true;
                if (snap.Unsafe.Count == 0) _retries = 0;
            }
            if (snap.Unsafe.Count > 0)
                StoreDiagnostics.Report("Some library files can't be read right now, so Automation Portal won't change them: " + string.Join("; ", snap.Errors));
            return true;
        }
        return false;
    }

    private Snapshot ReadAll(Dictionary<string, Shortcut> prevByFile, bool first)
    {
        var snap = new Snapshot();
        List<string>? files = null;
        try
        {
            Directory.CreateDirectory(_paths.ShortcutsFolder);
            files = Directory.EnumerateFiles(_paths.ShortcutsFolder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            snap.Errors.Add($"The shortcuts folder can't be read: {ex.Message}");
        }

        if (files is not null)
        {
            snap.Map = new();
            foreach (var file in files)
            {
                var r = StoreFile.ReadText(file);
                if (r.Status == StoreReadStatus.Missing) continue;
                Shortcut? s = null;
                if (r.Status == StoreReadStatus.Ok)
                {
                    try { s = AutomationPortalJson.Deserialize<Shortcut>(r.Text!); }
                    catch (JsonException ex) { snap.Errors.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
                }
                else
                {
                    snap.Errors.Add($"{Path.GetFileName(file)}: {r.Error}");
                    snap.Unsafe.Add(file);
                }
                if (s is null && !prevByFile.TryGetValue(file, out s)) continue;

                if (snap.Map.TryGetValue(s.Id, out _))
                {
                    var current = snap.FileOf[s.Id];
                    var dups = snap.Dups.TryGetValue(s.Id, out var d) ? d : snap.Dups[s.Id] = new();
                    if (Same(file, _paths.ShortcutFile(s.Id)) && !Same(current, file))
                    {
                        dups.Add(current);
                        snap.Map[s.Id] = s; snap.FileOf[s.Id] = file;
                        snap.Errors.Add($"{Path.GetFileName(current)} has the same id as {Path.GetFileName(file)}; it is ignored.");
                    }
                    else
                    {
                        dups.Add(file);
                        snap.Errors.Add($"{Path.GetFileName(file)} has the same id as {Path.GetFileName(current)}; it is ignored.");
                    }
                    continue;
                }
                snap.Map[s.Id] = s;
                snap.FileOf[s.Id] = file;
            }
        }

        snap.Folders = ReadList<ShortcutFolder>(_paths.FoldersFile, snap, first);
        snap.Automations = ReadList<Automation>(_paths.AutomationsFile, snap, first);
        snap.Snippets = ReadList<Snippet>(SnippetsFile, snap, first);
        return snap;
    }

    /// <summary>Null = keep what's in memory (unreadable, or damaged while the app was running).</summary>
    private static List<T>? ReadList<T>(string file, Snapshot snap, bool first)
    {
        var r = StoreFile.ReadText(file);
        switch (r.Status)
        {
            case StoreReadStatus.Missing: return new();
            case StoreReadStatus.Unreadable:
                snap.Errors.Add($"{Path.GetFileName(file)}: {r.Error}");
                snap.Unsafe.Add(file);
                return null;
        }
        try { return AutomationPortalJson.Deserialize<List<T>>(r.Text!) ?? new(); }
        catch (JsonException ex)
        {
            snap.Errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            if (first) { StoreFile.Quarantine(file); return new(); }
            snap.Unsafe.Add(file);
            return null;
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private void CheckWritable(string path)
    {
        if (_unsafe.Contains(path))
            throw new LibraryException($"Automation Portal couldn't read '{Path.GetFileName(path)}' just now, so it won't change it (that could lose your data). Close anything that has it open and try again in a moment.");
    }

    private void Write(string path, string text, bool backup)
    {
        CheckWritable(path);
        var bytes = new UTF8Encoding(false).GetBytes(text);
        _selfWrites[path] = Hash(bytes);
        try { StoreFile.WriteText(path, text, backup); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LibraryException($"Couldn't save '{Path.GetFileName(path)}'. {ex.Message}", ex);
        }
        _rev++;
    }

    protected override void PersistShortcut(Shortcut s)
    {
        var path = _paths.ShortcutFile(s.Id);
        Write(path, AutomationPortalJson.Serialize(s, indented: true), backup: false);
        _fileOf[s.Id] = path;
    }

    protected override void PersistShortcutDeleted(Guid id)
    {
        var paths = new List<string> { _paths.ShortcutFile(id) };
        if (_fileOf.TryGetValue(id, out var src) && !Same(src, paths[0])) paths.Add(src);
        foreach (var p in paths)
        {
            CheckWritable(p);
            _selfWrites[p] = "";
            try { File.Delete(p); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new LibraryException($"Couldn't delete '{Path.GetFileName(p)}'. It may be open in another program.", ex);
            }
        }
        // OneDrive conflict copies carry the same id and would bring the shortcut back; set them aside, don't destroy them.
        if (_dupFiles.Remove(id, out var dups))
            foreach (var d in dups)
            {
                _selfWrites[d] = "";
                try { File.Move(d, d + ".dup", overwrite: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        _fileOf.Remove(id);
        _rev++;
    }

    protected override void PersistFolders() => Write(_paths.FoldersFile, AutomationPortalJson.Serialize(FolderList, indented: true), backup: true);
    protected override void PersistAutomations() => Write(_paths.AutomationsFile, AutomationPortalJson.Serialize(AutomationList, indented: true), backup: true);
    protected override void PersistSnippets() => Write(SnippetsFile, AutomationPortalJson.Serialize(SnippetList, indented: true), backup: true);

    private void StartWatching()
    {
        Directory.CreateDirectory(_paths.LibraryFolder);
        _watcher = new FileSystemWatcher(_paths.LibraryFolder, "*.json") { IncludeSubdirectories = true, EnableRaisingEvents = true };
        FileSystemEventHandler h = (_, e) => OnFileEvent(e.FullPath);
        _watcher.Changed += h; _watcher.Created += h; _watcher.Deleted += h;
        _watcher.Renamed += (_, e) => OnFileEvent(e.FullPath);
        _watcher.Error += (_, e) => { StoreDiagnostics.Report("The library folder watcher stopped.", e.GetException()); ScheduleReload(400); };
    }

    private bool IsOurOwnWrite(string path)
    {
        if (!_selfWrites.TryGetValue(path, out var expected)) return false;
        if (expected.Length == 0) return !File.Exists(path);
        try { return Hash(File.ReadAllBytes(path)) == expected; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private void OnFileEvent(string path)
    {
        try
        {
            if (IsOurOwnWrite(path)) return;
            ScheduleReload(400);
        }
        catch (Exception ex) { StoreDiagnostics.Report("Library file change handling failed.", ex); }
    }

    private void ScheduleReload(int delayMs)
    {
        lock (_timerGate)
        {
            if (_disposed) return;
            _debounce?.Dispose();
            _debounce = new Timer(_ => BackgroundReload(), null, delayMs, Timeout.Infinite);
        }
    }

    /// <summary>Runs on a thread-pool timer: nothing may escape, or the whole app goes down.</summary>
    private void BackgroundReload()
    {
        try
        {
            var swapped = Load();
            bool again;
            lock (Gate) again = !swapped || (_unsafe.Count > 0 && _retries++ < 6);
            if (again) ScheduleReload(swapped ? 5000 : 200);
            if (swapped) RaiseChanged(All, external: true);
        }
        catch (Exception ex)
        {
            StoreDiagnostics.Report("Reloading the library failed. Automation Portal keeps using what it has.", ex);
        }
    }

    /// <summary>Re-reads everything from disk and raises Changed.</summary>
    public void Reload()
    {
        Load();
        RaiseChanged(All, external: true);
    }

    public void Dispose()
    {
        lock (_timerGate) { _disposed = true; _debounce?.Dispose(); _debounce = null; }
        _watcher?.Dispose();
    }
}
