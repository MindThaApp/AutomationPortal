namespace AutomationPortal.Core;

/// <summary>A shortcut in Recently deleted, read from its info file.</summary>
public sealed class DeletedShortcut
{
    internal DeletedShortcut(string key, Guid shortcutId, string name, DateTimeOffset deletedAt, string originalFileName, int automationCount)
    {
        Key = key; ShortcutId = shortcutId; Name = name; DeletedAt = deletedAt; OriginalFileName = originalFileName; AutomationCount = automationCount;
    }

    /// <summary>The name of the saved copy inside the Recently deleted folder.</summary>
    public string Key { get; }
    public Guid ShortcutId { get; }
    public string Name { get; }
    public DateTimeOffset DeletedAt { get; }
    /// <summary>The file name it had in the shortcuts folder.</summary>
    public string OriginalFileName { get; }
    /// <summary>How many automations used it when it was deleted.</summary>
    public int AutomationCount { get; }
    public DateTimeOffset RemovedAt => DeletedAt + RecentlyDeleted.KeepFor;
}

/// <summary>What <see cref="JsonShortcutLibrary.Restore"/> did.</summary>
public sealed class RestoreResult
{
    internal RestoreResult(Shortcut shortcut, bool asCopy, string? renamedFrom, bool removedFromDeleted)
    {
        Shortcut = shortcut; AsCopy = asCopy; RenamedFrom = renamedFrom; RemovedFromDeleted = removedFromDeleted;
    }

    public Shortcut Shortcut { get; }
    /// <summary>True when its id was already in use, so it came back as a copy with a new id (automations aren't linked to the copy).</summary>
    public bool AsCopy { get; }
    /// <summary>The old name when a shortcut with that name already existed.</summary>
    public string? RenamedFrom { get; }
    /// <summary>False when the copy in Recently deleted couldn't be removed after the restore.</summary>
    public bool RemovedFromDeleted { get; }
}

/// <summary>
/// The Recently deleted folder (<see cref="AutomationPortalPaths.DeletedFolder"/>). Each deleted shortcut file is kept as a copy
/// plus "&lt;copy&gt;.info" describing it. Only files listed by an info file directly inside this folder are ever removed;
/// nothing is removed recursively and nothing outside the folder is touched.
/// </summary>
public sealed class RecentlyDeleted
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(30);
    private const string InfoExtension = ".info";

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

    /// <summary>Newest first. Info files that can't be read, or whose copy is missing, are left alone and not listed.</summary>
    public IReadOnlyList<DeletedShortcut> List()
    {
        var list = new List<DeletedShortcut>();
        if (!Directory.Exists(Folder)) return list;
        IEnumerable<string> infos;
        try { infos = Directory.EnumerateFiles(Folder, "*" + InfoExtension).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return list; }
        foreach (var infoPath in infos)
        {
            var dataPath = infoPath[..^InfoExtension.Length];
            if (!File.Exists(dataPath)) continue;
            var r = StoreFile.ReadText(infoPath);
            if (r.Status != StoreReadStatus.Ok) continue;
            Info? info;
            try { info = AutomationPortalJson.Deserialize<Info>(r.Text!); }
            catch (System.Text.Json.JsonException) { continue; }
            if (info is null || info.ShortcutId == Guid.Empty) continue;
            list.Add(new DeletedShortcut(Path.GetFileName(dataPath), info.ShortcutId, info.Name, info.DeletedAt, info.OriginalFileName, info.AutomationCount));
        }
        return list.OrderByDescending(d => d.DeletedAt).ToList();
    }

    /// <summary>
    /// Copies <paramref name="sourceFile"/> in under a name not used yet, checks the copy matches byte for byte, then writes its info
    /// file. The source is left in place; the caller removes it once this returns.
    /// </summary>
    internal DeletedShortcut Add(string sourceFile, Guid shortcutId, string name, int automationCount, DateTimeOffset now)
    {
        EnsureFolder();
        var stem = Path.GetFileNameWithoutExtension(sourceFile);
        var ext = Path.GetExtension(sourceFile);
        var key = stem + ext;
        for (var n = 2; Taken(key); n++) key = $"{stem} ({n}){ext}";
        var dataPath = OwnPath(key);

        File.Copy(sourceFile, dataPath, overwrite: false);
        try
        {
            new FileInfo(dataPath).IsReadOnly = false;
            var original = StoreFile.ReadBytes(sourceFile);
            var copy = StoreFile.ReadBytes(dataPath);
            if (original.Status != StoreReadStatus.Ok || copy.Status != StoreReadStatus.Ok || !original.Bytes!.AsSpan().SequenceEqual(copy.Bytes))
                throw new LibraryException($"Couldn't make a safe copy of '{Path.GetFileName(sourceFile)}', so it wasn't deleted. Try again in a moment.");
            var info = new Info { ShortcutId = shortcutId, Name = name, DeletedAt = now, OriginalFileName = Path.GetFileName(sourceFile), AutomationCount = automationCount };
            var bytes = new System.Text.UTF8Encoding(false).GetBytes(AutomationPortalJson.Serialize(info, indented: true));
            using (var fs = new FileStream(dataPath + InfoExtension, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                fs.Write(bytes);
        }
        catch
        {
            // Only the copy made just above is removed; the source was never changed.
            TryDeleteOwn(dataPath + InfoExtension);
            TryDeleteOwn(dataPath);
            throw;
        }
        return new DeletedShortcut(key, shortcutId, name, now, Path.GetFileName(sourceFile), automationCount);
    }

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

    /// <summary>Removes one item for good: its copy, then its info file.</summary>
    public void DeletePermanently(DeletedShortcut entry)
    {
        var dataPath = OwnPath(entry.Key);
        var infoPath = OwnPath(entry.Key + InfoExtension);
        try
        {
            if (File.Exists(dataPath)) File.Delete(dataPath);
            if (File.Exists(infoPath)) File.Delete(infoPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LibraryException($"Couldn't remove \"{entry.Name}\" from Recently deleted. {ex.Message}", ex);
        }
    }

    /// <summary>Removes every listed item. Returns how many were removed; items that couldn't be removed stay.</summary>
    public int Empty() => RemoveWhere(_ => true);

    /// <summary>Removes items deleted more than <see cref="KeepFor"/> before <paramref name="now"/>.</summary>
    public int Purge(DateTimeOffset now) => RemoveWhere(d => d.RemovedAt <= now);

    private int RemoveWhere(Func<DeletedShortcut, bool> match)
    {
        var removed = 0;
        foreach (var d in List().Where(match))
        {
            try { DeletePermanently(d); removed++; }
            catch (LibraryException ex) { StoreDiagnostics.Report(ex.Message); }
        }
        return removed;
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
