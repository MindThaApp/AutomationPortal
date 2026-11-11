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
    // Set under Gate while Restore saves, so the shortcut file is only ever created, never replaced.
    private bool _createOnly;

    /// <summary>Problems found while loading (corrupt, locked or duplicate files are skipped, never deleted).</summary>
    public List<string> LoadErrors { get; } = new();

    /// <summary>Where <see cref="ShortcutLibraryBase.Delete"/> puts shortcuts; see <see cref="Restore"/>.</summary>
    public RecentlyDeleted RecentlyDeleted { get; }

    /// <summary>Earlier versions of each shortcut, kept whenever a save replaces a shortcut's file; see <see cref="RestoreVersion"/>.</summary>
    public ShortcutVersions Versions { get; }

    public JsonShortcutLibrary(AutomationPortalPaths paths, ActionRegistry? registry = null, bool watch = true) : base(registry)
    {
        _paths = paths;
        RecentlyDeleted = new RecentlyDeleted(paths.DeletedFolder);
        Versions = new ShortcutVersions(paths.VersionsFolder);
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

    private void Write(string path, string text, bool backup, bool createNew = false)
    {
        CheckWritable(path);
        var bytes = new UTF8Encoding(false).GetBytes(text);
        _selfWrites[path] = Hash(bytes);
        try
        {
            if (createNew) WriteNew(path, bytes);
            else StoreFile.WriteText(path, text, backup);
        }
        catch (Exception ex) when (createNew && ex is IOException && File.Exists(path))
        {
            throw new LibraryException($"'{Path.GetFileName(path)}' appeared in the shortcuts folder just now, so nothing was overwritten. Try again.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LibraryException($"Couldn't save '{Path.GetFileName(path)}'. {ex.Message}", ex);
        }
        _rev++;
    }

    /// <summary>Writes a file that must not exist yet: temp file, then a move that fails instead of replacing anything.</summary>
    private static void WriteNew(string path, byte[] bytes)
    {
        var temp = $"{path}.{Guid.NewGuid().ToString("N")[..8]}.tmp";
        using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            fs.Write(bytes);
            fs.Flush(flushToDisk: true);
        }
        try { File.Move(temp, path, overwrite: false); }
        catch
        {
            // The temp file was created above by this call (CreateNew).
            try { File.Delete(temp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    protected override void PersistShortcut(Shortcut s)
    {
        var path = _paths.ShortcutFile(s.Id);
        var text = AutomationPortalJson.Serialize(s, indented: true);
        if (!_createOnly) KeepPreviousVersion(s.Id, path, text);
        Write(path, text, backup: false, createNew: _createOnly);
        _fileOf[s.Id] = path;
    }

    /// <summary>Keeps the file a save is about to replace in <see cref="Versions"/>. Never throws: a failure here must not lose the edit.</summary>
    private void KeepPreviousVersion(Guid id, string path, string next)
    {
        try
        {
            var source = File.Exists(path) ? path : _fileOf.TryGetValue(id, out var f) ? f : null;
            if (source is null || _unsafe.Contains(source)) return;
            var r = StoreFile.ReadBytes(source);
            if (r.Status == StoreReadStatus.Unreadable)
                StoreDiagnostics.Report($"Couldn't keep the previous version of '{Path.GetFileName(source)}' in its history. {r.Error}");
            if (r.Status != StoreReadStatus.Ok) return;
            Versions.Keep(id, r.Bytes!, DateTimeOffset.Now, new UTF8Encoding(false).GetBytes(next));
        }
        catch (Exception ex)
        {
            StoreDiagnostics.Report($"Couldn't keep the previous version of a shortcut in its history. The shortcut was still saved. {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Puts an earlier version back under the same id. The shortcut as it is now is kept in <see cref="Versions"/> first, so this can be
    /// undone; if that fails nothing is changed. Its folder, order, pin and automations stay as they are now. With
    /// <paramref name="keepCurrentSettings"/> its keyboard shortcut, Quick Run name and Send To setting stay too; otherwise the version's
    /// own come back, except a keyboard shortcut or Quick Run name another shortcut now uses. If another shortcut has the version's name,
    /// the current name is kept.
    /// </summary>
    public RestoreResult RestoreVersion(ShortcutVersion version, bool keepCurrentSettings = true)
    {
        var old = Versions.Read(version);
        string? renamedFrom = null, clearedHotkey = null, clearedAlias = null;
        lock (Gate)
        {
            if (!ShortcutMap.TryGetValue(version.ShortcutId, out var current))
                throw new LibraryException("That shortcut isn't in the library any more, so the version can't be restored. Use \"Open as copy\" instead.");
            var currentBytes = new UTF8Encoding(false).GetBytes(AutomationPortalJson.Serialize(current, indented: true));
            try { Versions.Keep(current.Id, currentBytes, DateTimeOffset.Now); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or LibraryException)
            {
                throw new LibraryException($"Couldn't keep the current version in the history first, so nothing was restored. {ex.Message}", ex);
            }

            old.Id = current.Id;
            old.FolderId = current.FolderId; old.SortOrder = current.SortOrder; old.Pinned = current.Pinned; old.CreatedAt = current.CreatedAt;
            if (keepCurrentSettings)
            {
                old.Hotkey = current.Hotkey; old.Alias = current.Alias; old.ShowInSendTo = current.ShowInSendTo;
            }
            else
            {
                var others = ShortcutMap.Values.Where(x => x.Id != current.Id).ToList();
                if (HotkeyKey(old.Hotkey) is { } hk && others.Any(x => HotkeyKey(x.Hotkey) == hk)) { clearedHotkey = old.Hotkey; old.Hotkey = null; }
                if (old.Alias is { } alias && !string.IsNullOrWhiteSpace(alias)
                    && others.Any(x => string.Equals(x.Alias?.Trim(), alias.Trim(), StringComparison.OrdinalIgnoreCase)))
                { clearedAlias = alias; old.Alias = null; }
            }
            if (string.IsNullOrWhiteSpace(old.Name) || IsNameTaken(old.Name, current.Id))
            {
                if (!string.IsNullOrWhiteSpace(old.Name)) renamedFrom = old.Name.Trim();
                old.Name = current.Name;
            }
            SaveLocked(old);
        }
        RaiseChanged(LibraryChange.Shortcuts);
        return new RestoreResult(GetById(old.Id) ?? old, asCopy: false, renamedFrom, removedFromDeleted: true, clearedHotkey, clearedAlias);
    }

    /// <summary>Adds an earlier version as a new shortcut (new id, unique name, no keyboard shortcut, Quick Run name or Send To). The shortcut it came from isn't touched.</summary>
    public ImportResult OpenVersionAsCopy(ShortcutVersion version)
    {
        var old = Versions.Read(version);
        old.Name = $"{(string.IsNullOrWhiteSpace(old.Name) ? "Shortcut" : old.Name.Trim())} (older version)";
        return Import(old);
    }

    /// <summary>
    /// Moves the shortcut's file(s) into Recently deleted: copy, check the copy, and only then remove the original. Duplicate files with
    /// the same id (sync conflict copies) go the same way, or they would bring the shortcut back on the next reload. The file the
    /// library loaded it from is removed last, so a failure part-way leaves the shortcut in the library.
    /// </summary>
    protected override void PersistShortcutDeleted(Guid id)
    {
        var canonical = _paths.ShortcutFile(id);
        var main = _fileOf.TryGetValue(id, out var src) ? src : canonical;
        var paths = new List<string>();
        if (_dupFiles.TryGetValue(id, out var dups)) paths.AddRange(dups);
        if (!Same(main, canonical)) paths.Add(canonical);
        paths = paths.Where(p => !Same(p, main)).Distinct(StringComparer.OrdinalIgnoreCase).Append(main).ToList();
        foreach (var p in paths) CheckWritable(p);
        var automations = AutomationList.Count(a => a.ShortcutId == id);
        var now = DateTimeOffset.Now;
        var staged = new List<(string Path, DeletedShortcut Entry)>();
        try
        {
            foreach (var p in paths.Where(File.Exists))
                staged.Add((p, RecentlyDeleted.Add(p, id, NameIn(p), automations, now)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or LibraryException)
        {
            foreach (var (_, entry) in staged) TryDiscard(entry);
            throw ex as LibraryException ?? new LibraryException($"Couldn't move the shortcut to Recently deleted, so it wasn't deleted. {ex.Message}", ex);
        }
        for (var i = 0; i < staged.Count; i++)
        {
            var p = staged[i].Path;
            _selfWrites[p] = "";
            try { File.Delete(p); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _selfWrites.TryRemove(p, out _);
                for (var j = i; j < staged.Count; j++) TryDiscard(staged[j].Entry);
                throw new LibraryException($"Couldn't delete '{Path.GetFileName(p)}'. It may be open in another program.", ex);
            }
        }
        _dupFiles.Remove(id);
        _fileOf.Remove(id);
        _rev++;
    }

    private static string NameIn(string file)
    {
        var r = StoreFile.ReadText(file);
        try { if (r.Status == StoreReadStatus.Ok && AutomationPortalJson.Deserialize<Shortcut>(r.Text!) is { Name: { Length: > 0 } name }) return name; }
        catch (JsonException) { }
        return Path.GetFileNameWithoutExtension(file);
    }

    private void TryDiscard(DeletedShortcut entry)
    {
        try { RecentlyDeleted.DeletePermanently(entry); }
        catch (LibraryException ex) { StoreDiagnostics.Report(ex.Message); }
    }

    /// <summary>
    /// Brings a shortcut back from Recently deleted. Nothing that exists is overwritten: if its id is in use (in the library or as a
    /// file) it comes back as a copy with a new id and no keyboard shortcut, Quick Run name or Send To entry; if its name is taken it
    /// gets a number. When it keeps its id, a keyboard shortcut or Quick Run name another shortcut now uses is left off. Automations
    /// that used it work again only when it keeps its id.
    /// </summary>
    public RestoreResult Restore(DeletedShortcut entry)
    {
        var text = new UTF8Encoding(false).GetString(RecentlyDeleted.ReadData(entry)).TrimStart('﻿');
        Shortcut s;
        try { s = AutomationPortalJson.Deserialize<Shortcut>(text) ?? throw new LibraryException($"\"{entry.Name}\" is empty and can't be restored."); }
        catch (JsonException ex) { throw new LibraryException($"\"{entry.Name}\" is damaged and can't be restored.", ex); }

        var asCopy = false;
        string? renamedFrom = null, clearedHotkey = null, clearedAlias = null;
        lock (Gate)
        {
            if (s.Id == Guid.Empty || ShortcutMap.ContainsKey(s.Id) || _fileOf.ContainsKey(s.Id) || File.Exists(_paths.ShortcutFile(s.Id)))
            {
                do s.Id = Guid.NewGuid(); while (File.Exists(_paths.ShortcutFile(s.Id)));
                s.Hotkey = null; s.Alias = null; s.ShowInSendTo = false;
                asCopy = true;
            }
            else
            {
                if (HotkeyKey(s.Hotkey) is { } hk && ShortcutMap.Values.Any(x => HotkeyKey(x.Hotkey) == hk)) { clearedHotkey = s.Hotkey; s.Hotkey = null; }
                if (s.Alias is { } alias && !string.IsNullOrWhiteSpace(alias)
                    && ShortcutMap.Values.Any(x => string.Equals(x.Alias?.Trim(), alias.Trim(), StringComparison.OrdinalIgnoreCase)))
                { clearedAlias = alias; s.Alias = null; }
            }
            var baseName = string.IsNullOrWhiteSpace(s.Name) ? "Restored shortcut" : s.Name.Trim();
            var name = baseName;
            for (var n = 2; IsNameTaken(name); n++) name = $"{baseName} {n}";
            if (name != baseName) renamedFrom = baseName;
            s.Name = name;
            if (s.FolderId is { } fid && !FolderList.Any(f => f.Id == fid)) s.FolderId = null;

            _createOnly = true;
            try { SaveLocked(s); }
            finally { _createOnly = false; }
        }
        RaiseChanged(LibraryChange.Shortcuts);

        var removed = true;
        try { RecentlyDeleted.DeletePermanently(entry); }
        catch (LibraryException ex) { removed = false; StoreDiagnostics.Report(ex.Message); }
        return new RestoreResult(GetById(s.Id) ?? s, asCopy, renamedFrom, removed, clearedHotkey, clearedAlias);
    }

    /// <summary>A hotkey in a form where the same keys compare equal ("Alt + Ctrl+K" and "ctrl+alt+k").</summary>
    private static string? HotkeyKey(string? hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey)) return null;
        return string.Join("+", hotkey.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.ToLowerInvariant() switch { "control" => "ctrl", "windows" or "lwin" or "rwin" => "win", var x => x })
            .Order(StringComparer.Ordinal));
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
            if (path.StartsWith(RecentlyDeleted.Folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(Versions.Folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || IsOurOwnWrite(path)) return;
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
