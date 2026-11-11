namespace AutomationPortal.Core;

/// <summary>A shortcut in Recently deleted, read from its info file.</summary>
public sealed class DeletedShortcut
{
    internal DeletedShortcut(string key, Guid shortcutId, string name, DateTimeOffset deletedAt, string originalFileName, int automationCount,
        bool damaged = false, bool canRestore = true)
    {
        Key = key; ShortcutId = shortcutId; Name = name; DeletedAt = deletedAt; OriginalFileName = originalFileName; AutomationCount = automationCount;
        Damaged = damaged; CanRestore = canRestore;
    }

    /// <summary>The name of the saved copy inside the Recently deleted folder.</summary>
    public string Key { get; }
    public Guid ShortcutId { get; }
    public string Name { get; }
    /// <summary>Default when <see cref="Damaged"/>.</summary>
    public DateTimeOffset DeletedAt { get; }
    /// <summary>The file name it had in the shortcuts folder.</summary>
    public string OriginalFileName { get; }
    /// <summary>How many automations used it when it was deleted.</summary>
    public int AutomationCount { get; }
    public DateTimeOffset RemovedAt => DeletedAt + RecentlyDeleted.KeepFor;
    /// <summary>True when its info file is missing or can't be read. Such items are never removed automatically.</summary>
    public bool Damaged { get; }
    /// <summary>False when the saved copy can't be read as a shortcut.</summary>
    public bool CanRestore { get; }
}

/// <summary>What <see cref="JsonShortcutLibrary.Restore"/> did.</summary>
public sealed class RestoreResult
{
    internal RestoreResult(Shortcut shortcut, bool asCopy, string? renamedFrom, bool removedFromDeleted, string? clearedHotkey, string? clearedAlias)
    {
        Shortcut = shortcut; AsCopy = asCopy; RenamedFrom = renamedFrom; RemovedFromDeleted = removedFromDeleted;
        ClearedHotkey = clearedHotkey; ClearedAlias = clearedAlias;
    }

    public Shortcut Shortcut { get; }
    /// <summary>True when its id was already in use, so it came back as a copy with a new id (automations aren't linked to the copy).</summary>
    public bool AsCopy { get; }
    /// <summary>The old name when a shortcut with that name already existed.</summary>
    public string? RenamedFrom { get; }
    /// <summary>False when the copy in Recently deleted couldn't be removed after the restore.</summary>
    public bool RemovedFromDeleted { get; }
    /// <summary>Its keyboard shortcut, left off because another shortcut uses it.</summary>
    public string? ClearedHotkey { get; }
    /// <summary>Its Quick Run name, left off because another shortcut uses it.</summary>
    public string? ClearedAlias { get; }
}

/// <summary>
/// The Recently deleted folder (<see cref="AutomationPortalPaths.DeletedFolder"/>), kept on this PC only. Each deleted shortcut file
/// is kept as a copy plus "&lt;copy&gt;.info" describing it. Only files directly inside this folder are ever removed; nothing is
/// removed recursively and nothing outside the folder is touched.
/// </summary>
public sealed class RecentlyDeleted
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(30);
    /// <summary>An expired item is removed only by a purge at least this long after the purge that first saw it expired.</summary>
    internal static readonly TimeSpan PurgeConfirmAfter = TimeSpan.FromHours(24);
    public const string DamagedName = "Unknown shortcut (damaged info)";
    private const string InfoExtension = ".info";
    private const string DataExtension = ".json";
    private const string StateFileName = "purge-state.dat";

    public RecentlyDeleted(string folder) { Folder = Path.GetFullPath(folder).TrimEnd('\\', '/'); }

    public string Folder { get; }

    private sealed class Info
    {
        public Guid ShortcutId { get; set; }
        public string Name { get; set; } = "";
        public DateTimeOffset DeletedAt { get; set; }
        public string OriginalFileName { get; set; } = "";
        public int AutomationCount { get; set; }
    }

    private sealed class PurgeMark
    {
        public DateTimeOffset DeletedAt { get; set; }
        /// <summary>When a purge first saw it expired.</summary>
        public DateTimeOffset SeenExpiredAt { get; set; }
    }

    private sealed class PurgeState
    {
        public Dictionary<string, PurgeMark> Marks { get; set; } = new();
    }

    /// <summary>
    /// Newest first, then copies whose info file is missing or damaged (listed as <see cref="DamagedName"/>). Info files whose copy is
    /// missing are left alone and not listed.
    /// </summary>
    public IReadOnlyList<DeletedShortcut> List()
    {
        var list = new List<DeletedShortcut>();
        if (!Directory.Exists(Folder)) return list;
        List<string> files;
        try { files = Directory.EnumerateFiles(Folder).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return list; }
        var names = new HashSet<string>(files.Select(f => Path.GetFileName(f)), StringComparer.OrdinalIgnoreCase);
        foreach (var dataPath in files.Where(f => f.EndsWith(DataExtension, StringComparison.OrdinalIgnoreCase)))
        {
            var key = Path.GetFileName(dataPath);
            var info = names.Contains(key + InfoExtension) ? ReadInfo(dataPath + InfoExtension) : null;
            list.Add(info is not null
                ? new DeletedShortcut(key, info.ShortcutId, info.Name, info.DeletedAt, info.OriginalFileName, info.AutomationCount)
                : DamagedEntry(dataPath, key));
        }
        return list.OrderBy(d => d.Damaged).ThenByDescending(d => d.DeletedAt).ToList();
    }

    private static Info? ReadInfo(string path)
    {
        var r = StoreFile.ReadText(path);
        if (r.Status != StoreReadStatus.Ok) return null;
        try { return AutomationPortalJson.Deserialize<Info>(r.Text!) is { } info && info.ShortcutId != Guid.Empty ? info : null; }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private static DeletedShortcut DamagedEntry(string dataPath, string key)
    {
        Shortcut? s = null;
        var r = StoreFile.ReadText(dataPath);
        if (r.Status == StoreReadStatus.Ok)
        {
            try { s = AutomationPortalJson.Deserialize<Shortcut>(r.Text!); }
            catch (System.Text.Json.JsonException) { }
        }
        return new DeletedShortcut(key, s?.Id ?? Guid.Empty, DamagedName, default, key, 0, damaged: true, canRestore: s is not null);
    }

    /// <summary>
    /// Writes a copy of <paramref name="sourceFile"/> under a name not used yet (straight to disk), checks the copy read back matches
    /// byte for byte and the source hasn't changed meanwhile, then writes and checks its info file the same way. The source is left in
    /// place; the caller removes it once this returns.
    /// </summary>
    internal DeletedShortcut Add(string sourceFile, Guid shortcutId, string name, int automationCount, DateTimeOffset now)
    {
        EnsureFolder();
        var fileName = Path.GetFileName(sourceFile);
        var original = StoreFile.ReadBytes(sourceFile);
        if (original.Status != StoreReadStatus.Ok)
            throw new LibraryException($"Couldn't read '{fileName}', so it wasn't deleted. {original.Error}");
        var stem = Path.GetFileNameWithoutExtension(sourceFile);
        var ext = Path.GetExtension(sourceFile);
        var key = stem + ext;
        for (var n = 2; Taken(key); n++) key = $"{stem} ({n}){ext}";
        var dataPath = OwnPath(key);
        var infoPath = OwnPath(key + InfoExtension);

        var dataCreated = false;
        var infoCreated = false;
        try
        {
            using (var fs = CreateNewDurable(dataPath))
            {
                dataCreated = true;
                fs.Write(original.Bytes!);
                fs.Flush(flushToDisk: true);
            }
            var copy = StoreFile.ReadBytes(dataPath);
            var source = StoreFile.ReadBytes(sourceFile);
            if (copy.Status != StoreReadStatus.Ok || !original.Bytes!.AsSpan().SequenceEqual(copy.Bytes))
                throw new LibraryException($"Couldn't make a safe copy of '{fileName}', so it wasn't deleted. Try again in a moment.");
            if (source.Status != StoreReadStatus.Ok || !original.Bytes!.AsSpan().SequenceEqual(source.Bytes))
                throw new LibraryException($"'{fileName}' changed while it was being deleted, so it wasn't deleted. Try again in a moment.");

            var info = new Info { ShortcutId = shortcutId, Name = name, DeletedAt = now, OriginalFileName = fileName, AutomationCount = automationCount };
            var bytes = new System.Text.UTF8Encoding(false).GetBytes(AutomationPortalJson.Serialize(info, indented: true));
            using (var fs = CreateNewDurable(infoPath))
            {
                infoCreated = true;
                fs.Write(bytes);
                fs.Flush(flushToDisk: true);
            }
            var back = StoreFile.ReadBytes(infoPath);
            if (back.Status != StoreReadStatus.Ok || !bytes.AsSpan().SequenceEqual(back.Bytes))
                throw new LibraryException($"Couldn't make a safe copy of '{fileName}', so it wasn't deleted. Try again in a moment.");
        }
        catch
        {
            // Only files this call created (CreateNew) are removed; the source was never changed.
            if (infoCreated) TryDeleteOwn(infoPath);
            if (dataCreated) TryDeleteOwn(dataPath);
            throw;
        }
        return new DeletedShortcut(key, shortcutId, name, now, fileName, automationCount);
    }

    private static FileStream CreateNewDurable(string path) =>
        new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);

    internal byte[] ReadData(DeletedShortcut entry)
    {
        var r = StoreFile.ReadBytes(OwnPath(entry.Key));
        return r.Status switch
        {
            StoreReadStatus.Ok => r.Bytes!,
            StoreReadStatus.Missing => throw new LibraryException($"\"{entry.Name}\" isn't in Recently deleted any more."),
            _ => throw new LibraryException($"Couldn't read \"{entry.Name}\" from Recently deleted. {r.Error}"),
        };
    }

    /// <summary>
    /// Removes one item for good: its copy, then its info file. Refuses when the files under its name now describe a different item
    /// (it was restored and something else was deleted under the same name since it was listed).
    /// </summary>
    public void DeletePermanently(DeletedShortcut entry)
    {
        var dataPath = OwnPath(entry.Key);
        var infoPath = OwnPath(entry.Key + InfoExtension);
        try
        {
            if (!File.Exists(dataPath) && !File.Exists(infoPath)) return;
            if (!StillSame(entry, infoPath))
                throw new LibraryException($"\"{entry.Name}\" changed in Recently deleted since it was listed, so it wasn't removed.");
            if (File.Exists(dataPath)) File.Delete(dataPath);
            if (File.Exists(infoPath)) File.Delete(infoPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LibraryException($"Couldn't remove \"{entry.Name}\" from Recently deleted. {ex.Message}", ex);
        }
        if (entry.ShortcutId != Guid.Empty && RemovedForGood is { } removed)
        {
            try { removed(entry.ShortcutId); }
            catch (Exception ex) { StoreDiagnostics.Report($"Recently deleted: couldn't note a removed shortcut for its version history. {ex.Message}", ex); }
        }
    }

    /// <summary>Called with the shortcut's id after an item is removed for good (also when it was restored and then cleared). Must not throw.</summary>
    internal Action<Guid>? RemovedForGood { get; set; }

    private static bool StillSame(DeletedShortcut entry, string infoPath)
    {
        var info = File.Exists(infoPath) ? ReadInfo(infoPath) : null;
        if (entry.Damaged) return info is null;
        return info is not null && info.ShortcutId == entry.ShortcutId && SameTime(info.DeletedAt, entry.DeletedAt);
    }

    private static bool SameTime(DateTimeOffset a, DateTimeOffset b) => a.UtcTicks == b.UtcTicks;

    /// <summary>Removes every listed item, damaged ones included. Returns how many were removed; items that couldn't be removed stay.</summary>
    public int Empty() => Empty(List());

    /// <summary>Removes exactly <paramref name="items"/> (as listed earlier). Returns how many were removed.</summary>
    public int Empty(IEnumerable<DeletedShortcut> items)
    {
        var removed = 0;
        foreach (var d in items)
        {
            try { DeletePermanently(d); removed++; }
            catch (LibraryException ex) { StoreDiagnostics.Report(ex.Message); }
        }
        return removed;
    }

    /// <summary>
    /// Removes items deleted more than <see cref="KeepFor"/> before <paramref name="now"/>, in two steps so a clock that jumps ahead
    /// once can't remove everything: a purge only notes an item as expired, and a later purge at least
    /// <see cref="PurgeConfirmAfter"/> after that removes it. Damaged items and items without a deletion time, or with one in the
    /// future, are never removed.
    /// </summary>
    public int Purge(DateTimeOffset now)
    {
        var items = List().Where(d => !d.Damaged).ToList();
        var state = ReadState();
        var next = new Dictionary<string, PurgeMark>(StringComparer.OrdinalIgnoreCase);
        var remove = new List<DeletedShortcut>();
        foreach (var d in items)
        {
            if (d.DeletedAt == default || d.DeletedAt > now || d.RemovedAt > now) continue;
            var mark = state.TryGetValue(d.Key, out var m) && SameTime(m.DeletedAt, d.DeletedAt) && m.SeenExpiredAt >= d.RemovedAt && m.SeenExpiredAt <= now ? m : null;
            if (mark is not null && now - mark.SeenExpiredAt >= PurgeConfirmAfter) remove.Add(d);
            else next[d.Key] = mark ?? new PurgeMark { DeletedAt = d.DeletedAt, SeenExpiredAt = now };
        }
        if (state.Count == 0 && next.Count == 0) return 0;
        try { WriteState(next); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or LibraryException)
        {
            StoreDiagnostics.Report($"Recently deleted: couldn't save the cleanup state, so nothing was removed. {ex.Message}");
            return 0;
        }
        return Empty(remove);
    }

    private Dictionary<string, PurgeMark> ReadState()
    {
        var result = new Dictionary<string, PurgeMark>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(Folder)) return result;
        var r = StoreFile.ReadText(OwnPath(StateFileName));
        if (r.Status != StoreReadStatus.Ok) return result;
        try
        {
            if (AutomationPortalJson.Deserialize<PurgeState>(r.Text!)?.Marks is { } marks)
                foreach (var (k, v) in marks) if (v is not null) result[k] = v;
        }
        catch (System.Text.Json.JsonException) { }
        return result;
    }

    private void WriteState(Dictionary<string, PurgeMark> marks)
    {
        EnsureFolder();
        var bytes = new System.Text.UTF8Encoding(false).GetBytes(AutomationPortalJson.Serialize(new PurgeState { Marks = marks }, indented: true));
        var path = OwnPath(StateFileName);
        var temp = OwnPath($"{StateFileName}.{Guid.NewGuid():N}.tmp");
        using (var fs = CreateNewDurable(temp))
        {
            fs.Write(bytes);
            fs.Flush(flushToDisk: true);
        }
        // Replaces only this class's own state file (a fixed name that is never a shortcut copy or info file).
        try { File.Move(temp, path, overwrite: true); }
        catch { TryDeleteOwn(temp); throw; }
    }

    private bool Taken(string key)
    {
        var p = OwnPath(key);
        return File.Exists(p) || Directory.Exists(p) || File.Exists(p + InfoExtension) || Directory.Exists(p + InfoExtension);
    }

    private void EnsureFolder()
    {
        Directory.CreateDirectory(Folder);
        CheckFolder();
    }

    private void CheckFolder()
    {
        if (PathSafety.IsProtected(Folder))
            throw new LibraryException("The Recently deleted folder is in a place Automation Portal won't change.");
        // A junction or link would send removals somewhere else.
        if (Directory.Exists(Folder) && (File.GetAttributes(Folder) & FileAttributes.ReparsePoint) != 0)
            throw new LibraryException("The Recently deleted folder is a link to another place, so Automation Portal won't change it.");
    }

    /// <summary>The full path of a file directly inside the folder. Throws for anything else.</summary>
    private string OwnPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName || fileName is "." or "..")
            throw new LibraryException("That item isn't in Recently deleted.");
        var full = Path.GetFullPath(Path.Combine(Folder, fileName));
        if (!string.Equals(Path.GetDirectoryName(full), Folder, StringComparison.OrdinalIgnoreCase))
            throw new LibraryException("That item isn't in Recently deleted.");
        CheckFolder();
        return full;
    }

    private void TryDeleteOwn(string path)
    {
        try { if (File.Exists(path)) File.Delete(OwnPath(Path.GetFileName(path))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or LibraryException) { }
    }
}
