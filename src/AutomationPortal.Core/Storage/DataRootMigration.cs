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

    private static EnumerationOptions Options => new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,   // links are not followed or copied
        IgnoreInaccessible = false,
    };

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.EnumerateDirectories(from, "*", Options))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.EnumerateFiles(from, "*", Options))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: false);
    }

    private static Dictionary<string, long> Measure(string root)
    {
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(root, "*", Options))
            map[Path.GetRelativePath(root, file)] = new FileInfo(file).Length;
        return map;
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
