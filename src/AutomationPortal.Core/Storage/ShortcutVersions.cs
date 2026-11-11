using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace AutomationPortal.Core;

/// <summary>An earlier version of a shortcut, kept by <see cref="ShortcutVersions"/>.</summary>
public sealed class ShortcutVersion
{
    internal ShortcutVersion(Guid shortcutId, string key, DateTimeOffset savedAt, DateTimeOffset editedAt, string name, int stepCount, string changes, bool canOpen)
    {
        ShortcutId = shortcutId; Key = key; SavedAt = savedAt; EditedAt = editedAt; Name = name; StepCount = stepCount; Changes = changes; CanOpen = canOpen;
    }

    public Guid ShortcutId { get; }
    /// <summary>The file name inside the shortcut's versions folder.</summary>
    public string Key { get; }
    /// <summary>When a newer save replaced this version (the age limit counts from here).</summary>
    public DateTimeOffset SavedAt { get; }
    /// <summary>When this version itself was last saved; <see cref="SavedAt"/> when that isn't known.</summary>
    public DateTimeOffset EditedAt { get; }
    public string Name { get; }
    public int StepCount { get; }
    /// <summary>A short note of what changed compared with the version before it, for example "+2 steps, renamed".</summary>
    public string Changes { get; }
    /// <summary>False when the file can't be read as a shortcut.</summary>
    public bool CanOpen { get; }
}

/// <summary>
/// Earlier versions of each shortcut, kept on this PC only in <see cref="AutomationPortalPaths.VersionsFolder"/>\&lt;id&gt;\ (outside the
/// library, so a synced library isn't cluttered and saving a version never reloads the library). Each version is the shortcut file as
/// it was, under a name made from the time it was replaced. Files are only ever created, never replaced. Only "*.json" files directly
/// inside a "&lt;id&gt;" folder are ever removed, by <see cref="Purge"/>.
/// </summary>
public sealed class ShortcutVersions
{
    public const int MaxPerShortcut = 30;
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(90);
    /// <summary>A version is removed only by a purge at least this long after the purge that first saw it was due.</summary>
    internal static readonly TimeSpan PurgeConfirmAfter = TimeSpan.FromHours(24);
    private const string DataExtension = ".json";
    private const string StampFormat = "yyyyMMdd'T'HHmmssfff'Z'";
    private const string StateFileName = "purge-state.dat";

    private readonly object _gate = new();

    public ShortcutVersions(string folder) { Folder = Path.GetFullPath(folder).TrimEnd('\\', '/'); }

    public string Folder { get; }

    private sealed class PurgeMark
    {
        public DateTimeOffset SavedAt { get; set; }
        /// <summary>When a purge first saw it was due.</summary>
        public DateTimeOffset SeenDueAt { get; set; }
    }

    private sealed class PurgeState
    {
        public Dictionary<string, PurgeMark> Marks { get; set; } = new();
    }

    private readonly record struct VersionFile(string Path, string Key, DateTimeOffset SavedAt, int Number);

    /// <summary>The versions kept for a shortcut, newest first.</summary>
    public IReadOnlyList<ShortcutVersion> List(Guid shortcutId)
    {
        lock (_gate)
        {
            var result = new List<ShortcutVersion>();
            Shortcut? older = null;
            foreach (var f in Files(shortcutId).AsEnumerable().Reverse())
            {
                var s = Parse(StoreFile.ReadBytes(f.Path));
                var changes = s is null ? "Can't be read" : Describe(older, s);
                result.Add(new ShortcutVersion(shortcutId, f.Key, f.SavedAt, s is { ModifiedAt: var m } && m != default ? m : f.SavedAt,
                    s?.Name ?? "Unknown", s is null ? 0 : StepCount(s), changes, s is not null));
                if (s is not null) older = s;
            }
            result.Reverse();
            return result;
        }
    }

    /// <summary>Reads a version as a shortcut. Throws <see cref="LibraryException"/> when it's gone or damaged.</summary>
    public Shortcut Read(ShortcutVersion version)
    {
        var r = StoreFile.ReadBytes(OwnFile(version.ShortcutId, version.Key));
        if (r.Status == StoreReadStatus.Missing) throw new LibraryException("That version isn't in the history any more.");
        if (r.Status != StoreReadStatus.Ok) throw new LibraryException($"Couldn't read that version. {r.Error}");
        return Parse(r) ?? throw new LibraryException("That version is damaged and can't be opened.");
    }

    /// <summary>
    /// Keeps <paramref name="content"/> (a shortcut file's bytes) as a new version, written straight to disk under a name not used yet and
    /// checked by reading it back. Nothing is kept when it matches <paramref name="replacedBy"/> or the newest kept version (ignoring the
    /// save time and its place in the library). Returns true when a version was written. Throws when it can't be written.
    /// </summary>
    internal bool Keep(Guid shortcutId, byte[] content, DateTimeOffset now, byte[]? replacedBy = null)
    {
        if (shortcutId == Guid.Empty) return false;
        var hash = ContentHash(content);
        if (replacedBy is not null && ContentHash(replacedBy) == hash) return false;
        lock (_gate)
        {
            var files = Files(shortcutId);
            if (files.Count > 0 && StoreFile.ReadBytes(files[0].Path) is { Status: StoreReadStatus.Ok } newest && ContentHash(newest.Bytes!) == hash)
                return false;
            EnsureFolder(shortcutId);
            var stem = now.UtcDateTime.ToString(StampFormat, CultureInfo.InvariantCulture);
            for (var n = 1; n < 1000; n++)
            {
                var path = OwnFile(shortcutId, n == 1 ? stem + DataExtension : $"{stem} ({n}){DataExtension}");
                if (File.Exists(path) || Directory.Exists(path)) continue;
                FileStream fs;
                try { fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough); }
                catch (IOException) when (File.Exists(path)) { continue; }
                try
                {
                    using (fs)
                    {
                        fs.Write(content);
                        fs.Flush(flushToDisk: true);
                    }
                    var back = StoreFile.ReadBytes(path);
                    if (back.Status != StoreReadStatus.Ok || !content.AsSpan().SequenceEqual(back.Bytes))
                        throw new LibraryException("A version couldn't be saved safely.");
                }
                catch
                {
                    // Only the file this call just created (CreateNew) is removed.
                    TryDeleteOwn(shortcutId, path);
                    throw;
                }
                return true;
            }
            throw new LibraryException("A version couldn't be saved: too many versions with the same time.");
        }
    }

    /// <summary>
    /// Removes versions beyond the newest <see cref="MaxPerShortcut"/> of a shortcut, and versions replaced more than <see cref="KeepFor"/>
    /// before <paramref name="now"/>, in two steps so a clock that jumps ahead once can't remove them: a purge only notes a version as
    /// due, and a later purge at least <see cref="PurgeConfirmAfter"/> after that removes it. Files whose name has no readable time are
    /// never removed. Returns how many were removed.
    /// </summary>
    public int Purge(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!Directory.Exists(Folder)) return 0;
            CheckFolder(Folder);
            var due = new List<(string Key, Guid Id, VersionFile File, bool ByCount)>();
            foreach (var id in ShortcutIds())
            {
                var files = Files(id);
                for (var i = 0; i < files.Count; i++)
                {
                    var f = files[i];
                    var byCount = i >= MaxPerShortcut;
                    var byAge = f.SavedAt <= now && f.SavedAt + KeepFor <= now;
                    if (byCount || byAge) due.Add(($"{id:D}/{f.Key}", id, f, byCount));
                }
            }
            var state = ReadState();
            var next = new Dictionary<string, PurgeMark>(StringComparer.OrdinalIgnoreCase);
            var remove = new List<(Guid Id, VersionFile File)>();
            foreach (var d in due)
            {
                var mark = state.TryGetValue(d.Key, out var m) && m.SavedAt.UtcTicks == d.File.SavedAt.UtcTicks && m.SeenDueAt <= now
                    && (d.ByCount || m.SeenDueAt >= d.File.SavedAt + KeepFor) ? m : null;
                if (mark is not null && now - mark.SeenDueAt >= PurgeConfirmAfter) remove.Add((d.Id, d.File));
                else next[d.Key] = mark ?? new PurgeMark { SavedAt = d.File.SavedAt, SeenDueAt = now };
            }
            if (state.Count == 0 && next.Count == 0) return 0;
            try { WriteState(next); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or LibraryException)
            {
                StoreDiagnostics.Report($"Version history: couldn't save the cleanup state, so nothing was removed. {ex.Message}");
                return 0;
            }
            var removed = 0;
            foreach (var (id, f) in remove)
            {
                try
                {
                    var path = OwnFile(id, f.Key);
                    if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                    File.Delete(path);
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or LibraryException)
                {
                    StoreDiagnostics.Report($"Version history: couldn't remove an old version. {ex.Message}");
                }
            }
            return removed;
        }
    }

    /// <summary>"+2 steps, renamed" and the like: what <paramref name="newer"/> changed compared with <paramref name="older"/>.</summary>
    internal static string Describe(Shortcut? older, Shortcut newer)
    {
        if (older is null) return "Oldest kept version";
        var parts = new List<string>();
        var delta = StepCount(newer) - StepCount(older);
        if (delta != 0) parts.Add($"{(delta > 0 ? "+" : "-")}{Math.Abs(delta)} step{(Math.Abs(delta) == 1 ? "" : "s")}");
        else if (JsonSerializer.Serialize(newer.Actions, AutomationPortalJson.Options) != JsonSerializer.Serialize(older.Actions, AutomationPortalJson.Options))
            parts.Add("steps changed");
        if (!string.Equals(newer.Name, older.Name, StringComparison.Ordinal)) parts.Add("renamed");
        if (parts.Count == 0) parts.Add(Normalized(newer) == Normalized(older) ? "no changes" : "settings changed");
        var text = string.Join(", ", parts);
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    internal static int StepCount(Shortcut s) => s.Actions.Count(a => a.Flow is null || a.Flow.Role == FlowRole.Start);

    /// <summary>Hash of a shortcut file's content, ignoring when it was saved and where it sits in the library.</summary>
    internal static string ContentHash(byte[] bytes)
    {
        var s = Parse(new StoreRead(StoreReadStatus.Ok, null, bytes, null));
        return Convert.ToHexString(SHA256.HashData(s is null ? bytes : System.Text.Encoding.UTF8.GetBytes(Normalized(s))));
    }

    private static string Normalized(Shortcut s)
    {
        var c = s.Clone();
        c.ModifiedAt = default; c.SortOrder = 0; c.FolderId = null; c.Pinned = false;
        return AutomationPortalJson.Serialize(c);
    }

    private static Shortcut? Parse(StoreRead r)
    {
        if (r.Status != StoreReadStatus.Ok) return null;
        var b = r.Bytes!;
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) b = b[3..];
        try { return AutomationPortalJson.Deserialize<Shortcut>(new System.Text.UTF8Encoding(false).GetString(b)); }
        catch (JsonException) { return null; }
    }

    /// <summary>Version files of a shortcut whose name holds a readable time, newest first.</summary>
    private List<VersionFile> Files(Guid shortcutId)
    {
        var dir = ShortcutFolder(shortcutId);
        var list = new List<VersionFile>();
        try
        {
            if (!Directory.Exists(dir) || (File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) return list;
            foreach (var path in Directory.EnumerateFiles(dir, "*" + DataExtension))
            {
                var key = Path.GetFileName(path);
                if (key.EndsWith(DataExtension, StringComparison.OrdinalIgnoreCase) && TryParseKey(key, out var savedAt, out var n))
                    list.Add(new VersionFile(path, key, savedAt, n));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return list.OrderByDescending(f => f.SavedAt).ThenByDescending(f => f.Number).ToList();
    }

    private static bool TryParseKey(string key, out DateTimeOffset savedAt, out int number)
    {
        savedAt = default; number = 1;
        var stem = key[..^DataExtension.Length];
        var stampLength = StampFormat.Replace("'", "").Length;
        if (stem.Length < stampLength) return false;
        var rest = stem[stampLength..];
        if (rest.Length > 0 && !(rest.StartsWith(" (") && rest.EndsWith(')') && int.TryParse(rest[2..^1], NumberStyles.None, CultureInfo.InvariantCulture, out number)))
            return false;
        if (!DateTime.TryParseExact(stem[..stampLength], StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return false;
        savedAt = new DateTimeOffset(dt, TimeSpan.Zero);
        return true;
    }

    private List<Guid> ShortcutIds()
    {
        var ids = new List<Guid>();
        try
        {
            foreach (var d in Directory.EnumerateDirectories(Folder))
                if (Guid.TryParseExact(Path.GetFileName(d), "D", out var id) && Path.GetFileName(d) == id.ToString("D")) ids.Add(id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return ids;
    }

    private string ShortcutFolder(Guid shortcutId) => Path.Combine(Folder, shortcutId.ToString("D"));

    private void EnsureFolder(Guid shortcutId)
    {
        Directory.CreateDirectory(Folder);
        CheckFolder(Folder);
        var dir = ShortcutFolder(shortcutId);
        Directory.CreateDirectory(dir);
        CheckFolder(dir);
    }

    private static void CheckFolder(string folder)
    {
        if (PathSafety.IsProtected(folder))
            throw new LibraryException("The version history folder is in a place Automation Portal won't change.");
        // A junction or link would send writes and removals somewhere else.
        if (Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            throw new LibraryException("The version history folder is a link to another place, so Automation Portal won't change it.");
    }

    /// <summary>The full path of a version file directly inside the shortcut's own folder. Throws for anything else.</summary>
    private string OwnFile(Guid shortcutId, string fileName)
    {
        if (shortcutId == Guid.Empty || string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName
            || !fileName.EndsWith(DataExtension, StringComparison.OrdinalIgnoreCase))
            throw new LibraryException("That version isn't in the history.");
        var dir = ShortcutFolder(shortcutId);
        var full = Path.GetFullPath(Path.Combine(dir, fileName));
        if (!string.Equals(Path.GetDirectoryName(full), dir, StringComparison.OrdinalIgnoreCase))
            throw new LibraryException("That version isn't in the history.");
        CheckFolder(Folder);
        CheckFolder(dir);
        return full;
    }

    private void TryDeleteOwn(Guid shortcutId, string path)
    {
        try { var own = OwnFile(shortcutId, Path.GetFileName(path)); if (File.Exists(own)) File.Delete(own); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or LibraryException) { }
    }

    private Dictionary<string, PurgeMark> ReadState()
    {
        var result = new Dictionary<string, PurgeMark>(StringComparer.OrdinalIgnoreCase);
        var r = StoreFile.ReadText(Path.Combine(Folder, StateFileName));
        if (r.Status != StoreReadStatus.Ok) return result;
        try
        {
            if (AutomationPortalJson.Deserialize<PurgeState>(r.Text!)?.Marks is { } marks)
                foreach (var (k, v) in marks) if (v is not null) result[k] = v;
        }
        catch (JsonException) { }
        return result;
    }

    private void WriteState(Dictionary<string, PurgeMark> marks)
    {
        CheckFolder(Folder);
        var bytes = new System.Text.UTF8Encoding(false).GetBytes(AutomationPortalJson.Serialize(new PurgeState { Marks = marks }, indented: true));
        var path = Path.Combine(Folder, StateFileName);
        var temp = Path.Combine(Folder, $"{StateFileName}.{Guid.NewGuid():N}.tmp");
        using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            fs.Write(bytes);
            fs.Flush(flushToDisk: true);
        }
        // Replaces only this class's own state file (a fixed name directly in the versions folder, never a version).
        try { File.Move(temp, path, overwrite: true); }
        catch
        {
            try { File.Delete(temp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }
}
