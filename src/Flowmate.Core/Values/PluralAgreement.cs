using System.Text.RegularExpressions;

namespace Flowmate.Core;

/// <summary>
/// Number and unit agreement for sentences built from templates ("Every {every} {unit}" must read "every 1 minute", not "every 1 minutes").
/// Only well-known unit words are touched, so ordinary words that happen to end in "s" are left alone.
/// </summary>
public static class PluralAgreement
{
    private static readonly Dictionary<string, string> Singulars = new(StringComparer.OrdinalIgnoreCase)
    {
        ["milliseconds"] = "millisecond", ["seconds"] = "second", ["minutes"] = "minute", ["hours"] = "hour", ["days"] = "day", ["weeks"] = "week",
        ["months"] = "month", ["years"] = "year", ["times"] = "time", ["items"] = "item", ["files"] = "file", ["folders"] = "folder",
        ["results"] = "result", ["rows"] = "row", ["lines"] = "line", ["words"] = "word", ["characters"] = "character", ["pages"] = "page",
        ["steps"] = "step", ["entries"] = "entry", ["attempts"] = "attempt", ["retries"] = "retry", ["messages"] = "message",
        ["shortcuts"] = "shortcut", ["actions"] = "action", ["pixels"] = "pixel", ["events"] = "event", ["emails"] = "email",
    };

    private static readonly Regex OneThenUnit = new(
        @"(?<![\d.,])(?<n>1)(?<sp>\s+)(?<u>" + string.Join("|", Singulars.Keys.OrderByDescending(k => k.Length)) + @")\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LeadingUnit = new(
        @"^(?<lead>\s*)(?<u>" + string.Join("|", Singulars.Keys.OrderByDescending(k => k.Length)) + @")\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>"1 minutes" becomes "1 minute" anywhere in the sentence ("Every 1 hours" → "Every 1 hour"). Other numbers are untouched.</summary>
    public static string Fix(string sentence) =>
        string.IsNullOrEmpty(sentence) ? sentence : OneThenUnit.Replace(sentence, m => m.Groups["n"].Value + m.Groups["sp"].Value + Singular(m.Groups["u"].Value));

    /// <summary>"minutes" becomes "minute" for the known unit words; anything else comes back unchanged.</summary>
    public static string Singular(string word) =>
        Singulars.TryGetValue(word, out var s) ? MatchCase(word, s) : word;

    /// <summary>The unit word at the start of <paramref name="text"/> (after spaces) made singular: " minutes before an event" → " minute before an event".</summary>
    public static string SingularLeadingUnit(string text) =>
        LeadingUnit.Replace(text, m => m.Groups["lead"].Value + Singular(m.Groups["u"].Value), 1);

    /// <summary>True when the value is exactly the number one (a number, or text that is just "1").</summary>
    public static bool IsOne(ParamValue? value) => value switch
    {
        NumberValue n => n.Value == 1,
        TextValue t => t.ToLiteralText().Trim() == "1",
        _ => false,
    };

    private static string MatchCase(string original, string singular) =>
        original.Length > 0 && char.IsUpper(original[0]) ? char.ToUpperInvariant(singular[0]) + singular[1..] : singular;
}
