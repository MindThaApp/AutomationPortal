namespace AutomationPortal.Core;

/// <summary>
/// With a custom data root (<c>--data-root</c>, AUTOMATIONPORTAL_DATA_ROOT) the user's real folders are out of reach:
/// Desktop, Documents, Downloads and the rest map to <c>&lt;root&gt;\user\&lt;Name&gt;</c>. Without a root every
/// <c>Map</c> returns null and callers use the real folder as before.
/// </summary>
public static class UserFolders
{
    private static string? _root;
    private static readonly AsyncLocal<string?> _scoped = new();
    private static string? Current => _scoped.Value ?? _root;

    /// <summary>Uses <paramref name="root"/> for the current async flow only, until the result is disposed. For tests, so parallel tests are not affected.</summary>
    public static IDisposable Scope(string root)
    {
        var previous = _scoped.Value;
        _scoped.Value = Path.GetFullPath(root).TrimEnd('\\', '/');
        return new ScopeEnd(previous);
    }

    private sealed class ScopeEnd(string? previous) : IDisposable
    {
        public void Dispose() => _scoped.Value = previous;
    }

    /// <summary>The custom data root, full path, or null on a normal launch.</summary>
    public static string? Root => Current;
    public static bool IsSandboxed => Current is not null;

    public static void UseRoot(string? root) =>
        _root = string.IsNullOrWhiteSpace(root) ? null : Path.GetFullPath(root).TrimEnd('\\', '/');

    /// <summary>desktop, documents, downloads, pictures, screenshots, music, videos, home, onedrive, roaming, local.</summary>
    public static string? Map(string name)
    {
        if (Current is not { } root) return null;
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
        var path = Path.Combine(root, "user", folder);
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

    /// <summary>The system temp folder, or <c>&lt;root&gt;\temp</c> under a custom root.</summary>
    public static string TempPath()
    {
        if (Current is not { } root) return Path.GetTempPath();
        var path = Path.Combine(root, "temp");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>True when there is no custom root, or <paramref name="path"/> is inside it.</summary>
    public static bool IsInsideRoot(string path)
    {
        if (Current is not { } root) return true;
        if (string.IsNullOrWhiteSpace(path)) return false;
        string full;
        try { full = Path.GetFullPath(path).TrimEnd('\\', '/'); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
        return IsInside(root, full);
    }

    /// <summary>
    /// True when <paramref name="full"/> (a full path) is <paramref name="root"/> or below it, and no existing folder or file
    /// below the root on the way is a junction or symbolic link (those could point outside the root).
    /// </summary>
    public static bool IsInside(string root, string full)
    {
        root = root.TrimEnd('\\', '/');
        full = full.TrimEnd('\\', '/');
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal) || full.StartsWith(@"\\.\", StringComparison.Ordinal)) return false;
        if (full.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
        var current = root;
        foreach (var part in full[(root.Length + 1)..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            try
            {
                var attrs = File.GetAttributes(current);
                if ((attrs & FileAttributes.ReparsePoint) != 0) return false;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { break; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        }
        return true;
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

    /// <summary>Under a test data root, actions that change real system state (wallpaper) or start programs and open files are refused.</summary>
    public static void BlockSystemChange(string what)
    {
        if (UserFolders.IsSandboxed)
            throw new ActionException($"Blocked: {what} is turned off while a test data root is in use, because it would change the real PC.");
    }

    public static void Check(params string?[] paths)
    {
        foreach (var p in paths) Check(p);
    }
}

