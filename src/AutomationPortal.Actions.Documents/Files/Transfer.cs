using System.Globalization;

namespace AutomationPortal.Actions.Documents;

public enum ConflictMode { KeepBoth, Replace, Skip }

public sealed record TransferResult(IReadOnlyList<string> Paths, int Skipped)
{
    /// <summary>Things the user should know although everything arrived (originals that couldn't be removed after a move).</summary>
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

/// <summary>Move / copy files and folders into a destination, optionally sorted into subfolders by {kind} {year} {month} {day} {ext}.</summary>
public static class Transfer
{
    public static ConflictMode ParseMode(string id) => id.ToLowerInvariant() switch
    {
        "replace" => ConflictMode.Replace,
        "skip" => ConflictMode.Skip,
        _ => ConflictMode.KeepBoth,
    };

    public static string ExpandSubfolder(string template, string path)
    {
        if (string.IsNullOrWhiteSpace(template)) return "";
        var isDir = Directory.Exists(path);
        DateTime when;
        try { when = isDir ? Directory.GetLastWriteTime(path) : File.GetLastWriteTime(path); } catch { when = DateTime.Now; }
        var ext = isDir ? "" : Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var expanded = template
            .Replace("{kind}", isDir ? "Folders" : FileKinds.Of(path), StringComparison.OrdinalIgnoreCase)
            .Replace("{year}", when.Year.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{monthName}", when.ToString("MMMM", CultureInfo.CurrentCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{month}", when.Month.ToString("00", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{day}", when.Day.ToString("00", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{ext}", ext.Length == 0 ? "No extension" : ext, StringComparison.OrdinalIgnoreCase);
        var segments = expanded.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NameTokens.SafeName).Where(s => s.Length > 0 && s != "." && s != "..");
        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    public static TransferResult Run(IEnumerable<string> sources, string destinationRoot, string subfolderTemplate,
        ConflictMode mode, bool move, CancellationToken ct = default)
    {
        Directory.CreateDirectory(destinationRoot);
        var results = new List<string>();
        var notes = new List<string>();
        var skipped = 0;
        foreach (var src in sources)
        {
            ct.ThrowIfCancellationRequested();
            var isDir = Directory.Exists(src);
            if (!isDir && !File.Exists(src))
                throw new ActionException($"Couldn't find '{Path.GetFileName(src)}'. Check the file still exists.");
            if (move) ProtectedPaths.Ensure(src, "move");

            var sub = ExpandSubfolder(subfolderTemplate, src);
            var destDir = sub.Length == 0 ? destinationRoot : Path.Combine(destinationRoot, sub);
            var target = Path.Combine(destDir, Path.GetFileName(src.TrimEnd('\\', '/')));

            if (isDir && IsInside(destDir, src))
                throw new ActionException($"Can't put the folder '{Path.GetFileName(src)}' inside itself. Choose a different destination.");

            if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                if (move) { results.Add(src); continue; }
                // copying onto itself: only "keep both" makes sense
                if (mode != ConflictMode.KeepBoth) { skipped++; continue; }
            }

            Directory.CreateDirectory(destDir);
            string? aside = null;
            if (File.Exists(target) || Directory.Exists(target))
            {
                switch (mode)
                {
                    case ConflictMode.Skip: skipped++; continue;
                    case ConflictMode.KeepBoth: target = OutputTarget.UniquePath(destDir, Path.GetFileName(target)); break;
                    case ConflictMode.Replace:
                        ProtectedPaths.Ensure(target, "replace");
                        if (Directory.Exists(target) != isDir)
                            throw new ActionException(isDir
                                ? $"Can't replace the file '{Path.GetFileName(target)}' with the folder '{Path.GetFileName(src)}'. Choose \"Keep both\" or a different destination."
                                : $"Can't replace the folder '{Path.GetFileName(target)}' with the file '{Path.GetFileName(src)}'. Choose \"Keep both\" or a different destination.");
                        if (Directory.Exists(target) && IsInside(src, target))
                            throw new ActionException($"Can't replace '{Path.GetFileName(target)}' because '{Path.GetFileName(src)}' is inside it. Choose a different destination.");
                        aside = MoveAside(target);
                        break;
                }
            }

            string? note = null;
            try
            {
                if (isDir)
                {
                    if (move) note = MoveDirectory(src, target);
                    else CopyDirectoryFresh(src, target);
                }
                else if (move) File.Move(src, target);
                else CopyFileFresh(src, target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (aside is not null) RestoreAside(aside, target, destDir);
                throw ex is IOException
                    ? new ActionException($"Couldn't {(move ? "move" : "copy")} '{Path.GetFileName(src)}'. It may be open in another program. ({ex.Message})", ex)
                    : new ActionException($"Automation Portal isn't allowed to {(move ? "move" : "copy")} '{Path.GetFileName(src)}' there.", ex);
            }
            catch
            {
                if (aside is not null) RestoreAside(aside, target, destDir);
                throw;
            }
            if (aside is not null) DiscardAside(aside, destDir);
            results.Add(target);
            if (note is not null) notes.Add(note);
        }
        return new TransferResult(results, skipped) { Notes = notes };
    }

    internal static string MoveAside(string target)
    {
        var aside = target.TrimEnd('\\', '/') + ".autoportal-old-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            if (Directory.Exists(target)) Directory.Move(target, aside); else File.Move(target, aside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ActionException($"Couldn't replace '{Path.GetFileName(target)}'. It may be open in another program.", ex);
        }
        return aside;
    }

    /// <summary>Put the replaced item back; if something is already at the target (a finished copy whose source cleanup failed) keep both.</summary>
    internal static void RestoreAside(string aside, string target, string destDir)
    {
        try
        {
            if (!File.Exists(target) && !Directory.Exists(target))
            {
                if (Directory.Exists(aside)) Directory.Move(aside, target); else File.Move(aside, target);
            }
            else KeepAside(aside, destDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The new item is in place; the old one goes to the Recycle Bin, or stays next to it when that isn't possible.</summary>
    internal static void DiscardAside(string aside, string destDir)
    {
        try { if (RecycleBin.TrySend(aside)) return; } catch (ActionException) { }
        try { KeepAside(aside, destDir); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Replaces the file <paramref name="target"/> with a copy of <paramref name="src"/>. The copy is made first, then the
    /// old file is moved aside and sent to the Recycle Bin (or kept as "name (replaced)" when it can't go there).
    /// </summary>
    internal static void ReplaceWithCopy(string src, string target)
    {
        var destDir = Path.GetDirectoryName(Path.GetFullPath(target))!;
        var temp = target + ".autoportal-new-" + Guid.NewGuid().ToString("N")[..8];
        try { File.Copy(src, temp); }
        catch { TryDeleteFile(temp); throw; }
        string aside;
        try { aside = MoveAside(target); }
        catch { TryDeleteFile(temp); throw; }
        try { File.Move(temp, target); }
        catch
        {
            RestoreAside(aside, target, destDir);
            TryDeleteFile(temp);
            throw;
        }
        DiscardAside(aside, destDir);
    }

    /// <summary>
    /// Copies a file to <paramref name="target"/>, which must not exist: the copy is made under a new unique name next to it and then
    /// renamed into place without replacing anything. On failure only that new file is removed, never a file something else made
    /// at <paramref name="target"/> meanwhile.
    /// </summary>
    private static void CopyFileFresh(string src, string target)
    {
        var temp = target + ".autoportal-new-" + Guid.NewGuid().ToString("N")[..8];
        if (File.Exists(temp) || Directory.Exists(temp)) throw new IOException($"'{temp}' already exists.");
        try
        {
            File.Copy(src, temp);
            File.Move(temp, target, overwrite: false);
        }
        catch
        {
            TryDeleteFile(temp);
            throw;
        }
    }

    private static void KeepAside(string aside, string destDir)
    {
        var original = Path.GetFileName(aside.TrimEnd('\\', '/'));
        var name = original[..original.LastIndexOf(".autoportal-old-", StringComparison.Ordinal)];
        var keep = OutputTarget.UniquePath(destDir, name + " (replaced)");
        if (Directory.Exists(aside)) Directory.Move(aside, keep); else File.Move(aside, keep);
    }

    private static void TryDeleteFile(string path) { try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
    private static void TryDeleteDirectory(string path) { try { Directory.Delete(path, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
    private static bool IsInside(string candidate, string folder)
    {
        var c = Path.GetFullPath(candidate).TrimEnd('\\') + "\\";
        var f = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
        return c.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Moves a folder; across drives it copies, checks the copy, then removes the original. Returns a note for the user, or null.</summary>
    private static string? MoveDirectory(string src, string target)
    {
        try { Directory.Move(src, target); return null; }
        catch (IOException) when (!string.Equals(Path.GetPathRoot(Path.GetFullPath(src)), Path.GetPathRoot(Path.GetFullPath(target)), StringComparison.OrdinalIgnoreCase)
                                  && !Directory.Exists(target) && !File.Exists(target))
        {
            return MoveByCopy(src, target);
        }
    }

    /// <summary>
    /// Copy, verify, then delete. The list of files is taken first; the copy is checked against it before it is renamed into place,
    /// and afterwards only those files are removed from the original, each only if it is unchanged since it was listed. Files added
    /// or changed meanwhile, and folders that aren't empty then, stay where they are. Returns a note for the user, or null.
    /// </summary>
    internal static string? MoveByCopy(string src, string target, Action? beforeRemovingOriginals = null)
    {
        var name = Path.GetFileName(src.TrimEnd('\\', '/'));
        var listed = ListFiles(src);
        CopyDirectoryFresh(src, target, staging =>
        {
            var missing = listed.Count(f => new FileInfo(Path.Combine(staging, f.Key)) is not { Exists: true } c || c.Length != f.Value.Length);
            if (missing > 0)
                throw new ActionException($"Couldn't move the folder '{name}': {missing} of its {listed.Count} files didn't copy completely. The original was left where it was.");
        });

        beforeRemovingOriginals?.Invoke();
        string? problem = null;
        foreach (var (relative, (length, written)) in listed)
        {
            try
            {
                var original = new FileInfo(Path.Combine(src, relative));
                if (original.Exists && original.Length == length && original.LastWriteTimeUtc == written) original.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { problem ??= ex.Message; }
        }
        RemoveEmptyFolders(new DirectoryInfo(src));
        if (!Directory.Exists(src)) return null;
        // Every listed file is safe in the new place; something is still in the original (a file that was open, or one added during the move).
        return $"Moved '{name}' to '{target}', but some files stayed in '{src}'{(problem is null ? " (they were added or changed during the move)" : $" ({problem})")}.";
    }

    /// <summary>Every file in a folder and its subfolders (not following junctions or links), by relative path, with its size and time.</summary>
    private static Dictionary<string, (long Length, DateTime Written)> ListFiles(string root)
    {
        var files = new Dictionary<string, (long, DateTime)>(StringComparer.OrdinalIgnoreCase);
        void Walk(DirectoryInfo dir, string relative)
        {
            foreach (var f in dir.EnumerateFiles()) files[Path.Combine(relative, f.Name)] = (f.Length, f.LastWriteTimeUtc);
            foreach (var d in dir.EnumerateDirectories())
                if (!d.Attributes.HasFlag(FileAttributes.ReparsePoint)) Walk(d, Path.Combine(relative, d.Name));
        }
        Walk(new DirectoryInfo(root), "");
        return files;
    }

    /// <summary>Removes the folder and its subfolders that are empty, children first, one at a time; junctions and links are left alone.</summary>
    private static void RemoveEmptyFolders(DirectoryInfo dir)
    {
        try
        {
            foreach (var d in dir.EnumerateDirectories())
                if (!d.Attributes.HasFlag(FileAttributes.ReparsePoint)) RemoveEmptyFolders(d);
            if (!dir.EnumerateFileSystemInfos().Any()) dir.Delete(recursive: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Copies a folder to <paramref name="target"/>, which must not exist: the copy is made in a new hidden folder next to it and then
    /// renamed into place, so a folder something else creates at that name meanwhile is never merged into or deleted. <paramref name="check"/>
    /// can look at the copy before it is renamed and throw to stop. On failure only that new folder is removed.
    /// </summary>
    private static void CopyDirectoryFresh(string src, string target, Action<string>? check = null)
    {
        var full = Path.GetFullPath(target).TrimEnd('\\', '/');
        var parent = Path.GetDirectoryName(full)!;
        var staging = Path.Combine(parent, "." + Path.GetFileName(full) + ".autoportal-copying-" + Guid.NewGuid().ToString("N")[..8]);
        if (Directory.Exists(staging) || File.Exists(staging)) throw new IOException($"'{staging}' already exists.");
        try
        {
            CopyTree(src, staging);
            check?.Invoke(staging);
            Directory.Move(staging, full);
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }
    }

    // Only ever writes into a new staging folder, so nothing is there to replace.
    private static void CopyTree(string src, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var f in Directory.EnumerateFiles(src)) File.Copy(f, Path.Combine(target, Path.GetFileName(f)), overwrite: false);
        foreach (var d in Directory.EnumerateDirectories(src)) CopyTree(d, Path.Combine(target, Path.GetFileName(d)));
    }
}
