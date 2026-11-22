namespace AutomationPortal.Actions.Documents;

/// <summary>
/// Writes a result file that must be new. The file is opened with <see cref="FileMode.CreateNew"/>, so nothing already there is
/// ever opened or replaced: when something appeared at the chosen name since it was picked, the next free "name (2).ext",
/// "name (3).ext" … is used instead. Returns the path actually written. If writing fails, only the file made here is removed.
/// </summary>
public static class NewFile
{
    private const int MaxTries = 1000;

    public static string Write(string path, Action<Stream> write)
    {
        var (stream, actual) = Open(path);
        try
        {
            using (stream) write(stream);
        }
        catch
        {
            TryRemoveOwn(actual);
            throw;
        }
        return actual;
    }

    public static async Task<string> WriteAsync(string path, Func<Stream, Task> write)
    {
        var (stream, actual) = Open(path);
        try
        {
            await using (stream) await write(stream).ConfigureAwait(false);
        }
        catch
        {
            TryRemoveOwn(actual);
            throw;
        }
        return actual;
    }

    /// <summary>Creates a new, empty file at <paramref name="path"/> or the next free "name (n)" next to it, and returns it open for writing.</summary>
    internal static (FileStream Stream, string Path) Open(string path)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full)!;
        var stem = Path.GetFileNameWithoutExtension(full);
        var ext = Path.GetExtension(full);
        var candidate = full;
        for (var n = 2; ; n++)
        {
            if (!Directory.Exists(candidate))
            {
                TestRootGuard.Check(candidate);
                try { return (new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None), candidate); }
                catch (IOException) when (File.Exists(candidate) || Directory.Exists(candidate)) { }
            }
            if (n > MaxTries) throw new IOException($"Couldn't find a free name for '{Path.GetFileName(full)}'.");
            candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
        }
    }

    // Only ever called with a path this class just created with CreateNew.
    private static void TryRemoveOwn(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
