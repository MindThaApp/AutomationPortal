namespace AutomationPortal.Core;

/// <summary>What a launch of AutomationPortal.exe (first or second) asks for.</summary>
public sealed class LaunchCommand
{
    public bool Background { get; init; }
    /// <summary>Shortcut id or name for <c>--run</c> / <c>automationportal://run</c>.</summary>
    public string? RunTarget { get; init; }
    public string? Input { get; init; }
    public IReadOnlyList<string> Files { get; init; } = Array.Empty<string>();
    /// <summary>A <c>.autoportal</c> file to import (<c>--import</c>, <c>automationportal://import?file=</c>, or a bare path).</summary>
    public string? ImportFile { get; init; }
    /// <summary>True when the request came from an automationportal:// link (a web page or another app), so it must be allowed first.</summary>
    public bool FromLink { get; init; }
    /// <summary>Files and folders from a <c>automationportal://pick</c> link: the user chooses which shortcut runs on them. Never runs by itself.</summary>
    public IReadOnlyList<string> PickFiles { get; init; } = Array.Empty<string>();
    /// <summary>Files from a <c>automationportal://new</c> link: a new, unsaved shortcut opens in the editor with them to try it on. Never runs anything.</summary>
    public IReadOnlyList<string> NewShortcutFiles { get; init; } = Array.Empty<string>();
    public bool Unregister { get; init; }
    /// <summary>A bare <c>automationportal:</c> link or <c>automationportal://open</c>: show the main window. Runs nothing, so nothing is asked first.</summary>
    public bool Open { get; init; }
    /// <summary>The page from <c>automationportal://open?page=</c> (one of <see cref="LaunchArguments.OpenPages"/>), or null for the window as it is.</summary>
    public string? OpenPage { get; init; }
    /// <summary>Plain-English problem with the arguments, or null.</summary>
    public string? Error { get; init; }

    public bool IsEmpty => RunTarget is null && ImportFile is null && PickFiles.Count == 0 && NewShortcutFiles.Count == 0 && !Unregister && !Open;
}

/// <summary>Parses the command line shared by AutomationPortal.exe, Explorer verbs, Send To, links and the file association.</summary>
public static class LaunchArguments
{
    public const string LinkScheme = "automationportal";

    /// <summary>The switch the link handler is registered with: <c>"AutomationPortal.exe" --link "%1"</c>.</summary>
    public const string LinkSwitch = "--link";

    /// <summary>True when the text is an automationportal: link (the scheme and a colon, any case, with or without slashes).</summary>
    public static bool IsLink(string? arg) =>
        arg is not null && arg.StartsWith(LinkScheme + ":", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when Windows started Automation Portal for an automationportal: link: any argument is an automationportal: link, or the link switch is present.
    /// Windows hands the whole link over unescaped, so a quote inside it can split it into several arguments in any position.
    /// </summary>
    public static bool IsLinkLaunch(IReadOnlyList<string> args) =>
        args.Any(IsLink) || args.Any(a => a.Equals(LinkSwitch, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The arguments Automation Portal may act on. For a link launch that is the first automationportal: link alone (nothing at all when the
    /// link switch has no link): anything else came from whoever built the link, so options such as --run, --files, --data-root
    /// or a bare .autoportal path are ignored.
    /// </summary>
    public static IReadOnlyList<string> Trusted(IReadOnlyList<string> args)
    {
        if (!IsLinkLaunch(args)) return args;
        var link = args.FirstOrDefault(IsLink);
        return link is null ? Array.Empty<string>() : new[] { link };
    }

    public static LaunchCommand Parse(IReadOnlyList<string> args)
    {
        args = Trusted(args);
        var background = false;
        var unregister = false;
        string? run = null, input = null, import = null, error = null;
        var files = new List<string>();
        var fromLink = false;
        var open = false;
        string? page = null;
        IReadOnlyList<string> pick = Array.Empty<string>();
        IReadOnlyList<string> newFiles = Array.Empty<string>();

        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            switch (a.ToLowerInvariant())
            {
                case "--background": background = true; continue;
                case "--unregister": unregister = true; continue;
                case "--run":
                    if (i + 1 < args.Count) run = args[++i]; else error = Strings.Get("LaunchArguments_RunNeedsTheName");
                    continue;
                case "--input":
                    if (i + 1 < args.Count) input = args[++i]; else error = Strings.Get("LaunchArguments_InputNeedsSomeText");
                    continue;
                case "--import":
                    if (i + 1 < args.Count) import = args[++i]; else error = Strings.Get("LaunchArguments_ImportNeedsAnAutoportal");
                    continue;
                case "--data-root": case "--show": case "--log": case "--shell-sandbox":
                    i++;
                    continue;
                case "--files": case "--file":
                    while (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) files.Add(args[++i]);
                    continue;
            }

            if (IsLink(a))
            {
                fromLink = true;
                if (OpenLinkPage(a) is { } openPage) { open = true; page = openPage.Length == 0 ? null : openPage; }
                else if (NewLinkQuery(a) is { } newQuery)
                {
                    var (paths, problem) = ParseNewShortcutPaths(newQuery);
                    if (problem is not null) error = problem; else newFiles = paths;
                }
                else ParseLink(a, ref run, ref input, ref import, ref pick, ref error);
            }
            else if (!a.StartsWith('-') && ShortcutPackage.HasPackageExtension(a) && import is null)
                import = a;
        }

        return new LaunchCommand
        {
            Background = background, Unregister = unregister, RunTarget = run, Input = input, ImportFile = import,
            Files = files, PickFiles = pick, NewShortcutFiles = newFiles,
            FromLink = fromLink && (run is not null || import is not null || pick.Count > 0), Error = error,
            Open = open, OpenPage = page,
        };
    }

    /// <summary>The pages <c>automationportal://open?page=</c> can show.</summary>
    public static IReadOnlyList<string> OpenPages { get; } = new[] { "library", "pinned", "automations", "snippets", "gallery", "history", "settings" };

    /// <summary>
    /// For a bare <c>automationportal:</c> link (slashes allowed) or <c>automationportal://open</c>: the page to show, "" for none or an
    /// unknown page. Null for any other link.
    /// </summary>
    private static string? OpenLinkPage(string link)
    {
        var rest = link[(LinkScheme.Length + 1)..].Trim();
        if (rest.Trim('/').Length == 0) return "";
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || !uri.Host.Equals("open", StringComparison.OrdinalIgnoreCase)) return null;
        if (uri.AbsolutePath.Trim('/').Length > 0) return null;
        var page = ParseQuery(uri.Query).GetValueOrDefault("page")?.Trim().ToLowerInvariant();
        return page is not null && OpenPages.Contains(page) ? page : "";
    }

    private static void ParseLink(string link, ref string? run, ref string? input, ref string? import, ref IReadOnlyList<string> pick, ref string? error)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri)) { error = Strings.Get("LaunchArguments_ThatLinkIsnT"); return; }
        var query = ParseQuery(uri.Query);
        var verb = uri.Host.ToLowerInvariant();
        if (verb is "run")
        {
            run = query.GetValueOrDefault("name") ?? query.GetValueOrDefault("id");
            input = query.GetValueOrDefault("input");
            if (string.IsNullOrWhiteSpace(run)) { run = null; error = Strings.Get("LaunchArguments_ThatLinkDoesnT"); }
        }
        else if (verb is "import")
        {
            import = query.GetValueOrDefault("file");
            if (string.IsNullOrWhiteSpace(import)) { import = null; error = Strings.Get("LaunchArguments_ThatLinkDoesnT2"); }
            else if (LinkImportProblem(import) is { } problem) { import = null; error = problem; }
        }
        else if (verb is "pick")
        {
            var (paths, problem) = ParsePickPaths(uri.Query);
            if (problem is not null) error = problem; else pick = paths;
        }
        else error = $"Automation Portal doesn't know what \"{uri.Host}\" means in a link. Links look like automationportal://run?name=My%20Shortcut.";
    }

    /// <summary>
    /// Why an automationportal://import link's file can't be used, or null. Links may only name an .autoportal file on a local drive of this PC:
    /// opening a network path (\\server\share, \\?\UNC, //server, a mapped network drive) would make Windows sign in to that server
    /// before Automation Portal could ask anything. Checks the text only; nothing is opened.
    /// </summary>
    public static string? LinkImportProblem(string path)
    {
        const string network = "Automation Portal only adds shortcuts from links when the file is on this PC, not on a network or another computer. Download the file first, then open it.";
        var p = path.Trim().Trim('"').Replace('/', '\\');
        if (p.StartsWith(@"\\", StringComparison.Ordinal)) return network;
        if (!(p.Length > 3 && char.IsAsciiLetter(p[0]) && p[1] == ':' && p[2] == '\\') || p.IndexOf(':', 2) >= 0)
            return "That link doesn't point to an .autoportal file on this PC.";
        if (!ShortcutPackage.HasPackageExtension(p)) return "That link doesn't point to an .autoportal file.";
        try { if (new DriveInfo(p[..3]).DriveType == DriveType.Network) return network; }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { return network; }
        return null;
    }

    public const int MaxPickPaths = 100;

    /// <summary>
    /// The files and folders of an automationportal://pick link (repeated <c>path</c> keys). Only absolute paths on a local drive (X:\...) are accepted;
    /// paths that don't exist are dropped. Checks text, then asks the file system only whether each path exists.
    /// </summary>
    public static (IReadOnlyList<string> Paths, string? Error) ParsePickPaths(string query)
    {
        var raw = ParseQueryAll(query).Where(kv => kv.Key.Equals("path", StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Value).ToList();
        if (raw.Count == 0) return (Array.Empty<string>(), "That link doesn't say which files or folders to use.");
        if (raw.Count > MaxPickPaths) return (Array.Empty<string>(), $"That link names more than {MaxPickPaths} files and folders, which is too many to use at once.");
        var result = new List<string>();
        foreach (var path in raw)
        {
            if (PickPathProblem(path) is { } problem) return (Array.Empty<string>(), problem);
            var p = path.Replace('/', '\\');
            if ((File.Exists(p) || Directory.Exists(p)) && !result.Contains(p, StringComparer.OrdinalIgnoreCase)) result.Add(p);
        }
        return result.Count == 0 ? (Array.Empty<string>(), "None of the files or folders in that link exist on this PC.") : (result, null);
    }

    /// <summary>Why a path from a pick link is refused, or null when it is a well-formed absolute local path.</summary>
    public static string? PickPathProblem(string path)
    {
        if (path.Any(char.IsControl)) return "That link contains a file name Automation Portal can't use.";
        var p = path.Replace('/', '\\');
        if (p.StartsWith(@"\\", StringComparison.Ordinal)) return "Automation Portal only takes files and folders on this PC from links, not network paths.";
        if (!(p.Length >= 3 && char.IsAsciiLetter(p[0]) && p[1] == ':' && p[2] == '\\') || p.IndexOf(':', 2) >= 0)
            return @"That link has a file or folder that isn't a full path on this PC (like C:\Folder\File.txt).";
        if (p.Split('\\').Any(seg => seg == "..")) return "That link has a path with \"..\" in it, which Automation Portal doesn't accept.";
        if (p.IndexOfAny(new[] { '<', '>', '"', '|', '?', '*' }) >= 0) return "That link contains a file name Automation Portal can't use.";
        return null;
    }

    public const int MaxNewShortcutLinkPaths = 100;

    /// <summary>The query of an <c>automationportal://new</c> link ("" when it has none), or null for any other link.</summary>
    private static string? NewLinkQuery(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || !uri.Host.Equals("new", StringComparison.OrdinalIgnoreCase)) return null;
        return uri.AbsolutePath.Trim('/').Length > 0 ? null : uri.Query;
    }

    /// <summary>
    /// The files of an automationportal://new link (repeated <c>path</c> keys, at most <see cref="MaxNewShortcutLinkPaths"/>). Each must pass
    /// <see cref="NewShortcutPathProblem"/>. Text only: nothing is opened or checked on disk here. Repeats are dropped.
    /// </summary>
    public static (IReadOnlyList<string> Paths, string? Error) ParseNewShortcutPaths(string query)
    {
        var raw = ParseQueryAll(query).Where(kv => kv.Key.Equals("path", StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Value).ToList();
        if (raw.Count == 0) return (Array.Empty<string>(), "That link doesn't say which files the new shortcut is for.");
        if (raw.Count > MaxNewShortcutLinkPaths) return (Array.Empty<string>(), $"That link names more than {MaxNewShortcutLinkPaths} files, which is too many to use at once.");
        return CheckNewShortcutPaths(raw);
    }

    /// <summary>Validates and de-duplicates the files for a new shortcut (link, pipe or ap new). The first bad path is the error.</summary>
    public static (IReadOnlyList<string> Paths, string? Error) CheckNewShortcutPaths(IEnumerable<string> paths)
    {
        var result = new List<string>();
        foreach (var path in paths)
        {
            if (NewShortcutPathProblem(path) is { } problem) return (Array.Empty<string>(), problem);
            var p = path.Replace('/', '\\');
            if (!result.Contains(p, StringComparer.OrdinalIgnoreCase)) result.Add(p);
        }
        return result.Count == 0 ? (Array.Empty<string>(), "No files were given for the new shortcut.") : (result, null);
    }

    private static readonly string[] DeviceNames =
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Why a path can't be a file for a new shortcut, or null. Accepted: a full path on a drive (<c>C:\Folder\a.txt</c>) or a network path
    /// (<c>\\server\share\a.txt</c>). Refused: relative paths, <c>\\?\</c> and <c>\\.\</c> paths, device names (CON, NUL, COM1…),
    /// <c>..</c>, alternate streams and characters Windows doesn't allow in names. Checks text only.
    /// </summary>
    public static string? NewShortcutPathProblem(string path)
    {
        const string notFull = @"isn't a full path (like C:\Folder\File.txt or \\server\share\File.txt).";
        if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl)) return "One of the files has a name Automation Portal can't use.";
        var p = path.Replace('/', '\\');
        string rest;
        if (p.StartsWith(@"\\", StringComparison.Ordinal))
        {
            if (p.StartsWith(@"\\?\", StringComparison.Ordinal) || p.StartsWith(@"\\.\", StringComparison.Ordinal))
                return $"\"{path}\" is a device path, which Automation Portal doesn't accept.";
            var parts = p[2..].Split('\\');
            if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0 || parts[0] is "." or "?") return $"\"{path}\" {notFull}";
            rest = p[2..];
        }
        else if (p.Length >= 3 && char.IsAsciiLetter(p[0]) && p[1] == ':' && p[2] == '\\') rest = p[3..];
        else return $"\"{path}\" {notFull}";

        if (rest.Contains(':')) return $"\"{path}\" has a \":\" in it, which Automation Portal doesn't accept.";
        if (rest.IndexOfAny(new[] { '<', '>', '"', '|', '?', '*' }) >= 0) return $"\"{path}\" has a character Windows doesn't allow in file names.";
        var segments = rest.Split('\\');
        if (segments.Any(s => s is ".." or ".")) return $"\"{path}\" has \".\" or \"..\" in it, which Automation Portal doesn't accept.";
        foreach (var s in segments)
        {
            var stem = s.Split('.')[0].TrimEnd(' ');
            if (DeviceNames.Contains(stem, StringComparer.OrdinalIgnoreCase)) return $"\"{path}\" names a device ({stem}), not a file.";
        }
        return null;
    }

    /// <summary>
    /// The files that aren't there. Network paths are not looked at (that could make Windows sign in to the server), so they never
    /// count as missing. Nothing is opened.
    /// </summary>
    public static IReadOnlyList<string> MissingNewShortcutFiles(IEnumerable<string> paths) =>
        paths.Where(p => !p.StartsWith(@"\\", StringComparison.Ordinal) && !File.Exists(p) && !Directory.Exists(p)).ToList();

    /// <summary>Like <see cref="ParseQuery"/> but keeps repeated keys, in order.</summary>
    public static List<KeyValuePair<string, string>> ParseQueryAll(string query)
    {
        var result = new List<KeyValuePair<string, string>>();
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            result.Add(new(Decode(eq < 0 ? part : part[..eq]), eq < 0 ? "" : Decode(part[(eq + 1)..])));
        }
        return result;
    }

    /// <summary>
    /// True when text passed in a link could be read as a file or folder path by steps that take files, so the run asks first.
    /// Errs on the side of asking: a drive ("C:"), any slash or backslash (relative paths, UNC, "..\"), "%VAR%", "~", or a
    /// leading dot all count.
    /// </summary>
    public static bool InputLooksLikePath(string? input) =>
        !string.IsNullOrWhiteSpace(input) && input.Split('\n').Any(line =>
        {
            var t = line.Trim().Trim('"', '\'').Trim();
            if (t.Length == 0) return false;
            return t.IndexOfAny(new[] { '%', '~', '\\', '/' }) >= 0
                || t.StartsWith('.')
                || (t.Length >= 2 && t[1] == ':' && char.IsLetter(t[0]));
        });

    public static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var key = Decode(eq < 0 ? part : part[..eq]);
            result[key] = eq < 0 ? "" : Decode(part[(eq + 1)..]);
        }
        return result;
    }

    private static string Decode(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));
}
