using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
    /// <summary>SHA-256 (hex) of <see cref="Backup"/> when it was made, so undo can tell a damaged or swapped copy.</summary>
    public string? BackupSha256 { get; init; }
    public DateTimeOffset At { get; init; }
}

/// <summary>
/// Size and last write time of a file, or of everything in a folder (file count, total size, newest write), plus a SHA-256 of the
/// contents when they are no larger than <see cref="FileJournal.MaxBackupBytes"/>.
/// </summary>
public sealed record FileFingerprint
{
    public string Path { get; init; } = "";
    public bool IsFolder { get; init; }
    public long Length { get; init; }
    public int Files { get; init; }
    public DateTime LastWriteUtc { get; init; }
    /// <summary>SHA-256 (hex) of the file, or of every file in the folder with its relative name; null when too large or unreadable.</summary>
    public string? Sha256 { get; init; }

    /// <summary>The fingerprint of what is at <paramref name="path"/> now (with its hash), or null when nothing is there or it can't be read.</summary>
    public static FileFingerprint? Of(string path) => Of(path, withHash: true);

    internal static FileFingerprint? Of(string path, bool withHash)
    {
        try
        {
            if (File.Exists(path))
            {
                var f = new FileInfo(path);
                return new FileFingerprint
                {
                    Path = path, Length = f.Length, Files = 1, LastWriteUtc = f.LastWriteTimeUtc,
                    Sha256 = withHash && f.Length <= FileJournal.MaxBackupBytes ? HashOf(path, false) : null,
                };
            }
            if (!Directory.Exists(path)) return null;
            long length = 0;
            var files = 0;
            var newest = DateTime.MinValue;
            foreach (var f in FilesUnder(path))
            {
                files++;
                length += f.Length;
                if (f.LastWriteTimeUtc > newest) newest = f.LastWriteTimeUtc;
            }
            return new FileFingerprint
            {
                Path = path, IsFolder = true, Length = length, Files = files, LastWriteUtc = newest,
                Sha256 = withHash && length <= FileJournal.MaxBackupBytes ? HashOf(path, true) : null,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Same kind, size, file count and write time, and the same hash when both have one; the path isn't compared.</summary>
    public bool SameContentAs(FileFingerprint? other) =>
        other is not null && IsFolder == other.IsFolder && Length == other.Length && Files == other.Files && LastWriteUtc == other.LastWriteUtc
        && (Sha256 is null || other.Sha256 is null || string.Equals(Sha256, other.Sha256, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when what is at <paramref name="path"/> now has this size and date and, when this fingerprint has a hash, the same contents.</summary>
    public bool Matches(string path)
    {
        if (!SameContentAs(Of(path, withHash: false))) return false;
        return Sha256 is null || string.Equals(Sha256, HashOf(path, IsFolder), StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<FileInfo> FilesUnder(string root)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var f in dir.EnumerateFiles()) yield return f;
            foreach (var d in dir.EnumerateDirectories())
                if (!d.Attributes.HasFlag(FileAttributes.ReparsePoint)) pending.Push(d);
        }
    }

    private static string? HashOf(string path, bool folder)
    {
        try
        {
            if (!folder) return FileJournal.Sha256Hex(path);
            var root = System.IO.Path.GetFullPath(path).TrimEnd('\\', '/');
            var files = FilesUnder(root).Select(f => (Relative: f.FullName[(root.Length + 1)..], f.FullName))
                .OrderBy(f => f.Relative, StringComparer.OrdinalIgnoreCase).ToList();
            using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var (relative, full) in files)
            {
                h.AppendData(Encoding.UTF8.GetBytes(relative.ToUpperInvariant() + "\0"));
                using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
                h.AppendData(SHA256.HashData(fs));
            }
            return Convert.ToHexString(h.GetHashAndReset());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
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
/// The journal files are treated as untrusted input by undo: they live in a user-writable folder.
/// </summary>
public sealed class FileJournal
{
    public const int KeepRuns = 20;
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(7);
    /// <summary>Larger files aren't copied before they are replaced (undo then points to the Recycle Bin), nor hashed for the "unchanged" check.</summary>
    public const long MaxBackupBytes = 256L * 1024 * 1024;
    /// <summary>A journal or backup folder written this recently is never removed, and an expired one only by a purge this long after the purge that first saw it expired.</summary>
    internal static readonly TimeSpan PurgeConfirmAfter = TimeSpan.FromHours(24);
    private static readonly TimeSpan StaleUndoClaim = TimeSpan.FromDays(1);
    private const string StateFileName = "purge-state.dat";

    private static readonly object Gate = new();
    private static readonly Dictionary<string, int> ActiveRuns = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> UndoingNow = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<long> ProcessStarted = new(() =>
    {
        using var p = Process.GetCurrentProcess();
        return p.StartTime.ToUniversalTime().Ticks;
    });
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
        public DateTimeOffset? Started { get; set; }
        public FileChange? Change { get; set; }
        public List<FileFingerprint>? Final { get; set; }
        public DateTimeOffset? UndoneAt { get; set; }
        public UndoClaim? Undoing { get; set; }
        public DateTimeOffset? UndoStopped { get; set; }
    }

    private sealed class UndoClaim
    {
        public DateTimeOffset At { get; set; }
        public int ProcessId { get; set; }
        public long ProcessStarted { get; set; }
    }

    private sealed class PurgeMark
    {
        public DateTime? JournalWritten { get; set; }
        public DateTimeOffset SeenExpiredAt { get; set; }
    }

    private string FileFor(string runId) => Path.Combine(Folder, Id(runId) + ".jsonl");
    public string BackupFolderFor(string runId) => Path.Combine(Folder, Id(runId) + ".backup");

    private static string Id(string runId) =>
        IdRx.IsMatch(runId) ? runId : throw new ArgumentException("A run id has only letters, digits and dashes.", nameof(runId));

    public bool Has(string runId) => IdRx.IsMatch(runId) && File.Exists(FileFor(runId));

    public void Append(string runId, FileChange change) => Write(runId, new Line { Change = change });
    public void Seal(string runId, IEnumerable<FileFingerprint> final) => Write(runId, new Line { Final = final.ToList() });
    public void MarkUndone(string runId, DateTimeOffset when) => Write(runId, new Line { UndoneAt = when });

    /// <summary>Writes the run's first line if it has no journal yet, so its backup folder never exists without one.</summary>
    public void Start(string runId, DateTimeOffset at)
    {
        lock (Gate)
            if (!File.Exists(FileFor(runId))) Write(runId, new Line { Started = at });
    }

    /// <summary>A run that is still going: <see cref="Purge"/> never touches its journal or backups.</summary>
    internal void RunStarted(string runId)
    {
        var key = FileFor(runId);
        lock (ActiveRuns) ActiveRuns[key] = ActiveRuns.GetValueOrDefault(key) + 1;
    }

    internal void RunEnded(string runId)
    {
        var key = FileFor(runId);
        lock (ActiveRuns)
        {
            if (!ActiveRuns.TryGetValue(key, out var n)) return;
            if (n <= 1) ActiveRuns.Remove(key); else ActiveRuns[key] = n - 1;
        }
    }

    private bool IsActive(string runId)
    {
        lock (ActiveRuns) return ActiveRuns.ContainsKey(FileFor(runId));
    }

    private void Write(string runId, Line line)
    {
        var file = FileFor(runId);
        lock (Gate)
        {
            Directory.CreateDirectory(Folder);
            // A line cut short (the app closed mid-write) must not swallow the next one.
            File.AppendAllText(file, (EndsMidLine(file) ? "\n" : "") + JsonSerializer.Serialize(line, Json) + "\n");
        }
    }

    private static bool EndsMidLine(string file)
    {
        if (!File.Exists(file)) return false;
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fs.Length == 0) return false;
        fs.Seek(-1, SeekOrigin.End);
        return fs.ReadByte() != '\n';
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
        return Parse(runId, lines).Run;
    }

    /// <summary>Lines that aren't valid (damaged, cut short, or from a newer version) are skipped.</summary>
    private static (JournaledRun Run, UndoClaim? Claim, DateTimeOffset? Stopped) Parse(string runId, IEnumerable<string> lines)
    {
        var changes = new List<FileChange>();
        Dictionary<string, FileFingerprint>? final = null;
        DateTimeOffset? undone = null, stopped = null;
        UndoClaim? claim = null;
        foreach (var text in lines)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            Line? l;
            try { l = JsonSerializer.Deserialize<Line>(text, Json); }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException) { continue; }
            if (l is null) continue;
            if (l.Change is { } c && !string.IsNullOrEmpty(c.Path)) changes.Add(c);
            if (l.Final is { } f)
                final = f.Where(x => x is not null && !string.IsNullOrEmpty(x.Path))
                    .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
            if (l.UndoneAt is { } u) undone = u;
            if (l.Undoing is { } claimed) claim = claimed;
            if (l.UndoStopped is { } s) stopped = s;
        }
        return (new JournaledRun { RunId = runId, Changes = changes, Final = final, UndoneAt = undone }, claim, stopped);
    }

    /// <summary>True while this app is undoing the run.</summary>
    public bool IsUndoing(string runId)
    {
        if (!IdRx.IsMatch(runId)) return false;
        lock (UndoingNow) return UndoingNow.Contains(FileFor(runId));
    }

    /// <summary>
    /// Claims the run for undo: in this app, and on disk so another copy of the app sees it too. Null when claimed; else why not.
    /// Every successful claim must be followed by <see cref="EndUndo"/>.
    /// </summary>
    public string? BeginUndo(string runId)
    {
        var Busy = Strings.Get("Journal_Undo_Busy");
        var file = FileFor(runId);
        lock (UndoingNow)
            if (!UndoingNow.Add(file)) return Busy;
        string? problem = null;
        try
        {
            lock (Gate)
            {
                // Held without write sharing from reading to appending, so another copy of the app can't claim it in between.
                using var fs = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                string text;
                using (var reader = new StreamReader(fs, Encoding.UTF8, false, 4096, leaveOpen: true)) text = reader.ReadToEnd();
                var (run, claim, stopped) = Parse(runId, text.Split('\n'));
                if (run.UndoneAt is not null) problem = Strings.Get("Journal_Undo_Already");
                else if (claim is not null && ClaimLive(claim, stopped)) problem = Busy;
                else
                {
                    var line = JsonSerializer.Serialize(new Line
                    {
                        Undoing = new UndoClaim { At = DateTimeOffset.Now, ProcessId = Environment.ProcessId, ProcessStarted = ProcessStarted.Value },
                    }, Json);
                    var bytes = new UTF8Encoding(false).GetBytes((text.Length > 0 && text[^1] != '\n' ? "\n" : "") + line + "\n");
                    fs.Seek(0, SeekOrigin.End);
                    fs.Write(bytes);
                    fs.Flush(flushToDisk: true);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = Strings.Format("Journal_Undo_CantOpen", ex.Message);
        }
        if (problem is not null)
            lock (UndoingNow) UndoingNow.Remove(file);
        return problem;
    }

    /// <summary>Ends an undo claimed by <see cref="BeginUndo"/>: marks the run undone when every step ran, else releases the claim.</summary>
    public void EndUndo(string runId, bool finished)
    {
        try { Write(runId, finished ? new Line { UndoneAt = DateTimeOffset.Now } : new Line { UndoStopped = DateTimeOffset.Now }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StoreDiagnostics.Report("Couldn't note how undoing a run ended: " + ex.Message, ex); }
        finally
        {
            lock (UndoingNow) UndoingNow.Remove(FileFor(runId));
        }
    }

    /// <summary>A claim by another running copy of the app. This app's own claims are tracked in memory.</summary>
    private static bool ClaimLive(UndoClaim claim, DateTimeOffset? stopped)
    {
        if (stopped is { } s && s >= claim.At) return false;
        if (DateTimeOffset.Now - claim.At > StaleUndoClaim) return false;
        if (claim.ProcessId == Environment.ProcessId && claim.ProcessStarted == ProcessStarted.Value) return false;
        try
        {
            using var p = Process.GetProcessById(claim.ProcessId);
            return p.StartTime.ToUniversalTime().Ticks == claim.ProcessStarted;
        }
        catch (ArgumentException) { return false; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { return true; }
    }

    /// <summary>
    /// Copies a file into the run's backup folder before it is replaced or changed. Returns the copy, or null when the file is
    /// too big or couldn't be copied. Never copies over anything.
    /// </summary>
    public string? KeepBackup(string runId, string path) => KeepBackupWithHash(runId, path)?.Path;

    /// <summary>The copy and the SHA-256 of its contents, checked by reading the copy back.</summary>
    internal (string Path, string Sha256)? KeepBackupWithHash(string runId, string path)
    {
        string? target = null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxBackupBytes) return null;
            Start(runId, DateTimeOffset.Now);
            var dir = BackupFolderFor(runId);
            Directory.CreateDirectory(dir);
            var candidate = Path.Combine(dir, Guid.NewGuid().ToString("N")[..8] + "-" + info.Name);
            string hash;
            using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var copy = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                target = candidate;
                using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    h.AppendData(buffer, 0, read);
                    copy.Write(buffer, 0, read);
                }
                copy.Flush(flushToDisk: true);
                hash = Convert.ToHexString(h.GetHashAndReset());
            }
            File.SetLastWriteTimeUtc(target, info.LastWriteTimeUtc);
            if (!string.Equals(Sha256Hex(target), hash, StringComparison.OrdinalIgnoreCase))
            {
                DeleteOwnCopy(target);
                return null;
            }
            return (target, hash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (target is not null) DeleteOwnCopy(target);
            return null;
        }
    }

    // Only ever called on a copy this class just created with FileMode.CreateNew under a new unique name in its own backup folder.
    private static void DeleteOwnCopy(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    internal static string Sha256Hex(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    /// <summary>
    /// Removes journals beyond the newest <see cref="KeepRuns"/> and those older than <see cref="KeepFor"/>, with their backup folders,
    /// and backup folders whose journal is gone. Only touches <c>*.jsonl</c> files and <c>*.backup</c> folders named after a run id
    /// directly in <see cref="Folder"/>. Runs still going, and anything written in the last <see cref="PurgeConfirmAfter"/> (or in the
    /// future), are never removed. Removal takes two purges so a clock that jumps ahead once can't remove everything: one notes an
    /// item as expired, and a later one at least <see cref="PurgeConfirmAfter"/> after that removes it.
    /// </summary>
    public int Purge(DateTimeOffset now)
    {
        if (!Directory.Exists(Folder)) return 0;
        var removed = 0;
        lock (Gate)
        {
            var dir = new DirectoryInfo(Folder);
            if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) return 0;
            var journals = dir.EnumerateFiles("*.jsonl").Where(f => IdRx.IsMatch(Path.GetFileNameWithoutExtension(f.Name)))
                .OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            var rank = new Dictionary<string, (FileInfo File, int Rank)>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < journals.Count; i++) rank[Path.GetFileNameWithoutExtension(journals[i].Name)] = (journals[i], i);
            var backups = dir.EnumerateDirectories("*.backup").Where(b => IdRx.IsMatch(b.Name[..^".backup".Length]))
                .ToDictionary(b => b.Name[..^".backup".Length], StringComparer.OrdinalIgnoreCase);

            var state = ReadState();
            var next = new Dictionary<string, PurgeMark>(StringComparer.OrdinalIgnoreCase);
            var remove = new List<string>();
            foreach (var id in rank.Keys.Union(backups.Keys, StringComparer.OrdinalIgnoreCase))
            {
                if (IsActive(id)) continue;
                var journal = rank.TryGetValue(id, out var j) ? j.File : null;
                var backup = backups.GetValueOrDefault(id);
                if (journal is not null && Recent(journal.LastWriteTimeUtc)) continue;
                if (backup is not null && (backup.Attributes.HasFlag(FileAttributes.ReparsePoint) || Recent(backup.LastWriteTimeUtc))) continue;
                var expired = journal is null || j.Rank >= KeepRuns || now.UtcDateTime - journal.LastWriteTimeUtc >= KeepFor;
                if (!expired) continue;
                var written = journal?.LastWriteTimeUtc;
                var mark = state.TryGetValue(id, out var m) && m.JournalWritten == written && m.SeenExpiredAt <= now ? m : null;
                if (mark is not null && now - mark.SeenExpiredAt >= PurgeConfirmAfter) remove.Add(id);
                else next[id] = mark ?? new PurgeMark { JournalWritten = written, SeenExpiredAt = now };
            }
            if (state.Count == 0 && next.Count == 0 && remove.Count == 0) return 0;
            try { WriteState(next); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StoreDiagnostics.Report("Couldn't save the file change record cleanup state, so nothing was removed. " + ex.Message, ex);
                return 0;
            }

            foreach (var id in remove)
            {
                if (rank.TryGetValue(id, out var j))
                {
                    try { j.File.Delete(); removed++; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                }
                if (backups.TryGetValue(id, out var b) && !b.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    // A backup folder this class made, named after the run: everything in it is the app's own copies.
                    try { b.Delete(recursive: true); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
        }
        return removed;

        bool Recent(DateTime writtenUtc) => writtenUtc > now.UtcDateTime || now.UtcDateTime - writtenUtc < PurgeConfirmAfter;
    }

    private Dictionary<string, PurgeMark> ReadState()
    {
        var result = new Dictionary<string, PurgeMark>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var path = Path.Combine(Folder, StateFileName);
            if (!File.Exists(path)) return result;
            if (JsonSerializer.Deserialize<Dictionary<string, PurgeMark>>(File.ReadAllText(path), Json) is { } marks)
                foreach (var (k, v) in marks) if (v is not null) result[k] = v;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { }
        return result;
    }

    private void WriteState(Dictionary<string, PurgeMark> marks)
    {
        var path = Path.Combine(Folder, StateFileName);
        var temp = Path.Combine(Folder, $"{StateFileName}.{Guid.NewGuid():N}.tmp");
        using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            fs.Write(new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(marks, Json)));
            fs.Flush(flushToDisk: true);
        }
        // Replaces only this class's own state file (a fixed name that is never a journal or backup).
        try { File.Move(temp, path, overwrite: true); }
        catch
        {
            DeleteOwnCopy(temp);
            throw;
        }
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
    private readonly Dictionary<string, (string Path, string Sha256)?> _backups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<DateTimeOffset> _now;
    private bool _ended;

    public RunFileJournal(FileJournal store, string runId, string? ignoreRoot = null, Func<DateTimeOffset>? now = null)
    {
        _store = store;
        RunId = runId;
        _ignoreRoot = ignoreRoot is null ? null : Path.GetFullPath(ignoreRoot).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        _now = now ?? (() => DateTimeOffset.Now);
        store.RunStarted(runId);
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
            if (_backups.TryGetValue(full, out var existing)) return existing?.Path;
            var copy = _store.KeepBackupWithHash(RunId, full);
            _backups[full] = copy;
            return copy?.Path;
        }
    }

    /// <summary>
    /// True when this run already made, replaced or changed the item at this path, so a later change to its contents needs no new
    /// record. An item the run only moved or renamed here still needs one.
    /// </summary>
    public bool Recorded(string path)
    {
        var full = Path.GetFullPath(path);
        lock (_gate)
            return _changes.Any(c => c.Kind is FileChangeKind.Created or FileChangeKind.Replaced or FileChangeKind.Changed
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
        lock (_gate)
        {
            string? hash = null;
            if (backup is not null)
                foreach (var kept in _backups.Values)
                    if (kept is { } k && string.Equals(k.Path, backup, StringComparison.OrdinalIgnoreCase)) { hash = k.Sha256; break; }
            var change = new FileChange { Kind = kind, Path = full, From = from is null ? null : fullFrom, Backup = backup, BackupSha256 = hash, At = _now() };
            _changes.Add(change);
            try { _store.Append(RunId, change); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StoreDiagnostics.Report("Couldn't record a file change for undo: " + ex.Message, ex); }
        }
    }

    /// <summary>Called when the run ends: notes how every changed path looks now (so undo can tell later edits apart), then trims old journals.</summary>
    public void Seal()
    {
        List<string> paths;
        bool first;
        lock (_gate)
        {
            first = !_ended;
            _ended = true;
            paths = _changes.Where(c => c.Kind is FileChangeKind.Created or FileChangeKind.Moved or FileChangeKind.Renamed or FileChangeKind.Replaced)
                .Select(c => c.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (_changes.Count == 0)
            {
                if (first) _store.RunEnded(RunId);
                return;
            }
        }
        try
        {
            _store.Seal(RunId, paths.Select(FileFingerprint.Of).OfType<FileFingerprint>());
            if (first) _store.RunEnded(RunId);
            first = false;
            _store.Purge(_now());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StoreDiagnostics.Report("Couldn't finish the file change record: " + ex.Message, ex); }
        finally
        {
            if (first) _store.RunEnded(RunId);
        }
    }
}
