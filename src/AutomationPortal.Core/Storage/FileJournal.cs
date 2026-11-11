using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AutomationPortal.Core;

public enum FileChangeKind
{
    /// <summary>A new file or folder was made at <see cref="FileChange.Path"/>.</summary>
    Created,
    /// <summary>Moved from <see cref="FileChange.From"/> to <see cref="FileChange.Path"/>.</summary>
    Moved,
    /// <summary>Renamed from <see cref="FileChange.From"/> to <see cref="FileChange.Path"/>.</summary>
    Renamed,
    /// <summary>Sent to the Recycle Bin.</summary>
    Recycled,
    /// <summary>Deleted for good (the user chose "Delete permanently").</summary>
    Deleted,
    /// <summary>The item at <see cref="FileChange.Path"/> is new; the one it replaced is kept at <see cref="FileChange.Backup"/> (or only in the Recycle Bin when null).</summary>
    Replaced,
    /// <summary>Changed in place without a copy of the old version.</summary>
    Changed,
}

/// <summary>One change a run made to files. <see cref="Path"/> is where the item is after the change.</summary>
public sealed record FileChange
{
    public FileChangeKind Kind { get; init; }
    public string Path { get; init; } = "";
    public string? From { get; init; }
    public string? Backup { get; init; }
    public DateTimeOffset At { get; init; }
}

/// <summary>Size and last write time of a file, or of everything in a folder (file count, total size, newest write).</summary>
public sealed record FileFingerprint
{
    public string Path { get; init; } = "";
    public bool IsFolder { get; init; }
    public long Length { get; init; }
    public int Files { get; init; }
    public DateTime LastWriteUtc { get; init; }

    /// <summary>The fingerprint of what is at <paramref name="path"/> now, or null when nothing is there or it can't be read.</summary>
    public static FileFingerprint? Of(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var f = new FileInfo(path);
                return new FileFingerprint { Path = path, Length = f.Length, Files = 1, LastWriteUtc = f.LastWriteTimeUtc };
            }
            if (!Directory.Exists(path)) return null;
            long length = 0;
            var files = 0;
            var newest = DateTime.MinValue;
            void Walk(DirectoryInfo dir)
            {
                foreach (var f in dir.EnumerateFiles())
                {
                    files++;
                    length += f.Length;
                    if (f.LastWriteTimeUtc > newest) newest = f.LastWriteTimeUtc;
                }
                foreach (var d in dir.EnumerateDirectories())
                    if (!d.Attributes.HasFlag(FileAttributes.ReparsePoint)) Walk(d);
            }
            Walk(new DirectoryInfo(path));
            return new FileFingerprint { Path = path, IsFolder = true, Length = length, Files = files, LastWriteUtc = newest };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Same kind, size, file count and write time; the path isn't compared.</summary>
    public bool SameContentAs(FileFingerprint? other) =>
        other is not null && IsFolder == other.IsFolder && Length == other.Length && Files == other.Files && LastWriteUtc == other.LastWriteUtc;
}

/// <summary>What the journal holds for one run.</summary>
public sealed class JournaledRun
{
    public string RunId { get; init; } = "";
    public IReadOnlyList<FileChange> Changes { get; init; } = Array.Empty<FileChange>();
    /// <summary>The state of every changed path when the run ended, by full path; null when the run never finished (the app closed).</summary>
    public IReadOnlyDictionary<string, FileFingerprint>? Final { get; init; }
    public DateTimeOffset? UndoneAt { get; init; }
}

/// <summary>
/// Per-run journals of file changes, one <c>runId.jsonl</c> per run in the app's own folder, with copies of replaced files in
/// <c>runId.backup</c>. Only the latest <see cref="KeepRuns"/> runs of the last <see cref="KeepFor"/> are kept.
/// </summary>
public sealed class FileJournal
{
    public const int KeepRuns = 20;
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(7);
    /// <summary>Larger files aren't copied before they are replaced; undo then points to the Recycle Bin.</summary>
    public const long MaxBackupBytes = 256L * 1024 * 1024;

    private static readonly object Gate = new();
    private static readonly Regex IdRx = new("^[A-Za-z0-9-]{1,64}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public FileJournal(string folder) { Folder = Path.GetFullPath(folder); }
    public FileJournal(AutomationPortalPaths paths) : this(paths.JournalFolder) { }

    public string Folder { get; }

    private sealed class Line
    {
        public FileChange? Change { get; set; }
        public List<FileFingerprint>? Final { get; set; }
        public DateTimeOffset? UndoneAt { get; set; }
    }

    private string FileFor(string runId) => Path.Combine(Folder, Id(runId) + ".jsonl");
    public string BackupFolderFor(string runId) => Path.Combine(Folder, Id(runId) + ".backup");

    private static string Id(string runId) =>
        IdRx.IsMatch(runId) ? runId : throw new ArgumentException("A run id has only letters, digits and dashes.", nameof(runId));

    public bool Has(string runId) => IdRx.IsMatch(runId) && File.Exists(FileFor(runId));

    public void Append(string runId, FileChange change) => Write(runId, new Line { Change = change });
    public void Seal(string runId, IEnumerable<FileFingerprint> final) => Write(runId, new Line { Final = final.ToList() });
    public void MarkUndone(string runId, DateTimeOffset when) => Write(runId, new Line { UndoneAt = when });

    private void Write(string runId, Line line)
    {
        var file = FileFor(runId);
        lock (Gate)
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(file, JsonSerializer.Serialize(line, Json) + "\n");
        }
    }

    public JournaledRun? Read(string runId)
    {
        if (!Has(runId)) return null;
        string[] lines;
        lock (Gate)
        {
            try { lines = File.ReadAllLines(FileFor(runId)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        }
        var changes = new List<FileChange>();
        Dictionary<string, FileFingerprint>? final = null;
        DateTimeOffset? undone = null;
        foreach (var text in lines)
        {
            Line? l = null;
            try { l = JsonSerializer.Deserialize<Line>(text, Json); } catch (JsonException) { }
            if (l is null) continue;
            if (l.Change is { } c) changes.Add(c);
            if (l.Final is { } f) final = f.GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
            if (l.UndoneAt is { } u) undone = u;
        }
        return new JournaledRun { RunId = runId, Changes = changes, Final = final, UndoneAt = undone };
    }

    /// <summary>
    /// Copies a file into the run's backup folder before it is replaced or changed. Returns the copy, or null when the file is
    /// too big or couldn't be copied. Never copies over anything.
    /// </summary>
    public string? KeepBackup(string runId, string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxBackupBytes) return null;
            var dir = BackupFolderFor(runId);
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, Guid.NewGuid().ToString("N")[..8] + "-" + info.Name);
            File.Copy(path, target, overwrite: false);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Removes journals beyond the newest <see cref="KeepRuns"/> and those older than <see cref="KeepFor"/>, with their backup folders.
    /// Only touches <c>*.jsonl</c> files and <c>*.backup</c> folders named after a run id directly in <see cref="Folder"/>.
    /// </summary>
    public int Purge(DateTimeOffset now)
    {
        if (!Directory.Exists(Folder)) return 0;
        var removed = 0;
        lock (Gate)
        {
            var dir = new DirectoryInfo(Folder);
            var journals = dir.EnumerateFiles("*.jsonl").Where(f => IdRx.IsMatch(Path.GetFileNameWithoutExtension(f.Name)))
                .OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < journals.Count; i++)
            {
                var id = Path.GetFileNameWithoutExtension(journals[i].Name);
                if (i < KeepRuns && now.UtcDateTime - journals[i].LastWriteTimeUtc < KeepFor) { keep.Add(id); continue; }
                try { journals[i].Delete(); removed++; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { keep.Add(id); }
            }
            foreach (var b in dir.EnumerateDirectories("*.backup"))
            {
                var id = b.Name[..^".backup".Length];
                if (!IdRx.IsMatch(id) || keep.Contains(id) || b.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                // A backup folder this journal made for a run whose journal is gone: everything in it is the app's own copies.
                try { b.Delete(recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        return removed;
    }
}

/// <summary>
/// Records the file changes of one run (shared with the shortcuts it runs). Actions call it right after each change.
/// Recording never fails the run; paths in the app's temp folders aren't recorded.
/// </summary>
public sealed class RunFileJournal
{
    private readonly FileJournal _store;
    private readonly string? _ignoreRoot;
    private readonly object _gate = new();
    private readonly List<FileChange> _changes = new();
    private readonly Dictionary<string, string?> _backups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<DateTimeOffset> _now;

    public RunFileJournal(FileJournal store, string runId, string? ignoreRoot = null, Func<DateTimeOffset>? now = null)
    {
        _store = store;
        RunId = runId;
        _ignoreRoot = ignoreRoot is null ? null : Path.GetFullPath(ignoreRoot).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public string RunId { get; }
    public FileJournal Store => _store;
    public bool HasChanges { get { lock (_gate) return _changes.Count > 0; } }

    public void Created(string path) => Add(FileChangeKind.Created, path);
    public void Moved(string from, string to) => Add(FileChangeKind.Moved, to, from);
    public void Renamed(string from, string to) => Add(FileChangeKind.Renamed, to, from);
    public void Recycled(string path) => Add(FileChangeKind.Recycled, path);
    public void Deleted(string path) => Add(FileChangeKind.Deleted, path);
    public void Changed(string path) => Add(FileChangeKind.Changed, path);
    public void Replaced(string path, string? backup) => Add(FileChangeKind.Replaced, path, backup: backup);

    /// <summary>
    /// Keeps a copy of a file about to be replaced or changed in place. The first copy made in a run is reused, so undo returns the
    /// file to how it was before the run. Null when no copy could be kept (too big, unreadable) or the path isn't recorded.
    /// </summary>
    public string? BackUp(string path)
    {
        var full = Path.GetFullPath(path);
        if (Ignored(full)) return null;
        lock (_gate)
        {
            if (_backups.TryGetValue(full, out var existing)) return existing;
            var copy = _store.KeepBackup(RunId, full);
            _backups[full] = copy;
            return copy;
        }
    }

    /// <summary>True when this run already made or replaced the item at this path, so a later change to it needs no new record.</summary>
    public bool Recorded(string path)
    {
        var full = Path.GetFullPath(path);
        lock (_gate)
            return _changes.Any(c => c.Kind is FileChangeKind.Created or FileChangeKind.Replaced or FileChangeKind.Moved or FileChangeKind.Renamed or FileChangeKind.Changed
                                     && string.Equals(c.Path, full, StringComparison.OrdinalIgnoreCase));
    }

    private bool Ignored(string full) => _ignoreRoot is not null && full.StartsWith(_ignoreRoot, StringComparison.OrdinalIgnoreCase);

    private void Add(FileChangeKind kind, string path, string? from = null, string? backup = null)
    {
        string full, fullFrom;
        try
        {
            full = Path.GetFullPath(path);
            fullFrom = from is null ? "" : Path.GetFullPath(from);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return; }
        if (Ignored(full) && (from is null || Ignored(fullFrom))) return;
        var change = new FileChange { Kind = kind, Path = full, From = from is null ? null : fullFrom, Backup = backup, At = _now() };
        lock (_gate)
        {
            _changes.Add(change);
            try { _store.Append(RunId, change); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StoreDiagnostics.Report("Couldn't record a file change for undo: " + ex.Message, ex); }
        }
    }

    /// <summary>Called when the run ends: notes how every changed path looks now (so undo can tell later edits apart), then trims old journals.</summary>
    public void Seal()
    {
        List<string> paths;
        lock (_gate)
        {
            if (_changes.Count == 0) return;
            paths = _changes.Where(c => c.Kind is FileChangeKind.Created or FileChangeKind.Moved or FileChangeKind.Renamed or FileChangeKind.Replaced)
                .Select(c => c.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        try
        {
            _store.Seal(RunId, paths.Select(FileFingerprint.Of).OfType<FileFingerprint>());
            _store.Purge(_now());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StoreDiagnostics.Report("Couldn't finish the file change record: " + ex.Message, ex); }
    }
}
