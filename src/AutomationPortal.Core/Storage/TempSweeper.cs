namespace AutomationPortal.Core;

/// <summary>Deletes leftover temp files (failed cleanups, clipboard images, trigger samples). Call once at startup.</summary>
public static class TempSweeper
{
    public static int Sweep(AutomationPortalPaths paths, TimeSpan? olderThan = null)
    {
        var cutoff = DateTime.UtcNow - (olderThan ?? TimeSpan.FromDays(1));
        var removed = 0;
        try
        {
            if (!Directory.Exists(paths.TempRoot)) return 0;
            foreach (var entry in Directory.EnumerateFileSystemEntries(paths.TempRoot))
            {
                if (RunScope.ActiveTempFolders.ContainsKey(entry)) continue;
                try
                {
                    if (Directory.Exists(entry))
                    {
                        // "clipboard" and "trigger-samples" are long-lived folders: sweep their old files, keep the folder.
                        if (IsSharedFolder(entry)) removed += SweepFiles(entry, cutoff);
                        else if (Directory.GetLastWriteTimeUtc(entry) < cutoff && !HasNewer(entry, cutoff)) { Directory.Delete(entry, true); removed++; }
                    }
                    else if (File.GetLastWriteTimeUtc(entry) < cutoff) { File.Delete(entry); removed++; }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return removed;
    }

    private static bool IsSharedFolder(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals("clipboard", StringComparison.OrdinalIgnoreCase) || name.Equals("trigger-samples", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasNewer(string dir, DateTime cutoff) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any(f => File.GetLastWriteTimeUtc(f) >= cutoff);

    private static int SweepFiles(string dir, DateTime cutoff)
    {
        var n = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            try { if (File.GetLastWriteTimeUtc(f) < cutoff) { File.Delete(f); n++; } }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return n;
    }
}
