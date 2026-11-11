using System.Text;

namespace AutomationPortal.Cli;

/// <summary>Reads the paths for <c>ap run --files-from</c>. Only reads; never changes the list file.</summary>
internal static class FileList
{
    /// <summary>One path per line; blank lines are skipped and each line is trimmed. Relative paths are resolved from <paramref name="baseFolder"/>.</summary>
    internal static List<string> Parse(string text, string baseFolder)
    {
        var paths = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().Trim('﻿').Trim();
            if (line.Length == 0) continue;
            paths.Add(Path.GetFullPath(line, baseFolder));
        }
        return paths;
    }

    /// <summary>Reads a list file, or standard input when <paramref name="source"/> is "-". Returns null and an error message when the file can't be read.</summary>
    internal static async Task<(List<string>? Paths, string? Error)> ReadAsync(string source, TextReader stdin, string baseFolder)
    {
        try
        {
            string text;
            if (source == "-") text = await stdin.ReadToEndAsync();
            else
            {
                var path = Path.GetFullPath(source, baseFolder);
                if (!File.Exists(path)) return (null, $"The list file \"{source}\" doesn't exist.");
                text = await File.ReadAllTextAsync(path, Encoding.UTF8);
            }
            return (Parse(text, baseFolder), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (null, $"Couldn't read the list file \"{source}\". {ex.Message}");
        }
    }
}
