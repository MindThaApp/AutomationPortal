namespace AutomationPortal.Core;

/// <summary>
/// The app's data folders used to be %APPDATA%\Flowmate and %LOCALAPPDATA%\Flowmate. When the new folder is missing and the old
/// one is there, the old tree is copied (never moved, never changed) so nothing is lost. The copy is built beside the final
/// name, checked file by file, and only then renamed into place; if anything goes wrong the old folder is used for this run.
/// </summary>
internal static class DataRootMigration
{
    /// <summary>The folder name used before the rename, under %APPDATA% and %LOCALAPPDATA%.</summary>
    internal const string LegacyFolderName = "Flowmate";
    internal const string TempMarker = ".migrating-";

    /// <summary>Returns the folder to use: <paramref name="newRoot"/> when it exists or was copied from <paramref name="oldRoot"/>, otherwise the old one.</summary>
    internal static string Resolve(string oldRoot, string newRoot, Action<string>? log = null)
    {
        RemoveStaleCopies(newRoot);
        try
        {
            if (Directory.Exists(newRoot)) return newRoot;
            if (!Directory.Exists(oldRoot)) return newRoot;
            if (string.Equals(Path.GetFullPath(oldRoot).TrimEnd('\\', '/'), Path.GetFullPath(newRoot).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return newRoot;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return newRoot;
        }

        try
        {
            if (FindLink(oldRoot) is { } link)
            {
                log?.Invoke($"Did not copy the data in '{oldRoot}' because it contains a linked folder or file ('{link}'). Using the old folder.");
                return oldRoot;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            log?.Invoke($"Could not check the data in '{oldRoot}' ({ex.Message}). Using the old folder for now.");
            return oldRoot;
        }

        var temp = newRoot.TrimEnd('\\', '/') + TempMarker + Guid.NewGuid().ToString("N")[..8];
        try
        {
            CopyTree(oldRoot, temp);
            var before = Measure(oldRoot);
            var after = Measure(temp);
            if (!Same(before, after)) throw new IOException("the copy does not match the original");
            Directory.Move(temp, newRoot);
            log?.Invoke($"Copied the data in '{oldRoot}' to '{newRoot}' ({before.Count} files). The old folder was left as it was.");
            return newRoot;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            RemoveOwnTemp(temp);
            if (Directory.Exists(newRoot)) return newRoot;   // another copy of the app finished it first
            log?.Invoke($"Could not copy the data in '{oldRoot}' to '{newRoot}' ({ex.Message}). Using the old folder for now.");
            return oldRoot;
        }
    }

    /// <summary>Folders the app recreates itself (swept at start, filled per run), so they are not copied: <c>temp</c> directly under the root.</summary>
    internal const string DisposableFolder = "temp";

    private static bool IsDisposable(string root, string dir) =>
        string.Equals(Path.GetRelativePath(root, dir), DisposableFolder, StringComparison.OrdinalIgnoreCase);

    private static readonly EnumerationOptions Flat = new() { AttributesToSkip = 0, IgnoreInaccessible = false };

    /// <summary>All folders (relative walk, no links followed) and files under root, leaving out the disposable folder.</summary>
    private static void Walk(string root, List<string> dirs, List<string> files)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir, "*", Flat))
            {
                if (IsDisposable(root, sub)) continue;
                dirs.Add(sub);
                pending.Push(sub);
            }
            files.AddRange(Directory.EnumerateFiles(dir, "*", Flat));
        }
    }

    /// <summary>The first junction, symlink or other reparse point under root (outside the disposable folder), or null.</summary>
    private static string? FindLink(string root)
    {
        if (new DirectoryInfo(root).Attributes.HasFlag(FileAttributes.ReparsePoint)) return root;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", Flat))
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) return entry.FullName;
                if (entry is DirectoryInfo && !IsDisposable(root, entry.FullName)) pending.Push(entry.FullName);
            }
        }
        return null;
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        var dirs = new List<string>();
        var files = new List<string>();
        Walk(from, dirs, files);
        foreach (var dir in dirs) Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in files) File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: false);
    }

    private static Dictionary<string, long> Measure(string root)
    {
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var files = new List<string>();
        Walk(root, new List<string>(), files);
        foreach (var file in files) map[Path.GetRelativePath(root, file)] = new FileInfo(file).Length;
        return map;
    }

    /// <summary>
    /// Removes leftover half-made copies from a crash: sibling folders named exactly "&lt;new name&gt;.migrating-" plus 8 hex digits.
    /// A folder touched within the last hour may belong to another copy of the app that is still copying, so it is left.
    /// </summary>
    private static void RemoveStaleCopies(string newRoot)
    {
        try
        {
            var full = Path.GetFullPath(newRoot).TrimEnd('\\', '/');
            var parent = Path.GetDirectoryName(full);
            if (parent is null || !Directory.Exists(parent)) return;
            var pattern = new System.Text.RegularExpressions.Regex("^" + System.Text.RegularExpressions.Regex.Escape(Path.GetFileName(full) + TempMarker) + "[0-9a-f]{8}$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            foreach (var dir in Directory.EnumerateDirectories(parent))
            {
                var info = new DirectoryInfo(dir);
                if (!pattern.IsMatch(info.Name) || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                if (DateTime.UtcNow - info.LastWriteTimeUtc < TimeSpan.FromHours(1)) continue;
                try { info.Delete(recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
    }

    private static bool Same(Dictionary<string, long> a, Dictionary<string, long> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (name, size) in a)
            if (!b.TryGetValue(name, out var other) || other != size) return false;
        return true;
    }

    /// <summary>Deletes the half-made copy. Only a folder this class named (marker in its name) is ever deleted.</summary>
    private static void RemoveOwnTemp(string temp)
    {
        try
        {
            if (Path.GetFileName(temp).Contains(TempMarker, StringComparison.Ordinal) && Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
