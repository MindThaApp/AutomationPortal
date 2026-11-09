namespace Flowmate.App.Logic;

/// <summary>Small, forgiving text matcher for Quick Run and search boxes. Higher score = better; null = no match.</summary>
public static class FuzzyMatcher
{
    public static int? Score(string query, string? text)
    {
        query = query.Trim();
        if (query.Length == 0) return 0;
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();

        var cmp = StringComparison.CurrentCultureIgnoreCase;
        if (text.Equals(query, cmp)) return 1000;
        if (text.StartsWith(query, cmp)) return 900 - Math.Min(text.Length - query.Length, 80) / 4;

        var wordStart = WordStartIndex(text, query);
        if (wordStart >= 0) return 800 - Math.Min(wordStart, 60);

        var idx = text.IndexOf(query, cmp);
        if (idx >= 0) return 700 - Math.Min(idx, 60);

        if (query.Length < 2) return null;
        return Subsequence(query, text);
    }

    /// <summary>Best of name and alias. An exact alias hit beats everything ("gm" runs Good Morning).</summary>
    public static int? ScoreNameAndAlias(string query, string? name, string? alias)
    {
        query = query.Trim();
        if (query.Length == 0) return 0;
        int? best = Score(query, name);
        if (!string.IsNullOrWhiteSpace(alias))
        {
            if (alias.Trim().Equals(query, StringComparison.CurrentCultureIgnoreCase)) return 1100;
            if (alias.Trim().StartsWith(query, StringComparison.CurrentCultureIgnoreCase)) best = Math.Max(best ?? 0, 950);
        }
        return best;
    }

    private static int WordStartIndex(string text, string query)
    {
        var cmp = StringComparison.CurrentCultureIgnoreCase;
        for (var i = 1; i < text.Length; i++)
        {
            if (!char.IsLetterOrDigit(text[i - 1]) && char.IsLetterOrDigit(text[i]) && string.Compare(text, i, query, 0, query.Length, cmp) == 0)
                return i;
        }
        return -1;
    }

    private static int? Subsequence(string query, string text)
    {
        var cmp = StringComparison.CurrentCultureIgnoreCase;
        var pos = 0;
        var gaps = 0;
        var boundary = 0;
        var last = -1;
        foreach (var ch in query)
        {
            if (char.IsWhiteSpace(ch)) continue;
            var found = text.IndexOf(ch.ToString(), pos, cmp);
            if (found < 0) return null;
            if (last >= 0 && found > last + 1) gaps += found - last - 1;
            if (found == 0 || !char.IsLetterOrDigit(text[found - 1])) boundary++;
            last = found;
            pos = found + 1;
        }
        return Math.Max(1, 500 - gaps * 8 + boundary * 15 - Math.Min(text.Length, 100) / 5);
    }
}
