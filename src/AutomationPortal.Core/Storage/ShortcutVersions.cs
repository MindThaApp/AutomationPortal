using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

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
/// <para>
/// Clock caveats: every time here comes from the PC's clock. A version's name is the time it was replaced, so a clock set back makes
/// new versions sort below older ones, and a clock set ahead makes a version look newer than it is until the real time catches up.
/// Removal by age needs two purges at least <see cref="PurgeConfirmAfter"/> apart that both see it as due, so a clock that jumps ahead
/// once removes nothing, and a version whose time is in the future is never removed by age. The merge window in <see cref="Keep"/>
/// restarts when the clock goes back, so it never keeps more than one version per window either way.
/// </para>
/// </summary>
public sealed class ShortcutVersions
{
    public const int MaxPerShortcut = 30;
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(90);
    /// <summary>Saves of the same shortcut within this time of the version this app kept last are merged into that version.</summary>
    public static readonly TimeSpan MergeWithin = TimeSpan.FromMinutes(10);
    /// <summary>A version is removed only by a purge at least this long after the purge that first saw it was due.</summary>
    internal static readonly TimeSpan PurgeConfirmAfter = TimeSpan.FromHours(24);
    internal const long DefaultMaxTotalBytes = 100L * 1024 * 1024;
    private const string DataExtension = ".json";
    private const string StampFormat = "yyyyMMdd'T'HHmmssfff'Z'";
    private const string StateFileName = "purge-state.dat";
    private static readonly string[] IgnoredKeys = { "modifiedAt", "sortOrder", "folderId", "pinned" };

    private readonly object _gate = new();
    /// <summary>The version this app instance kept last for each shortcut, and when (for merging quick saves).</summary>
    private readonly Dictionary<Guid, (string Key, DateTimeOffset KeptAt)> _keptHere = new();

    public ShortcutVersions(string folder) { Folder = Path.GetFullPath(folder).TrimEnd('\\', '/'); }

    public string Folder { get; }

    /// <summary>Versions beyond this total size, oldest first, are removed by <see cref="Purge"/>.</summary>
    internal long MaxTotalBytes { get; init; } = DefaultMaxTotalBytes;

    /// <summary>
    /// Says whether a shortcut marked by <see cref="MarkRemoved"/> is in use again (back in the library or in Recently deleted), so its
    /// versions stay. Called by <see cref="Purge"/> outside its lock. Without it, marked shortcuts' versions are removed.
    /// </summary>
    internal Func<Guid, bool>? InUse { get; set; }

    private sealed class PurgeMark
    {
        public DateTimeOffset SavedAt { get; set; }
        /// <summary>When a purge first saw it was due.</summary>
        public DateTimeOffset SeenDueAt { get; set; }
    }

    private sealed class PurgeState
    {
        public Dictionary<string, PurgeMark> Marks { get; set; } = new();
        /// <summary>Shortcuts removed for good, and when; their versions are removed by the purge.</summary>
        public Dictionary<Guid, DateTimeOffset> Removed { get; set; } = new();
    }

    private readonly record struct VersionFile(string Path, string Key, DateTimeOffset SavedAt, int Number, long Length);

    private enum DueReason { Age, Count, Removed, Size }

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
    /// checked by reading it back. Nothing is kept when it matches <paramref name="replacedBy"/>, or matches the newest kept version while
    /// that is younger than <see cref="KeepFor"/> minus a day (so the version just replaced is never already due for removal); matching
    /// ignores only the save time and the place in the library. With <paramref name="merge"/>, nothing is kept either while the version
    /// this app kept last for the shortcut is still there and less than <see cref="MergeWithin"/> old: autosave during one editing
    /// session then keeps only the state from before the session, and the per-shortcut cap counts versions at least that far apart.
    /// Returns true when a version was written. Throws when it can't be written.
    /// </summary>
    internal bool Keep(Guid shortcutId, byte[] content, DateTimeOffset now, byte[]? replacedBy = null, bool merge = false)
    {
        if (shortcutId == Guid.Empty) return false;
        var hash = ContentHash(content);
        if (replacedBy is not null && ContentHash(replacedBy) == hash) return false;
        lock (_gate)
        {
            var files = Files(shortcutId);
            if (merge && _keptHere.TryGetValue(shortcutId, out var last)
                && files.Any(f => string.Equals(f.Key, last.Key, StringComparison.OrdinalIgnoreCase)))
            {
                // A clock set back restarts the window instead of keeping a version on every save.
                if (now < last.KeptAt) { _keptHere[shortcutId] = (last.Key, now); return false; }
                if (now - last.KeptAt < MergeWithin) return false;
            }
            if (files.Count > 0 && now - files[0].SavedAt < KeepFor - TimeSpan.FromDays(1)
                && StoreFile.ReadBytes(files[0].Path) is { Status: StoreReadStatus.Ok } newest && ContentHash(newest.Bytes!) == hash)
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
                _keptHere[shortcutId] = (Path.GetFileName(path), now);
                return true;
            }
            throw new LibraryException("A version couldn't be saved: too many versions with the same time.");
        }
    }

    /// <summary>
    /// Notes that a shortcut was removed for good (from Recently deleted), so <see cref="Purge"/> removes its versions in the usual two
    /// steps unless <see cref="InUse"/> says it is back. Nothing is removed here. Throws when the note can't be saved.
    /// </summary>
    internal void MarkRemoved(Guid shortcutId, DateTimeOffset now)
    {
        if (shortcutId == Guid.Empty) return;
        lock (_gate)
        {
            if (Files(shortcutId).Count == 0) return;
            CheckFolder(Folder);
            var state = ReadState();
            if (state.Removed.ContainsKey(shortcutId)) return;
            state.Removed[shortcutId] = now;
            WriteState(state);
        }
    }

    /// <summary>
    /// Removes versions beyond the newest <see cref="MaxPerShortcut"/> of a shortcut, versions replaced more than <see cref="KeepFor"/>
    /// before <paramref name="now"/>, versions of shortcuts noted by <see cref="MarkRemoved"/>, and the oldest versions beyond
    /// <see cref="MaxTotalBytes"/> in all. It works in two steps so a clock that jumps ahead once can't remove them: a purge only notes
    /// a version as due, and a later purge at least <see cref="PurgeConfirmAfter"/> after that removes it. Files whose name has no
    /// readable time are never removed. Returns how many were removed.
    /// </summary>
    public int Purge(DateTimeOffset now)
    {
        List<Guid> marked;
        lock (_gate)
        {
            if (!Directory.Exists(Folder)) return 0;
            CheckFolder(Folder);
            marked = ReadState().Removed.Keys.ToList();
        }
        // Asked outside the lock: the library calls Keep while holding its own lock, and InUse takes that lock.
        var back = new HashSet<Guid>();
        if (InUse is { } inUse)
            foreach (var id in marked)
            {
                try { if (inUse(id)) back.Add(id); }
                catch (Exception ex) { back.Add(id); StoreDiagnostics.Report($"Version history: couldn't check a removed shortcut, so its versions stay. {ex.Message}"); }
            }

        lock (_gate)
        {
            if (!Directory.Exists(Folder)) return 0;
            CheckFolder(Folder);
            var state = ReadState();
            var removedNext = new Dictionary<Guid, DateTimeOffset>();
            var due = new List<(string Key, Guid Id, VersionFile File, DueReason Reason)>();
            var kept = new List<(Guid Id, VersionFile File)>();
            foreach (var id in ShortcutIds())
            {
                var files = Files(id);
                var gone = false;
                if (state.Removed.TryGetValue(id, out var removedAt) && !back.Contains(id) && files.Count > 0)
                {
                    removedNext[id] = removedAt;
                    gone = marked.Contains(id);
                }
                for (var i = 0; i < files.Count; i++)
                {
                    var f = files[i];
                    DueReason? reason = gone ? DueReason.Removed
                        : i >= MaxPerShortcut ? DueReason.Count
                        : f.SavedAt <= now && f.SavedAt + KeepFor <= now ? DueReason.Age
                        : null;
                    if (reason is { } r) due.Add(($"{id:D}/{f.Key}", id, f, r));
                    else kept.Add((id, f));
                }
            }
            var total = 0L;
            foreach (var k in kept.OrderByDescending(k => k.File.SavedAt).ThenByDescending(k => k.File.Number))
            {
                total += k.File.Length;
                if (total > MaxTotalBytes) due.Add(($"{k.Id:D}/{k.File.Key}", k.Id, k.File, DueReason.Size));
            }

            var next = new Dictionary<string, PurgeMark>(StringComparer.OrdinalIgnoreCase);
            var remove = new List<(Guid Id, VersionFile File)>();
            foreach (var d in due)
            {
                var mark = state.Marks.TryGetValue(d.Key, out var m) && m.SavedAt.UtcTicks == d.File.SavedAt.UtcTicks && m.SeenDueAt <= now
                    && (d.Reason != DueReason.Age || m.SeenDueAt >= d.File.SavedAt + KeepFor) ? m : null;
                if (mark is not null && now - mark.SeenDueAt >= PurgeConfirmAfter) remove.Add((d.Id, d.File));
                else next[d.Key] = mark ?? new PurgeMark { SavedAt = d.File.SavedAt, SeenDueAt = now };
            }
            if (state.Marks.Count == 0 && state.Removed.Count == 0 && next.Count == 0 && removedNext.Count == 0) return 0;
            try { WriteState(new PurgeState { Marks = next, Removed = removedNext }); }
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

    /// <summary>
    /// Hash of a shortcut file's content, ignoring only when it was saved and where it sits in the library. Every other field counts,
    /// including ones this version of the app doesn't know, so a file from a newer version is never taken as unchanged.
    /// </summary>
    internal static string ContentHash(byte[] bytes)
    {
        var b = bytes.AsSpan();
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) b = b[3..];
        try
        {
            if (JsonNode.Parse(b, null, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) is JsonObject o)
            {
                foreach (var key in o.Select(p => p.Key).Where(k => IgnoredKeys.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList()) o.Remove(key);
                return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(o.ToJsonString())));
            }
        }
        catch (JsonException) { }
        return Convert.ToHexString(SHA256.HashData(bytes));
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
                {
                    long length;
                    try { length = new FileInfo(path).Length; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { length = 0; }
                    list.Add(new VersionFile(path, key, savedAt, n, length));
                }
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

    private PurgeState ReadState()
    {
        var result = new PurgeState { Marks = new Dictionary<string, PurgeMark>(StringComparer.OrdinalIgnoreCase) };
        var r = StoreFile.ReadText(Path.Combine(Folder, StateFileName));
        if (r.Status != StoreReadStatus.Ok) return result;
        try
        {
            if (AutomationPortalJson.Deserialize<PurgeState>(r.Text!) is { } state)
            {
                if (state.Marks is { } marks) foreach (var (k, v) in marks) if (v is not null) result.Marks[k] = v;
                if (state.Removed is { } removed) foreach (var (k, v) in removed) if (k != Guid.Empty) result.Removed[k] = v;
            }
        }
        catch (JsonException) { }
        return result;
    }

    private void WriteState(PurgeState state)
    {
        CheckFolder(Folder);
        var bytes = new System.Text.UTF8Encoding(false).GetBytes(AutomationPortalJson.Serialize(state, indented: true));
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
