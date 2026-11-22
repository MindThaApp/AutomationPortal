using System.IO.Compression;

namespace AutomationPortal.Actions.Documents;

public static class ZipTools
{
    /// <summary>Extract Zip stops when a zip would unpack to more than this many bytes in total.</summary>
    public const long MaxExtractedBytes = 20L * 1024 * 1024 * 1024;
    /// <summary>Extract Zip stops when a zip has more than this many entries.</summary>
    public const int MaxExtractedEntries = 200_000;

    /// <summary>Zips files and folders (folders keep their structure). Returns the zip path. An existing file is never overwritten.</summary>
    public static string Create(IReadOnlyList<string> items, string zipPath, CancellationToken ct = default)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fullZip = Path.GetFullPath(zipPath);
        var created = false;
        try
        {
            // CreateNew: if something appeared at this path since it was chosen, fail instead of replacing it.
            TestRootGuard.Check(zipPath);
            using var stream = new FileStream(zipPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            created = true;
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var item in items)
                {
                    ct.ThrowIfCancellationRequested();
                    if (Directory.Exists(item))
                    {
                        var root = UniqueName(Path.GetFileName(item.TrimEnd('\\', '/')), used);
                        zip.CreateEntry(root + "/");
                        AddFolder(zip, item, root, fullZip, ct);
                    }
                    else if (File.Exists(item))
                    {
                        if (string.Equals(Path.GetFullPath(item), fullZip, StringComparison.OrdinalIgnoreCase)) continue;
                        zip.CreateEntryFromFile(item, UniqueName(Path.GetFileName(item), used), CompressionLevel.Optimal);
                    }
                    else throw new ActionException($"Couldn't find '{Path.GetFileName(item)}'. Check it still exists.");
                }
            }
        }
        catch
        {
            // Only remove the half-made zip this call created; never a file that was already there.
            if (created) try { File.Delete(zipPath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
        return zipPath;
    }

    private static void AddFolder(ZipArchive zip, string folder, string entryRoot, string fullZip, CancellationToken ct)
    {
        foreach (var dir in Directory.EnumerateDirectories(folder))
        {
            var name = entryRoot + "/" + Path.GetFileName(dir);
            zip.CreateEntry(name + "/");
            AddFolder(zip, dir, name, fullZip, ct);
        }
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            ct.ThrowIfCancellationRequested();
            if (string.Equals(Path.GetFullPath(file), fullZip, StringComparison.OrdinalIgnoreCase)) continue;
            try { zip.CreateEntryFromFile(file, entryRoot + "/" + Path.GetFileName(file), CompressionLevel.Optimal); }
            catch (IOException ex) { throw new ActionException($"Couldn't add '{Path.GetFileName(file)}' to the zip. It may be open in another program.", ex); }
        }
    }

    private static string UniqueName(string name, HashSet<string> used)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        var candidate = name;
        for (var n = 2; !used.Add(candidate); n++) candidate = $"{stem} {n}{ext}";
        return candidate;
    }

    /// <summary>
    /// Extracts into <paramref name="destination"/>, never overwriting and never writing outside it. Returns the extracted
    /// files. A zip with more than <paramref name="maxEntries"/> entries, or that unpacks to more than
    /// <paramref name="maxBytes"/>, is refused; files this call already wrote are then removed again.
    /// </summary>
    public static IReadOnlyList<string> Extract(string zipPath, string destination, CancellationToken ct = default,
        long maxBytes = MaxExtractedBytes, int maxEntries = MaxExtractedEntries)
    {
        var root = Path.GetFullPath(destination).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        TestRootGuard.Check(destination);
        Directory.CreateDirectory(destination);
        var files = new List<string>();
        var zone = ReadZone(zipPath);
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            if (zip.Entries.Count > maxEntries)
                throw new ActionException($"'{Path.GetFileName(zipPath)}' has {zip.Entries.Count:N0} items, more than Automation Portal extracts at once ({maxEntries:N0}), so it wasn't extracted.");
            long declared = 0;
            foreach (var entry in zip.Entries) declared += entry.Length;
            if (declared > maxBytes) throw TooBig(zipPath, maxBytes);

            long written = 0;
            var buffer = new byte[81920];
            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                // "report.txt:x" would write a hidden stream onto a file that is already there.
                if (entry.FullName.Contains(':'))
                    throw new ActionException($"'{Path.GetFileName(zipPath)}' contains a file name with a colon (:), which Windows can't use, so it wasn't extracted.");
                // Zip-slip guard: every entry must land inside the destination folder.
                var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new ActionException($"'{Path.GetFileName(zipPath)}' contains files that try to leave the destination folder, so it wasn't extracted.");
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    TestRootGuard.Check(target);
                    Directory.CreateDirectory(target);
                    continue;
                }
                TestRootGuard.Check(target);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target) || Directory.Exists(target)) target = OutputTarget.UniquePath(Path.GetDirectoryName(target)!, Path.GetFileName(target));
                using (var input = entry.Open())
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    files.Add(target);
                    // Count what really comes out, not what the zip claims, so a lying header can't fill the disk.
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        written += read;
                        if (written > maxBytes) throw TooBig(zipPath, maxBytes);
                        output.Write(buffer, 0, read);
                    }
                }
                try { File.SetLastWriteTime(target, entry.LastWriteTime.DateTime); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
                if (zone is not null) WriteZone(target, zone);
            }
        }
        catch (InvalidDataException ex)
        {
            RemoveCreated(files);
            throw new ActionException($"'{Path.GetFileName(zipPath)}' isn't a valid zip file, or it is damaged.", ex);
        }
        catch
        {
            RemoveCreated(files);
            throw;
        }
        return files;
    }

    /// <summary>
    /// The zip's Mark of the Web (its Zone.Identifier stream, which Windows adds to downloads), or null. Extracted files carry
    /// it on, so Windows still warns before opening a program that came out of a downloaded zip.
    /// </summary>
    private static string? ReadZone(string zipPath)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try { return File.ReadAllText(Path.GetFullPath(zipPath) + ":Zone.Identifier"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    /// <summary>Adds the Mark of the Web to a file this extraction just created. Drives without streams (FAT) simply don't get it.</summary>
    private static void WriteZone(string createdFile, string zone)
    {
        try
        {
            using var stream = new FileStream(createdFile + ":Zone.Identifier", FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(zone);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
    }

    private static ActionException TooBig(string zipPath, long maxBytes) =>
        new($"'{Path.GetFileName(zipPath)}' would unpack to more than {maxBytes / (1024.0 * 1024 * 1024):0.#} GB, so it wasn't extracted.");

    /// <summary>Removes files this extraction created (each was opened with CreateNew, so none existed before).</summary>
    private static void RemoveCreated(List<string> files)
    {
        foreach (var f in files)
            try { File.Delete(f); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
