using System.Globalization;

namespace Flowmate.Actions.Documents;

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
                else
                {
                    try { File.Copy(src, target); }
                    catch { TryDeleteFile(target); throw; }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (aside is not null) RestoreAside(aside, target, destDir);
                throw ex is IOException
                    ? new ActionException($"Couldn't {(move ? "move" : "copy")} '{Path.GetFileName(src)}'. It may be open in another program. ({ex.Message})", ex)
                    : new ActionException($"Flow Mate isn't allowed to {(move ? "move" : "copy")} '{Path.GetFileName(src)}' there.", ex);
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
        var aside = target.TrimEnd('\\', '/') + ".flowmate-old-" + Guid.NewGuid().ToString("N")[..8];
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
        var temp = target + ".flowmate-new-" + Guid.NewGuid().ToString("N")[..8];
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

    private static void KeepAside(string aside, string destDir)
    {
        var original = Path.GetFileName(aside.TrimEnd('\\', '/'));
        var name = original[..original.LastIndexOf(".flowmate-old-", StringComparison.Ordinal)];
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
            CopyDirectoryFresh(src, target);
            // Copy, verify, then delete: the original goes only when the copy has every file at its full size.
            var (srcCount, srcBytes) = Measure(src);
            var (dstCount, dstBytes) = Measure(target);
            var name = Path.GetFileName(src.TrimEnd('\\', '/'));
            if (srcCount != dstCount || srcBytes != dstBytes)
            {
                TryDeleteDirectory(target);   // the copy is ours: CopyDirectoryFresh only ever renames a new folder into place
                throw new ActionException($"Couldn't move the folder '{name}': the copy didn't match the original ({dstCount} of {srcCount} files, {dstBytes:N0} of {srcBytes:N0} bytes). The original was left where it was.");
            }
            try { Directory.Delete(src, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Every file is safe in the new place; some originals are still there too (one was open, say). That's a move with a leftover, not a failure.
                return $"Moved '{name}' to '{target}', but some of the original files couldn't be removed ({ex.Message}). They are still in '{src}'.";
            }
            return null;
        }
    }

    /// <summary>
    /// Copies a folder to <paramref name="target"/>, which must not exist: the copy is made in a new hidden folder next to it and then
    /// renamed into place, so a folder something else creates at that name meanwhile is never merged into or deleted. On failure only
    /// that new folder is removed.
    /// </summary>
    private static void CopyDirectoryFresh(string src, string target)
    {
        var full = Path.GetFullPath(target).TrimEnd('\\', '/');
        var parent = Path.GetDirectoryName(full)!;
        var staging = Path.Combine(parent, "." + Path.GetFileName(full) + ".flowmate-copying-" + Guid.NewGuid().ToString("N")[..8]);
        if (Directory.Exists(staging) || File.Exists(staging)) throw new IOException($"'{staging}' already exists.");
        try
        {
            CopyTree(src, staging);
            Directory.Move(staging, full);
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }
    }

    /// <summary>Number of files and their total size in a folder and all its subfolders, walked the same way as the copy.</summary>
    internal static (int Files, long Bytes) Measure(string folder)
    {
        var count = 0;
        long bytes = 0;
        foreach (var f in new DirectoryInfo(folder).EnumerateFiles()) { count++; bytes += f.Length; }
        foreach (var d in Directory.EnumerateDirectories(folder))
        {
            var (c, b) = Measure(d);
            count += c;
            bytes += b;
        }
        return (count, bytes);
    }

    public static void CopyDirectory(string src, string target) => CopyDirectory(src, target, cleanupOnFailure: false);

    private static void CopyDirectory(string src, string target, bool cleanupOnFailure)
    {
        try { CopyTree(src, target); }
        catch when (cleanupOnFailure) { TryDeleteDirectory(target); throw; }
    }

    private static void CopyTree(string src, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var f in Directory.EnumerateFiles(src)) File.Copy(f, Path.Combine(target, Path.GetFileName(f)), true);
        foreach (var d in Directory.EnumerateDirectories(src)) CopyTree(d, Path.Combine(target, Path.GetFileName(d)));
    }
}