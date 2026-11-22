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
    /// <summary>Added to a planned permanent delete that happens only because the drive has no Recycle Bin.</summary>
    public const string NoRecycleBinNote = "this drive has no Recycle Bin";

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

    /// <summary>
    /// Moves or copies the sources. Changes are recorded in <paramref name="journal"/>. With <paramref name="preview"/> or
    /// <paramref name="plan"/> nothing is touched: each planned step is described there instead (the result's paths are where
    /// items would go). <paramref name="step"/> names the step in the plan.
    /// </summary>
    public static TransferResult Run(IEnumerable<string> sources, string destinationRoot, string subfolderTemplate,
        ConflictMode mode, bool move, CancellationToken ct = default, RunFileJournal? journal = null, List<string>? preview = null,
        Func<string, bool>? recycle = null, FileChangePlan? plan = null, string step = "")
    {
        var dryRun = preview is not null || plan is not null;
        if (!dryRun) { TestRootGuard.Check(destinationRoot); Directory.CreateDirectory(destinationRoot); }
        var results = new List<string>();
        var notes = new List<string>();
        var planned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skipped = 0;
        var verb = move ? "move" : "copy";
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
                if (move)
                {
                    preview?.Add($"{Path.GetFileName(src)}: already there");
                    plan?.Add(new PlannedChange(PlannedChangeKind.Skip, step, src, Note: "already there"));
                    results.Add(src);
                    continue;
                }
                // copying onto itself: only "keep both" makes sense
                if (mode != ConflictMode.KeepBoth)
                {
                    preview?.Add($"{Path.GetFileName(src)}: skipped, it is already there");
                    plan?.Add(new PlannedChange(PlannedChangeKind.Skip, step, src, Note: "it is already there"));
                    skipped++;
                    continue;
                }
            }

            if (dryRun)
            {
                bool Taken(string p) => planned.Contains(p) || (plan?.IsTaken(p) ?? (File.Exists(p) || Directory.Exists(p)));
                var taken = Taken(target);
                if (taken && mode == ConflictMode.Skip)
                {
                    preview?.Add($"{Path.GetFileName(src)}: skipped, {target} already exists");
                    plan?.Add(new PlannedChange(PlannedChangeKind.Skip, step, src, target, "a file with that name is already there"));
                    skipped++;
                    continue;
                }
                if (taken && mode == ConflictMode.KeepBoth)
                {
                    // The same names OutputTarget.UniquePath gives when it really runs: "name (2).ext", "name (3).ext" …
                    var stem = Path.GetFileNameWithoutExtension(target);
                    var ext = Path.GetExtension(target);
                    for (var n = 2; Taken(target); n++)
                        target = Path.Combine(destDir, $"{stem} ({n}){ext}");
                }
                planned.Add(target);
                var replacing = taken && mode == ConflictMode.Replace;
                var bin = !replacing || RecycleBin.DriveHasBin(target);
                preview?.Add(replacing
                    ? bin ? $"{verb} {src} → {target} (the one there now goes to the Recycle Bin)"
                          : $"{verb} {src} → {target} (the one there now is deleted permanently: {NoRecycleBinNote})"
                    : $"{verb} {src} → {target}");
                if (replacing)
                    plan?.Add(bin ? new PlannedChange(PlannedChangeKind.Recycle, step, target, Note: "replaced by the new one")
                                  : new PlannedChange(PlannedChangeKind.Delete, step, target, Note: "replaced by the new one; " + NoRecycleBinNote));
                plan?.Add(new PlannedChange(move ? PlannedChangeKind.Move : PlannedChangeKind.Copy, step, src, target));
                results.Add(target);
                continue;
            }

            TestRootGuard.Check(destDir);
            Directory.CreateDirectory(destDir);
            string? aside = null, backup = null;
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
                        if (!isDir) backup = journal?.BackUp(target);
                        aside = MoveAside(target);
                        break;
                }
            }

            string? note = null;
            var links = new List<string>();
            try
            {
                if (isDir)
                {
                    if (move) note = MoveFolder(src, target);
                    else CopyDirectoryFresh(src, target, skipped: links);
                }
                else if (move) note = MoveFile(src, target);
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
            if (aside is not null) DiscardAside(aside, destDir, recycle);
            if (journal is not null)
            {
                if (aside is not null) journal.Replaced(target, backup);
                // A move whose original stayed behind (it changed meanwhile, or couldn't be removed) left a copy, not a move.
                if (move && !isDir && File.Exists(src)) { if (aside is null) journal.Created(target); }
                else if (move) journal.Moved(src, target);
                else if (aside is null) journal.Created(target);
            }
            results.Add(target);
            if (note is not null) notes.Add(note);
            if (links.Count > 0) notes.Add(SkippedLinksNote(Path.GetFileName(src.TrimEnd('\\', '/')), links));
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
    internal static void DiscardAside(string aside, string destDir, Func<string, bool>? recycle = null)
    {
        try { if ((recycle ?? RecycleBin.TrySend)(aside)) return; } catch (ActionException) { }
        try { KeepAside(aside, destDir); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Replaces the file <paramref name="target"/> with a copy of <paramref name="src"/>. The copy is made first, then the
    /// old file is moved aside and sent to the Recycle Bin (or kept as "name (replaced)" when it can't go there).
    /// </summary>
    internal static void ReplaceWithCopy(string src, string target, Func<string, bool>? recycle = null)
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
        DiscardAside(aside, destDir, recycle);
    }

    /// <summary>
    /// Copies a file to <paramref name="target"/>, which must not exist: the copy is made under a new unique name next to it and then
    /// renamed into place without replacing anything. On failure only that new file is removed, never a file something else made
    /// at <paramref name="target"/> meanwhile.
    /// </summary>
    private static void CopyFileFresh(string src, string target, Action<string>? check = null)
    {
        var temp = target + ".autoportal-new-" + Guid.NewGuid().ToString("N")[..8];
        if (File.Exists(temp) || Directory.Exists(temp)) throw new IOException($"'{temp}' already exists.");
        try
        {
            File.Copy(src, temp);
            check?.Invoke(temp);
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
    internal static string? MoveFolder(string src, string target)
    {
        try { Directory.Move(src, target); return null; }
        catch (IOException) when (!SameDrive(src, target) && !Directory.Exists(target) && !File.Exists(target))
        {
            return MoveByCopy(src, target);
        }
    }

    private static bool SameDrive(string a, string b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Moves a file to <paramref name="target"/>, which must not exist. On the same drive it is a rename; across drives it is
    /// copy, verify, then delete (see <see cref="MoveFileByCopy"/>). Returns a note for the user, or null.
    /// </summary>
    internal static string? MoveFile(string src, string target)
    {
        if (!SameDrive(src, target)) return MoveFileByCopy(src, target);
        File.Move(src, target, overwrite: false);
        return null;
    }

    /// <summary>
    /// Copies the file next to <paramref name="target"/>, checks the copy has the same size and contents, renames it into place,
    /// and only then deletes the original, and only if it didn't change meanwhile. Returns a note when the original stayed.
    /// </summary>
    /// <param name="afterCopy">Tests only: called with the unverified copy.</param>
    internal static string? MoveFileByCopy(string src, string target, Action<string>? afterCopy = null)
    {
        var before = new FileInfo(src);
        // Held open without write sharing from hashing until the original is deleted, so nothing can change it in between.
        // FileShare.Delete lets the original be deleted through another handle while this one is open.
        using (var hold = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
        {
            var length = hold.Length;
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(hold));
            CopyFileFresh(src, target, copy =>
            {
                afterCopy?.Invoke(copy);
                if (new FileInfo(copy).Length != length || !string.Equals(Sha256Hex(copy), hash, StringComparison.OrdinalIgnoreCase))
                    throw new ActionException($"Couldn't move '{before.Name}': the copy didn't match the original. The original was left where it was.");
            });
            try { File.Delete(src); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return $"Copied '{before.Name}' to '{target}', but the original couldn't be removed from '{before.DirectoryName}' ({ex.Message}).";
            }
        }
        return null;
    }

    internal static string Sha256Hex(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs));
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
    private static void CopyDirectoryFresh(string src, string target, Action<string>? check = null, List<string>? skipped = null)
    {
        var full = Path.GetFullPath(target).TrimEnd('\\', '/');
        var parent = Path.GetDirectoryName(full)!;
        var staging = Path.Combine(parent, "." + Path.GetFileName(full) + ".autoportal-copying-" + Guid.NewGuid().ToString("N")[..8]);
        if (Directory.Exists(staging) || File.Exists(staging)) throw new IOException($"'{staging}' already exists.");
        try
        {
            CopyTree(src, staging, skipped);
            check?.Invoke(staging);
            Directory.Move(staging, full);
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }
    }

    /// <summary>
    /// Copies a folder's files and subfolders. Only ever writes into a new staging folder, so nothing is there to replace.
    /// Junctions and folder links are not followed (their contents belong somewhere else, they can be locked, and they can loop):
    /// they are left out and listed in <paramref name="skipped"/>.
    /// </summary>
    internal static void CopyTree(string src, string target, List<string>? skipped = null)
    {
        Directory.CreateDirectory(target);
        var dir = new DirectoryInfo(src);
        foreach (var f in dir.EnumerateFiles())
        {
            if (IsFolderLink(f)) { skipped?.Add(f.FullName); continue; }
            File.Copy(f.FullName, Path.Combine(target, f.Name), overwrite: false);
        }
        foreach (var d in dir.EnumerateDirectories())
        {
            if (d.Attributes.HasFlag(FileAttributes.ReparsePoint)) { skipped?.Add(d.FullName); continue; }
            CopyTree(d.FullName, Path.Combine(target, d.Name), skipped);
        }
    }

    /// <summary>A link listed as a file that really points at a folder.</summary>
    private static bool IsFolderLink(FileInfo f)
    {
        if (!f.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
        if (f.Attributes.HasFlag(FileAttributes.Directory)) return true;
        try { return f.LinkTarget is { } link && Directory.Exists(Path.Combine(f.DirectoryName ?? "", link)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>The sentence for the step result when linked folders were left out of a copy.</summary>
    internal static string SkippedLinksNote(string folderName, IReadOnlyList<string> skipped)
    {
        var names = string.Join(", ", skipped.Take(5).Select(p => $"'{Path.GetFileName(p.TrimEnd('\\', '/'))}'"));
        var more = skipped.Count > 5 ? $" and {skipped.Count - 5} more" : "";
        return skipped.Count == 1
            ? $"Skipped the linked folder {names} in '{folderName}'. It is a junction or link to another place, so its contents weren't copied."
            : $"Skipped {skipped.Count} linked folders in '{folderName}': {names}{more}. They are junctions or links to other places, so their contents weren't copied.";
    }
}
