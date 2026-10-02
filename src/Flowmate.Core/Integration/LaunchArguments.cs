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
    public bool Unregister { get; init; }
    /// <summary>Plain-English problem with the arguments, or null.</summary>
    public string? Error { get; init; }

    public bool IsEmpty => RunTarget is null && ImportFile is null && !Unregister;
}

/// <summary>Parses the command line shared by Flowmate.exe, Explorer verbs, Send To, links and the file association.</summary>
public static class LaunchArguments
{
    public const string LinkScheme = "flowmate";

    public static LaunchCommand Parse(IReadOnlyList<string> args)
    {
        var background = false;
        var unregister = false;
        string? run = null, input = null, import = null, error = null;
        var files = new List<string>();
        var fromLink = false;

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

            if (a.StartsWith(LinkScheme + "://", StringComparison.OrdinalIgnoreCase))
            {
                fromLink = true;
                ParseLink(a, ref run, ref input, ref import, ref error);
            }
            else if (!a.StartsWith('-') && a.EndsWith(".flowmate", StringComparison.OrdinalIgnoreCase) && import is null)
                import = a;
        }

        return new LaunchCommand
        {
            Background = background, Unregister = unregister, RunTarget = run, Input = input, ImportFile = import,
            Files = files, FromLink = fromLink && (run is not null || import is not null), Error = error,
        };
    }

    private static void ParseLink(string link, ref string? run, ref string? input, ref string? import, ref string? error)
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
        }
        else error = $"Flow Mate doesn't know what \"{uri.Host}\" means in a link. Links look like flowmate://run?name=My%20Shortcut.";
    }

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
