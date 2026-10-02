using Flowmate.Core;

namespace Flowmate.Editor.Logic;

/// <summary>Searching and suggesting actions for the action panel.</summary>
public static class ActionSearch
{
    /// <summary>
    /// Ranked search over title, keywords, category, description and id. Every word of the query must match something.
    /// Starred actions win ties.
    /// </summary>
    public static IReadOnlyList<ActionDescriptor> Search(ActionRegistry registry, string query, bool includeAdvanced,
        IReadOnlyCollection<string>? favourites = null, IReadOnlyList<string>? recents = null)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return Array.Empty<ActionDescriptor>();
        var scored = new List<(ActionDescriptor D, int Score)>();
        foreach (var a in registry.All)
        {
            var d = a.Descriptor;
            if (d.IsAdvanced && !includeAdvanced) continue;
            var total = 0;
            var ok = true;
            foreach (var w in words)
            {
                var s = WordScore(d, w);
                if (s == 0) { ok = false; break; }
                total += s;
            }
            if (!ok) continue;
            if (favourites?.Contains(d.Id) == true) total += 5;
            if (recents is not null) { var r = IndexOf(recents, d.Id); if (r >= 0) total += Math.Max(0, 4 - r); }
            scored.Add((d, total));
        }
        return scored.OrderByDescending(s => s.Score).ThenBy(s => s.D.Title, StringComparer.CurrentCultureIgnoreCase).Select(s => s.D).ToList();
    }

    internal static int WordScore(ActionDescriptor d, string q)
    {
        var cmp = StringComparison.CurrentCultureIgnoreCase;
        var best = 0;
        if (d.Title.Equals(q, cmp)) best = 100;
        else if (d.Title.StartsWith(q, cmp)) best = 85;
        else if (d.Title.Split(' ', '/', '-').Any(w => w.StartsWith(q, cmp))) best = 72;
        else if (d.Title.Contains(q, cmp)) best = 60;
        if (d.Keywords.Any(k => k.Equals(q, cmp))) best = Math.Max(best, 66);
        else if (d.Keywords.Any(k => k.StartsWith(q, cmp))) best = Math.Max(best, 55);
        else if (d.Keywords.Any(k => k.Contains(q, cmp))) best = Math.Max(best, 40);
        if (d.Category.StartsWith(q, cmp)) best = Math.Max(best, 35);
        else if (d.Category.Contains(q, cmp)) best = Math.Max(best, 30);
        if (d.Id.Contains(q, cmp)) best = Math.Max(best, 25);
        if (d.Description.Contains(q, cmp)) best = Math.Max(best, 10);
        if (best == 0 && q.Length >= 3 && IsSubsequence(q, d.Title)) best = 15;
        return best;
    }

    private static bool IsSubsequence(string needle, string hay)
    {
        var i = 0;
        foreach (var c in hay)
            if (i < needle.Length && char.ToLowerInvariant(c) == char.ToLowerInvariant(needle[i])) i++;
        return i == needle.Length;
    }

    private static int IndexOf(IReadOnlyList<string> list, string id)
    {
        for (var i = 0; i < list.Count; i++) if (string.Equals(list[i], id, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>
    /// "What usually comes next": actions that accept the previous action's output kind first (recents and stars ranked higher),
    /// then recently used actions. With nothing above, recents, stars and a few starters.
    /// </summary>
    public static IReadOnlyList<ActionDescriptor> Suggest(ActionRegistry registry, ContentKind? previousOutput,
        IReadOnlyList<string> recents, IReadOnlyCollection<string> favourites, bool includeAdvanced, int max = 8)
    {
        bool Allowed(ActionDescriptor d) => includeAdvanced || !d.IsAdvanced;
        var result = new List<ActionDescriptor>();
        void Add(ActionDescriptor? d) { if (d is not null && Allowed(d) && d.FlowKind == FlowKind.None && !result.Contains(d) && result.Count < max) result.Add(d); }

        var recentDescriptors = recents.Select(registry.Describe).Where(d => d is not null).Select(d => d!).ToList();

        if (previousOutput is { } kind && kind != ContentKind.Any)
        {
            var matching = registry.All.Select(a => a.Descriptor)
                .Where(d => Allowed(d) && d.FlowKind == FlowKind.None && d.InputKinds.Contains(kind))
                .OrderBy(d => IndexOfRecent(recents, d.Id) is var r && r >= 0 ? r : 100)
                .ThenBy(d => favourites.Contains(d.Id) ? 0 : 1)
                .ThenBy(d => d.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            foreach (var d in matching.Take(Math.Max(1, max - Math.Min(3, recentDescriptors.Count)))) Add(d);
        }
        foreach (var d in recentDescriptors) Add(d);
        foreach (var id in favourites) Add(registry.Describe(id));
        foreach (var id in new[] { "ui.ask", "content.text", "ui.showResult", "clip.set", "web.open", "ui.notify" }) Add(registry.Describe(id));
        return result;
    }

    private static int IndexOfRecent(IReadOnlyList<string> recents, string id) => IndexOf(recents, id);
}
