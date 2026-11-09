using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AutomationPortal.Core;

namespace AutomationPortal.Actions.Content.Utilities;

internal static class Separators
{
    public static readonly Choice[] Choices =
    {
        new("newLines", "New Lines"), new("spaces", "Spaces"), new("commas", "Commas"), new("nothing", "Nothing"), new("custom", "Custom…"),
    };

    public static readonly Choice[] SplitChoices =
    {
        new("newLines", "New Lines"), new("spaces", "Spaces"), new("commas", "Commas"), new("tabs", "Tabs"),
        new("everyCharacter", "Every Character"), new("custom", "Custom…"),
    };

    public static string Resolve(string id, string custom) => id switch
    {
        "newLines" => "\n",
        "spaces" => " ",
        "commas" => ", ",
        "tabs" => "\t",
        "nothing" => "",
        "custom" => Unescape(custom),
        _ => "\n",
    };

    /// <summary>Lets people type \n and \t in a "custom" box.</summary>
    public static string Unescape(string s) => s.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\r", "\r");
}

internal static class Patterns
{
    public static Regex Create(string pattern, bool matchCase, bool multiline = true)
    {
        if (string.IsNullOrEmpty(pattern)) throw new ActionException("Enter the pattern to look for.");
        var options = RegexOptions.CultureInvariant;
        if (!matchCase) options |= RegexOptions.IgnoreCase;
        if (multiline) options |= RegexOptions.Multiline;
        try { return new Regex(pattern, options, TimeSpan.FromSeconds(2)); }
        catch (ArgumentException) { throw new ActionException($"The pattern '{ContentFormat.Truncate(pattern, 60)}' isn't valid. Check it and try again."); }
    }

    public static T Guard<T>(Func<T> work)
    {
        try { return work(); }
        catch (RegexMatchTimeoutException) { throw new ActionException("The pattern took too long to check. Try a simpler one."); }
    }
}

/// <summary>Compares text the way people sort file names: "item 2" before "item 10", ignoring case.</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        int i = 0, j = 0;
        var ci = CultureInfo.CurrentCulture.CompareInfo;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var si = i; var sj = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;
                var a = x[si..i].TrimStart('0'); var b = y[sj..j].TrimStart('0');
                if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
                var c = string.CompareOrdinal(a, b);
                if (c != 0) return c;
            }
            else
            {
                var si = i; var sj = j;
                while (i < x.Length && !char.IsDigit(x[i])) i++;
                while (j < y.Length && !char.IsDigit(y[j])) j++;
                var c = ci.Compare(x.AsSpan(si, i - si), y.AsSpan(sj, j - sj), CompareOptions.IgnoreCase);
                if (c != 0) return c;
            }
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}

internal static class ActionHelpers
{
    /// <summary>A date from a parameter: date items, ISO text, or phrases like "tomorrow 3pm". Null when empty.</summary>
    public static async Task<DateTimeOffset?> GetFlexibleDateAsync(ActionContext ctx, string key)
    {
        var content = await ctx.GetContentAsync(key).ConfigureAwait(false);
        return ToDate(content);
    }

    public static DateTimeOffset? ToDate(ContentValue content)
    {
        if (content.IsEmpty) return null;
        if (content.AsDate() is { } d) return d;
        return DateParser.TryParse(content.AsText(), DateTimeOffset.Now, out var parsed) ? parsed : null;
    }

    public static IEnumerable<DateTimeOffset> ToDates(ContentValue content)
    {
        foreach (var item in content)
        {
            var one = ContentValue.From(item);
            if (ToDate(one) is { } d) yield return d;
        }
    }

    /// <summary>Applies <paramref name="map"/> to every item's text, so lists of text are handled item by item.</summary>
    public static ContentValue MapTexts(ContentValue input, Func<string, string> map) =>
        ContentValue.Texts(input.AsTexts().Select(map));

    public static string Plural(long n, string singular, string? plural = null) => n == 1 ? $"1 {singular}" : $"{n.ToString(CultureInfo.InvariantCulture)} {plural ?? singular + "s"}";

    public static ParameterDescriptor TextInput(string key = "text", string label = "Text", bool required = false) =>
        new() { Key = key, Label = label, Type = ParamType.MultilineText, Multiline = true, IsInput = true, Required = required };

    public static ParameterDescriptor UrlInput(string key = "url", string label = "Web address") =>
        new() { Key = key, Label = label, Type = ParamType.Url, IsInput = true, Required = true, Placeholder = "https://example.com" };

    public static string Describe(Uri url) => url.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? url.Host[4..] : url.Host;

    /// <summary>One web address from a Web address parameter, with friendly errors when missing or not a link.</summary>
    public static async Task<Uri> GetUrlAsync(ActionContext ctx, string key = "url")
    {
        var urls = await ctx.GetUrlsAsync(key).ConfigureAwait(false);
        if (urls.Count == 0)
        {
            var raw = await ctx.GetTextAsync(key).ConfigureAwait(false);
            throw new ActionException(string.IsNullOrWhiteSpace(raw)
                ? "There is no web address to use. Type or choose a link such as https://example.com."
                : $"'{ContentFormat.Truncate(raw.Trim(), 60)}' doesn't look like a web address. Use a link such as https://example.com.");
        }
        return urls[0];
    }

    public static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    public static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var text = Regex.Replace(html, @"<\s*br\s*/?>|</p\s*>|</div\s*>|</li\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", "");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"[ \t ]+", " ");
        text = Regex.Replace(text, @"\s*\n\s*", "\n");
        return text.Trim();
    }
}
