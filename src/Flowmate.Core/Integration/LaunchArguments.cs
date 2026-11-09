namespace Flowmate.Core;

/// <summary>What a launch of Flowmate.exe (first or second) asks for.</summary>
public sealed class LaunchCommand
{
    public bool Background { get; init; }
    /// <summary>Shortcut id or name for <c>--run</c> / <c>flowmate://run</c>.</summary>
    public string? RunTarget { get; init; }
    public string? Input { get; init; }
    public IReadOnlyList<string> Files { get; init; } = Array.Empty<string>();
    /// <summary>A <c>.flowmate</c> file to import (<c>--import</c>, <c>flowmate://import?file=</c>, or a bare path).</summary>
    public string? ImportFile { get; init; }
    /// <summary>True when the request came from a flowmate:// link (a web page or another app), so it must be allowed first.</summary>
    public bool FromLink { get; init; }
    /// <summary>Files and folders from a <c>flowmate://pick</c> link: the user chooses which shortcut runs on them. Never runs by itself.</summary>
    public IReadOnlyList<string> PickFiles { get; init; } = Array.Empty<string>();
    public bool Unregister { get; init; }
    /// <summary>Plain-English problem with the arguments, or null.</summary>
    public string? Error { get; init; }

    public bool IsEmpty => RunTarget is null && ImportFile is null && PickFiles.Count == 0 && !Unregister;
}

/// <summary>Parses the command line shared by Flowmate.exe, Explorer verbs, Send To, links and the file association.</summary>
public static class LaunchArguments
{
    public const string LinkScheme = "flowmate";

    /// <summary>The switch the link handler is registered with: <c>"Flowmate.exe" --link "%1"</c>.</summary>
    public const string LinkSwitch = "--link";

    /// <summary>True when the text is a flowmate: link (the scheme and a colon, any case, with or without slashes).</summary>
    public static bool IsLink(string? arg) =>
        arg is not null && arg.StartsWith(LinkScheme + ":", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when Windows started Flow Mate for a flowmate: link: any argument is a flowmate: link, or the link switch is present.
    /// Windows hands the whole link over unescaped, so a quote inside it can split it into several arguments in any position.
    /// </summary>
    public static bool IsLinkLaunch(IReadOnlyList<string> args) =>
        args.Any(IsLink) || args.Any(a => a.Equals(LinkSwitch, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The arguments Flow Mate may act on. For a link launch that is the first flowmate: link alone (nothing at all when the
    /// link switch has no link): anything else came from whoever built the link, so options such as --run, --files, --data-root
    /// or a bare .flowmate path are ignored.
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
        IReadOnlyList<string> pick = Array.Empty<string>();

        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            switch (a.ToLowerInvariant())
            {
                case "--background": background = true; continue;
                case "--unregister": unregister = true; continue;
                case "--run":
                    if (i + 1 < args.Count) run = args[++i]; else error = "--run needs the name of a shortcut.";
                    continue;
                case "--input":
                    if (i + 1 < args.Count) input = args[++i]; else error = "--input needs some text.";
                    continue;
                case "--import":
                    if (i + 1 < args.Count) import = args[++i]; else error = "--import needs a .flowmate file.";
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
                ParseLink(a, ref run, ref input, ref import, ref pick, ref error);
            }
            else if (!a.StartsWith('-') && a.EndsWith(".flowmate", StringComparison.OrdinalIgnoreCase) && import is null)
                import = a;
        }

        return new LaunchCommand
        {
            Background = background, Unregister = unregister, RunTarget = run, Input = input, ImportFile = import,
            Files = files, PickFiles = pick, FromLink = fromLink && (run is not null || import is not null || pick.Count > 0), Error = error,
        };
    }

    private static void ParseLink(string link, ref string? run, ref string? input, ref string? import, ref IReadOnlyList<string> pick, ref string? error)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri)) { error = "That link isn't a valid Flow Mate link."; return; }
        var query = ParseQuery(uri.Query);
        var verb = uri.Host.ToLowerInvariant();
        if (verb is "run")
        {
            run = query.GetValueOrDefault("name") ?? query.GetValueOrDefault("id");
            input = query.GetValueOrDefault("input");
            if (string.IsNullOrWhiteSpace(run)) { run = null; error = "That link doesn't say which shortcut to run."; }
        }
        else if (verb is "import")
        {
            import = query.GetValueOrDefault("file");
            if (string.IsNullOrWhiteSpace(import)) { import = null; error = "That link doesn't say which file to import."; }
            else if (LinkImportProblem(import) is { } problem) { import = null; error = problem; }
        }
        else if (verb is "pick")
        {
            var (paths, problem) = ParsePickPaths(uri.Query);
            if (problem is not null) error = problem; else pick = paths;
        }
        else error = $"Flow Mate doesn't know what \"{uri.Host}\" means in a link. Links look like flowmate://run?name=My%20Shortcut.";
    }

    /// <summary>
    /// Why a flowmate://import link's file can't be used, or null. Links may only name a .flowmate file on a local drive of this PC:
    /// opening a network path (\\server\share, \\?\UNC, //server, a mapped network drive) would make Windows sign in to that server
    /// before Flow Mate could ask anything. Checks the text only; nothing is opened.
    /// </summary>
    public static string? LinkImportProblem(string path)
    {
        const string network = "Flow Mate only adds shortcuts from links when the file is on this PC, not on a network or another computer. Download the file first, then open it.";
        var p = path.Trim().Trim('"').Replace('/', '\\');
        if (p.StartsWith(@"\\", StringComparison.Ordinal)) return network;
        if (!(p.Length > 3 && char.IsAsciiLetter(p[0]) && p[1] == ':' && p[2] == '\\') || p.IndexOf(':', 2) >= 0)
            return "That link doesn't point to a .flowmate file on this PC.";
        if (!p.EndsWith(".flowmate", StringComparison.OrdinalIgnoreCase)) return "That link doesn't point to a .flowmate file.";
        try { if (new DriveInfo(p[..3]).DriveType == DriveType.Network) return network; }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { return network; }
        return null;
    }

    public const int MaxPickPaths = 100;

    /// <summary>
    /// The files and folders of a flowmate://pick link (repeated <c>path</c> keys). Only absolute paths on a local drive (X:\...) are accepted;
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
        if (path.Any(char.IsControl)) return "That link contains a file name Flow Mate can't use.";
        var p = path.Replace('/', '\\');
        if (p.StartsWith(@"\\", StringComparison.Ordinal)) return "Flow Mate only takes files and folders on this PC from links, not network paths.";
        if (!(p.Length >= 3 && char.IsAsciiLetter(p[0]) && p[1] == ':' && p[2] == '\\') || p.IndexOf(':', 2) >= 0)
            return @"That link has a file or folder that isn't a full path on this PC (like C:\Folder\File.txt).";
        if (p.Split('\\').Any(seg => seg == "..")) return "That link has a path with \"..\" in it, which Flow Mate doesn't accept.";
        if (p.IndexOfAny(new[] { '<', '>', '"', '|', '?', '*' }) >= 0) return "That link contains a file name Flow Mate can't use.";
        return null;
    }

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

    /// <summary>True when text passed in a link would be read as a file or folder path by steps that take files.</summary>
    public static bool InputLooksLikePath(string? input) =>
        !string.IsNullOrWhiteSpace(input) && input.Split('\n').Any(line =>
        {
            var t = line.Trim().Trim('"');
            return t.Length >= 3 && (t[1] == ':' || t.StartsWith(@"\\", StringComparison.Ordinal) || t.StartsWith("//", StringComparison.Ordinal));
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
