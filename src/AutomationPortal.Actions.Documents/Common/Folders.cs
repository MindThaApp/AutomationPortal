using System.Runtime.InteropServices;

namespace AutomationPortal.Actions.Documents;

/// <summary>Well-known user folders, including Downloads (which Environment.SpecialFolder doesn't know about).</summary>
public static class Folders
{
    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    public static string Downloads()
    {
        if (UserFolders.Map("downloads") is { } sandboxed) return sandboxed;
        try
        {
            if (SHGetKnownFolderPath(DownloadsId, 0, IntPtr.Zero, out var p) == 0)
            {
                try { return Marshal.PtrToStringUni(p) ?? Fallback(); }
                finally { Marshal.FreeCoTaskMem(p); }
            }
        }
        catch { /* fall through */ }
        return Fallback();

        static string Fallback() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    public static string OneDrive()
    {
        if (UserFolders.Map("onedrive") is { } sandboxed) return sandboxed;
        var env = Environment.GetEnvironmentVariable("OneDrive") ?? Environment.GetEnvironmentVariable("OneDriveConsumer");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env)) return env;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var guess = Path.Combine(home, "OneDrive");
        if (Directory.Exists(guess)) return guess;
        throw new ActionException("OneDrive isn't set up on this PC. Choose another folder.");
    }

    public static bool IsKnownId(string id) =>
        id.ToLowerInvariant() is "desktop" or "documents" or "downloads" or "pictures" or "music" or "videos" or "home" or "onedrive";

    /// <summary>Resolves a folder id used by Get Folder: desktop, documents, downloads, pictures, music, videos, home, onedrive.</summary>
    public static string Resolve(string id) => id.ToLowerInvariant() switch
    {
        "desktop" => UserFolders.Desktop(),
        "documents" => UserFolders.Documents(),
        "downloads" => Downloads(),
        "pictures" => UserFolders.Pictures(),
        "music" => UserFolders.Music(),
        "videos" => UserFolders.Videos(),
        "home" => UserFolders.Home(),
        "onedrive" => OneDrive(),
        _ => throw new ActionException($"Don't know the folder '{id}'. Pick one from the list."),
    };

    public static string NotesFolder() =>
        ChooseNotesFolder(UserFolders.Documents(), Directory.Exists);

    /// <summary>
    /// Documents\Automation Portal Notes, unless only the folder used before the rename (Documents\Flowmate Notes) exists:
    /// then that one keeps being used. Notes are never copied or moved (Documents can be on another drive).
    /// </summary>
    internal static string ChooseNotesFolder(string documents, Func<string, bool> exists)
    {
        var current = Path.Combine(documents, "Automation Portal Notes");
        var previous = Path.Combine(documents, "Flowmate Notes");
        return !exists(current) && exists(previous) ? previous : current;
    }
}
