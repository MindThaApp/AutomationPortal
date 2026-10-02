using System.Globalization;

namespace Flowmate.Actions.Documents;

public enum ConflictMode { KeepBoth, Replace, Skip }

public sealed record TransferResult(IReadOnlyList<string> Paths, int Skipped);

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
        var skipped = 0;
        foreach (var src in sources)
        {
            ct.ThrowIfCancellationRequested();
            var isDir = Directory.Exists(src);
            if (!isDir && !File.Exists(src))
                throw new ActionException($"Couldn't find '{Path.GetFileName(src)}'. Check the file still exists.");

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
            if (File.Exists(target) || Directory.Exists(target))
            {
                switch (mode)
                {
                    case ConflictMode.Skip: skipped++; continue;
                    case ConflictMode.KeepBoth: target = OutputTarget.UniquePath(destDir, Path.GetFileName(target)); break;
                    case ConflictMode.Replace:
                        if (Directory.Exists(target) && !isDir) Directory.Delete(target, true);
                        else if (File.Exists(target) && isDir) File.Delete(target);
                        else if (Directory.Exists(target)) Directory.Delete(target, true);
                        break;
                }
            }

            try
            {
                if (isDir)
                {
                    if (move) MoveDirectory(src, target);
                    else CopyDirectory(src, target);
                }
                else if (move) File.Move(src, target, overwrite: true);
                else File.Copy(src, target, overwrite: true);
            }
            catch (IOException ex)
            {
                throw new ActionException($"Couldn't {(move ? "move" : "copy")} '{Path.GetFileName(src)}'. It may be open in another program. ({ex.Message})", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new ActionException($"Flow Mate isn't allowed to {(move ? "move" : "copy")} '{Path.GetFileName(src)}' there.", ex);
            }
            results.Add(target);
        }
        return new TransferResult(results, skipped);
    }

    private static bool IsInside(string candidate, string folder)
    {
        var c = Path.GetFullPath(candidate).TrimEnd('\\') + "\\";
        var f = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
        return c.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }

    private static void MoveDirectory(string src, string target)
    {
        try { Directory.Move(src, target); }
        catch (IOException) when (!string.Equals(Path.GetPathRoot(Path.GetFullPath(src)), Path.GetPathRoot(Path.GetFullPath(target)), StringComparison.OrdinalIgnoreCase))
        {
            CopyDirectory(src, target);
            Directory.Delete(src, true);
        }
    }

    public static void CopyDirectory(string src, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var f in Directory.EnumerateFiles(src)) File.Copy(f, Path.Combine(target, Path.GetFileName(f)), true);
        foreach (var d in Directory.EnumerateDirectories(src)) CopyDirectory(d, Path.Combine(target, Path.GetFileName(d)));
    }
}
