namespace Flowmate.Triggers;

/// <summary>Spots files that are still being written: browser downloads, temp files, Office lock files, sync placeholders.</summary>
public static class PartialDownloadFilter
{
    private static readonly string[] Extensions =
        { ".crdownload", ".part", ".partial", ".tmp", ".temp", ".download", ".opdownload", ".filepart", ".!ut", ".bc!", ".aria2" };

    public static bool IsPartial(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Length == 0) return false;
        if (Flowmate.Core.PathSafety.IsWorkInProgressName(name)) return true;   // Flow Mate's own temp files and folder copies being built
        if (name.StartsWith("~$", StringComparison.Ordinal)) return true;
        if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)) return true;
        return Extensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Looks at files on disk. Replaced by a fake in tests.</summary>
internal interface IFileProbe
{
    bool Exists(string path);
    long? Size(string path);
    /// <summary>True when nothing else holds the file open for writing.</summary>
    bool CanOpen(string path);
}

internal sealed class DiskFileProbe : IFileProbe
{
    public bool Exists(string path) => File.Exists(path);

    public long? Size(string path)
    {
        try { return new FileInfo(path) is { Exists: true } f ? f.Length : null; } catch { return null; }
    }

    public bool CanOpen(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (FileNotFoundException) { return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return true; } // can't write-lock it, but it's not being downloaded either
    }
}

internal sealed record FileChangeBatch(IReadOnlyList<string> Added, IReadOnlyList<string> Changed, IReadOnlyList<string> Removed)
{
    public bool IsEmpty => Added.Count + Changed.Count + Removed.Count == 0;
    public IEnumerable<string> All => Added.Concat(Changed).Concat(Removed);
}

/// <summary>
/// Turns a flood of file-system events into calm batches: partial downloads are ignored, a file is only reported once its size
/// has stopped changing and it can be opened, and everything that happens within the debounce time becomes one batch.
/// Pure logic: feed events, call <see cref="Poll"/> a few times per second.
/// </summary>
internal sealed class FolderChangeBatcher
{
    private enum Kind { Added, Changed, Removed }

    private sealed class Pending
    {
        public Kind Kind;
        public DateTime LastEvent;
        public DateTime FirstSeen;
        public bool New;
        public long? LastSize;
        public DateTime SizeSince;
    }

    private readonly TimeSpan _debounce;
    private readonly TimeSpan _stableFor;
    private readonly TimeSpan _giveUpAfter;
    private readonly IFileProbe _probe;
    private readonly Func<DateTime> _now;
    private readonly Func<string, bool> _accept;
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public FolderChangeBatcher(TimeSpan debounce, IFileProbe probe, Func<string, bool>? accept = null, Func<DateTime>? now = null,
        TimeSpan? stableFor = null, TimeSpan? giveUpAfter = null)
    {
        _debounce = debounce;
        _probe = probe;
        _accept = accept ?? (_ => true);
        _now = now ?? (() => DateTime.UtcNow);
        _stableFor = stableFor ?? TimeSpan.FromSeconds(1);
        _giveUpAfter = giveUpAfter ?? TimeSpan.FromMinutes(10);
    }

    public bool HasPending { get { lock (_gate) return _pending.Count > 0; } }

    /// <summary>
    /// A batch holds at most this many paths. No path is ever dropped: when more than this many wait, the ready ones are delivered
    /// in batches of this size, one after another, even while others are still arriving.
    /// </summary>
    internal const int MaxBatch = 1000;

    public void OnCreated(string path) => Note(path, Kind.Added);
    public void OnChanged(string path) => Note(path, Kind.Changed);
    public void OnDeleted(string path) => Note(path, Kind.Removed);

    /// <summary>A rename counts as the new name appearing (a finished download is its .crdownload renamed) and the old name leaving.</summary>
    public void OnRenamed(string oldPath, string newPath)
    {
        OnDeleted(oldPath);
        OnCreated(newPath);
    }

    private void Note(string path, Kind kind)
    {
        if (PartialDownloadFilter.IsPartial(path) || !_accept(path)) return;
        lock (_gate)
        {
            var now = _now();
            if (!_pending.TryGetValue(path, out var p))
            {
                // One entry per path; Poll delivers in batches once many wait.
                _pending[path] = new Pending { Kind = kind, New = kind == Kind.Added, LastEvent = now, FirstSeen = now, SizeSince = now };
                return;
            }
            p.LastEvent = now;
            p.Kind = (p.Kind, kind) switch
            {
                (Kind.Added, Kind.Removed) => Kind.Removed,     // came and went: resolved at Poll time
                (Kind.Added, _) => Kind.Added,
                (Kind.Changed, Kind.Added) => Kind.Added,
                (Kind.Changed, _) => kind,
                (Kind.Removed, Kind.Added) => Kind.Changed,     // deleted and re-created = replaced
                (Kind.Removed, _) => Kind.Removed,
                _ => kind,
            };
        }
    }

    /// <summary>Returns the next finished batch, or null while files are still arriving or being written.</summary>
    public FileChangeBatch? Poll()
    {
        lock (_gate)
        {
            if (_pending.Count == 0) return null;
            var now = _now();
            var ready = new List<string>();
            var waiting = false;
            var dropped = new List<string>();

            foreach (var (path, p) in _pending)
            {
                if (now - p.LastEvent < _debounce) { waiting = true; continue; }
                if (p.Kind == Kind.Removed)
                {
                    if (_probe.Exists(path)) p.Kind = Kind.Changed; // it came back
                    else if (p.New) { dropped.Add(path); continue; } // appeared and vanished inside one burst
                    else { ready.Add(path); continue; }
                }
                if (!_probe.Exists(path)) { dropped.Add(path); continue; }

                var size = _probe.Size(path);
                if (size != p.LastSize) { p.LastSize = size; p.SizeSince = now; }
                if (now - p.SizeSince >= _stableFor && _probe.CanOpen(path)) ready.Add(path);
                else if (now - p.FirstSeen > _giveUpAfter) dropped.Add(path);
                else waiting = true;
            }

            foreach (var d in dropped) _pending.Remove(d);
            // Usually one burst becomes one batch. Past MaxBatch waiting paths, what is ready goes now, MaxBatch at a time.
            if (waiting && _pending.Count <= MaxBatch) return null;
            if (ready.Count == 0) return null;

            var added = new List<string>();
            var changed = new List<string>();
            var removed = new List<string>();
            foreach (var path in ready.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Take(MaxBatch))
            {
                switch (_pending[path].Kind)
                {
                    case Kind.Added: added.Add(path); break;
                    case Kind.Changed: changed.Add(path); break;
                    default: removed.Add(path); break;
                }
                _pending.Remove(path);
            }
            return new FileChangeBatch(added, changed, removed);
        }
    }
}
