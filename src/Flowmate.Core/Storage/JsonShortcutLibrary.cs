using System.Collections.Concurrent;
using System.Text.Json;

namespace Flowmate.Core;

/// <summary>Library stored as files: shortcuts\&lt;id&gt;.json, folders.json, automations.json, snippets.json. Atomic writes; reloads when files change outside the app.</summary>
public sealed class JsonShortcutLibrary : ShortcutLibraryBase, IDisposable
{
    private readonly FlowmatePaths _paths;
    private readonly ConcurrentDictionary<string, DateTime> _selfWrites = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;

    /// <summary>Problems found while loading (corrupt files are skipped, never deleted).</summary>
    public List<string> LoadErrors { get; } = new();

    public JsonShortcutLibrary(FlowmatePaths paths, ActionRegistry? registry = null, bool watch = true) : base(registry)
    {
        _paths = paths;
        Directory.CreateDirectory(paths.ShortcutsFolder);
        Load();
        if (watch) StartWatching();
    }

    private string SnippetsFile => Path.Combine(_paths.LibraryFolder, "snippets.json");

    private void Load()
    {
        var map = new Dictionary<Guid, Shortcut>();
        var errors = new List<string>();
        foreach (var file in Directory.EnumerateFiles(_paths.ShortcutsFolder, "*.json"))
        {
            try
            {
                var s = FlowmateJson.Deserialize<Shortcut>(File.ReadAllText(file));
                if (s is not null) map[s.Id] = s;
            }
            catch (Exception ex) when (ex is JsonException or IOException) { errors.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
        }
        lock (Gate)
        {
            ShortcutMap = map;
            FolderList = ReadList<ShortcutFolder>(_paths.FoldersFile, errors);
            AutomationList = ReadList<Automation>(_paths.AutomationsFile, errors);
            SnippetList = ReadList<Snippet>(SnippetsFile, errors);
            LoadErrors.Clear();
            LoadErrors.AddRange(errors);
        }
    }

    private static List<T> ReadList<T>(string file, List<string> errors)
    {
        if (!File.Exists(file)) return new();
        try { return FlowmateJson.Deserialize<List<T>>(File.ReadAllText(file)) ?? new(); }
        catch (Exception ex) when (ex is JsonException or IOException) { errors.Add($"{Path.GetFileName(file)}: {ex.Message}"); return new(); }
    }

    private void Write(string path, string text)
    {
        _selfWrites[path] = DateTime.UtcNow;
        AtomicFile.WriteAllText(path, text);
    }

    protected override void PersistShortcut(Shortcut s) => Write(_paths.ShortcutFile(s.Id), FlowmateJson.Serialize(s, indented: true));
    protected override void PersistShortcutDeleted(Guid id)
    {
        var path = _paths.ShortcutFile(id);
        _selfWrites[path] = DateTime.UtcNow;
        try { File.Delete(path); } catch (IOException) { }
    }
    protected override void PersistFolders() => Write(_paths.FoldersFile, FlowmateJson.Serialize(FolderList, indented: true));
    protected override void PersistAutomations() => Write(_paths.AutomationsFile, FlowmateJson.Serialize(AutomationList, indented: true));
    protected override void PersistSnippets() => Write(SnippetsFile, FlowmateJson.Serialize(SnippetList, indented: true));

    private void StartWatching()
    {
        Directory.CreateDirectory(_paths.LibraryFolder);
        _watcher = new FileSystemWatcher(_paths.LibraryFolder, "*.json") { IncludeSubdirectories = true, EnableRaisingEvents = true };
        FileSystemEventHandler h = (_, e) => OnFileEvent(e.FullPath);
        _watcher.Changed += h; _watcher.Created += h; _watcher.Deleted += h;
        _watcher.Renamed += (_, e) => OnFileEvent(e.FullPath);
    }

    private void OnFileEvent(string path)
    {
        if (_selfWrites.TryGetValue(path, out var when) && DateTime.UtcNow - when < TimeSpan.FromSeconds(3)) return;
        _debounce?.Dispose();
        _debounce = new Timer(_ => { Load(); RaiseChanged(LibraryChange.Shortcuts | LibraryChange.Folders | LibraryChange.Automations | LibraryChange.Snippets, external: true); }, null, 400, Timeout.Infinite);
    }

    /// <summary>Re-reads everything from disk and raises Changed.</summary>
    public void Reload()
    {
        Load();
        RaiseChanged(LibraryChange.Shortcuts | LibraryChange.Folders | LibraryChange.Automations | LibraryChange.Snippets, external: true);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
    }
}
