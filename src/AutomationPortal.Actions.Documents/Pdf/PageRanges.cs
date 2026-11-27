using System.Globalization;

namespace AutomationPortal.Actions.Documents;

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
                throw new ActionException(Strings.Format("PageRanges_Error_ThePDFHasPage", pageCount, (pageCount == 1 ? "" : "s"), (from < 1 ? from : to)));
            groups.Add(Enumerable.Range(from, to - from + 1).ToList());
        }
        if (groups.Count == 0) throw new ActionException(Strings.Get("PageRanges_Error_TypeThePagesYou"));
        return groups;
    }

    public static IReadOnlyList<int> Parse(string? text, int pageCount) =>
        ParseGroups(text, pageCount).SelectMany(g => g).ToList();

    private static int ParsePage(string s, string whole)
    {
        if (int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return n;
        throw new ActionException(Strings.Format("PageRanges_Error_CouldnTUnderstandThe", whole));
    }

    public static string Describe(IReadOnlyList<int> pages) =>
        pages.Count == 1 ? $"page {pages[0]}" : $"pages {pages[0]}-{pages[^1]}";
}
