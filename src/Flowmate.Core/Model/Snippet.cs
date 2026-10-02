using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Flowmate.Core;

/// <summary>A text-expansion snippet: typing <see cref="Abbreviation"/> is replaced by <see cref="Text"/>.</summary>
public sealed class Snippet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Abbreviation { get; set; } = "";
    /// <summary>May contain {date}, {time}, {date:format}, {clipboard}, {cursor}, {input:Label}.</summary>
    public string Text { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>False: expansion follows the typed case (typing ";Hi" expands capitalised, ";HI" upper-case).</summary>
    public bool MatchCase { get; set; }
    public string? Folder { get; set; }

    public Snippet Clone() => FlowmateJson.Clone(this);
}

/// <summary>Pure placeholder expansion for snippets.</summary>
public static class SnippetExpander
{
    private static readonly Regex Placeholder = new(@"\{(date|time|clipboard|cursor|input)(?::([^}]*))?\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Labels of the {input:Label} fill-ins in order of appearance (distinct).</summary>
    public static IReadOnlyList<string> FillInLabels(string text) =>
        Placeholder.Matches(text).Where(m => m.Groups[1].Value.Equals("input", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Groups[2].Value).Distinct().ToList();

    /// <summary>
    /// Expands placeholders. <c>CursorOffsetFromEnd</c> is how many characters the caret must move left from the end of
    /// the result to land on {cursor} (null when there is no {cursor}).
    /// </summary>
    public static (string Result, int? CursorOffsetFromEnd) Expand(string text, DateTimeOffset now, string? clipboardText, IReadOnlyDictionary<string, string>? fillIns = null)
    {
        var sb = new StringBuilder();
        var last = 0;
        int? cursorAt = null;
        foreach (Match m in Placeholder.Matches(text))
        {
            sb.Append(text, last, m.Index - last);
            last = m.Index + m.Length;
            var arg = m.Groups[2].Success ? m.Groups[2].Value : null;
            switch (m.Groups[1].Value.ToLowerInvariant())
            {
                case "date": sb.Append(Format(now, string.IsNullOrEmpty(arg) ? "d" : arg)); break;
                case "time": sb.Append(Format(now, string.IsNullOrEmpty(arg) ? "t" : arg)); break;
                case "clipboard": sb.Append(clipboardText ?? ""); break;
                case "cursor": cursorAt ??= sb.Length; break;
                case "input":
                    string? v = null;
                    fillIns?.TryGetValue(arg ?? "", out v);
                    sb.Append(v ?? "");
                    break;
            }
        }
        sb.Append(text, last, text.Length - last);
        var result = sb.ToString();
        return (result, cursorAt is { } c ? result.Length - c : null);
    }

    private static string Format(DateTimeOffset now, string format)
    {
        try { return now.ToString(format, CultureInfo.CurrentCulture); }
        catch (FormatException) { return format; }
    }

    /// <summary>Applies the typed case to the expansion when the snippet isn't case-sensitive: "Hi"→capitalised first letter, "HI"→upper-case.</summary>
    public static string ApplyTypedCase(string typedAbbreviation, string expansion)
    {
        if (expansion.Length == 0 || typedAbbreviation.Length == 0) return expansion;
        var letters = typedAbbreviation.Where(char.IsLetter).ToArray();
        if (letters.Length == 0) return expansion;
        if (letters.Length > 1 && letters.All(char.IsUpper)) return expansion.ToUpperInvariant();
        if (char.IsUpper(letters[0])) return char.ToUpperInvariant(expansion[0]) + expansion[1..];
        return expansion;
    }
}
