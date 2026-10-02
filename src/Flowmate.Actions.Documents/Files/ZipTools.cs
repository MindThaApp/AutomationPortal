using System.IO.Compression;

namespace Flowmate.Actions.Documents;

public static class ZipTools
{
    /// <summary>Zips files and folders (folders keep their structure). Returns the zip path.</summary>
    public static string Create(IReadOnlyList<string> items, string zipPath, CancellationToken ct = default)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fullZip = Path.GetFullPath(zipPath);
        try
        {
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
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
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
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

    /// <summary>Extracts into <paramref name="destination"/>, never overwriting and never writing outside it. Returns the extracted files.</summary>
    public static IReadOnlyList<string> Extract(string zipPath, string destination, CancellationToken ct = default)
    {
        var root = Path.GetFullPath(destination).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(destination);
        var files = new List<string>();
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new ActionException($"'{Path.GetFileName(zipPath)}' contains files that try to leave the destination folder, so it wasn't extracted.");
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target)) target = OutputTarget.UniquePath(Path.GetDirectoryName(target)!, Path.GetFileName(target));
                entry.ExtractToFile(target, false);
                files.Add(target);
            }
        }
        catch (InvalidDataException ex)
        {
            throw new ActionException($"'{Path.GetFileName(zipPath)}' isn't a valid zip file, or it is damaged.", ex);
        }
        return files;
    }
}
