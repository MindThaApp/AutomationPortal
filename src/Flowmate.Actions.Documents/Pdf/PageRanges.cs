using System.Globalization;

namespace Flowmate.Actions.Documents;

/// <summary>Page lists like "1-3, 5, 8-" (1-based). An empty text means every page.</summary>
public static class PageRanges
{
    public static IReadOnlyList<IReadOnlyList<int>> ParseGroups(string? text, int pageCount)
    {
        var groups = new List<IReadOnlyList<int>>();
        if (string.IsNullOrWhiteSpace(text))
        {
            groups.Add(Enumerable.Range(1, pageCount).ToList());
            return groups;
        }
        foreach (var raw in text.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Trim().Replace("–", "-").Replace("—", "-").Replace(" ", "");
            if (part.Length == 0) continue;
            int from, to;
            var dash = part.IndexOf('-');
            if (dash < 0) { from = to = ParsePage(part, text); }
            else
            {
                var a = part[..dash];
                var b = part[(dash + 1)..];
                from = a.Length == 0 ? 1 : ParsePage(a, text);
                to = b.Length == 0 ? pageCount : ParsePage(b, text);
            }
            if (from > to) (from, to) = (to, from);
            if (from < 1 || to > pageCount)
                throw new ActionException($"The PDF has {pageCount} page{(pageCount == 1 ? "" : "s")}, but you asked for page {(from < 1 ? from : to)}. Use pages between 1 and {pageCount}.");
            groups.Add(Enumerable.Range(from, to - from + 1).ToList());
        }
        if (groups.Count == 0) throw new ActionException("Type the pages you want, like 1-3, 5.");
        return groups;
    }

    public static IReadOnlyList<int> Parse(string? text, int pageCount) =>
        ParseGroups(text, pageCount).SelectMany(g => g).ToList();

    private static int ParsePage(string s, string whole)
    {
        if (int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return n;
        throw new ActionException($"Couldn't understand the pages '{whole}'. Use numbers like 1-3, 5.");
    }

    public static string Describe(IReadOnlyList<int> pages) =>
        pages.Count == 1 ? $"page {pages[0]}" : $"pages {pages[0]}-{pages[^1]}";
}
