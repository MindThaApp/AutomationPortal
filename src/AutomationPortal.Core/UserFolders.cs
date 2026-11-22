namespace AutomationPortal.Core;

/// <summary>
/// With a custom data root (<c>--data-root</c>, AUTOMATIONPORTAL_DATA_ROOT) the user's real folders are out of reach:
/// Desktop, Documents, Downloads and the rest map to <c>&lt;root&gt;\user\&lt;Name&gt;</c>. Without a root every
/// <c>Map</c> returns null and callers use the real folder as before.
/// </summary>
public static class UserFolders
{
    private static string? _root;

    /// <summary>The custom data root, full path, or null on a normal launch.</summary>
    public static string? Root => _root;
    public static bool IsSandboxed => _root is not null;

    public static void UseRoot(string? root) =>
        _root = string.IsNullOrWhiteSpace(root) ? null : Path.GetFullPath(root).TrimEnd('\\', '/');

    /// <summary>desktop, documents, downloads, pictures, screenshots, music, videos, home, onedrive, roaming, local.</summary>
    public static string? Map(string name)
    {
        if (_root is null) return null;
        var key = name.Trim().ToLowerInvariant();
        var folder = key switch
        {
            "desktop" => "Desktop",
            "documents" => "Documents",
            "downloads" => "Downloads",
            "pictures" => "Pictures",
            "screenshots" => Path.Combine("Pictures", "Screenshots"),
            "music" => "Music",
            "videos" => "Videos",
            "home" => "Home",
            "onedrive" => "OneDrive",
            "roaming" => "AppData",
            "local" => "LocalAppData",
            _ => throw new ArgumentException($"Unknown user folder '{name}'.", nameof(name)),
        };
        var path = Path.Combine(_root, "user", folder);
        Directory.CreateDirectory(path);
        return path;
    }

    public static string Desktop() => Map("desktop") ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    public static string Documents() => Map("documents") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public static string Pictures() => Map("pictures") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    public static string Music() => Map("music") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
    public static string Videos() => Map("videos") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
    public static string Home() => Map("home") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string RoamingAppData() => Map("roaming") ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public static string LocalAppData() => Map("local") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>True when there is no custom root, or <paramref name="path"/> is inside it.</summary>
    public static bool IsInsideRoot(string path)
    {
        if (_root is null) return true;
        if (string.IsNullOrWhiteSpace(path)) return false;
        string full;
        try { full = Path.GetFullPath(path).TrimEnd('\\', '/'); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal) || full.StartsWith(@"\\.\", StringComparison.Ordinal)) return false;
        return full.Equals(_root, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Fail-closed check run before an action writes, moves or deletes: with a custom data root nothing outside it may change.</summary>
public static class TestRootGuard
{
    public static void Check(string? path)
    {
        if (!UserFolders.IsSandboxed) return;
        if (path is null || !UserFolders.IsInsideRoot(path))
            throw new ActionException($"Blocked: this run uses a test data root ({UserFolders.Root}), and \"{path}\" is outside it.");
    }

    public static void Check(params string?[] paths)
    {
        foreach (var p in paths) Check(p);
    }
}
