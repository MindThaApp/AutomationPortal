namespace AutomationPortal.App.Logic;

public enum PaletteKind { Shortcut, Command, Gallery }

/// <summary>One line in the command palette. <see cref="Key"/> says what to do with it (a shortcut id, a command id or a gallery file name).</summary>
public sealed record PaletteEntry(PaletteKind Kind, string Title, string? Detail, string Key, string? Alias = null, IReadOnlyList<string>? Keywords = null);

/// <summary>Ranks palette entries for what was typed: best match first, shortcuts before commands before Gallery items on a tie.</summary>
public static class PaletteSearch
{
    public const int DefaultMax = 12;

    public static IReadOnlyList<PaletteEntry> Rank(IEnumerable<PaletteEntry> entries, string? query, int max = DefaultMax)
    {
        query = query?.Trim() ?? "";
        if (query.Length == 0)
            // Nothing typed yet: the commands, then the shortcuts, each in the order they were given.
            return entries.OrderBy(e => e.Kind == PaletteKind.Command ? 0 : e.Kind == PaletteKind.Shortcut ? 1 : 2).Take(max).ToList();

        var scored = new List<(PaletteEntry Entry, int Score)>();
        foreach (var e in entries)
        {
            var best = FuzzyMatcher.ScoreNameAndAlias(query, e.Title, e.Alias);
            if (e.Keywords is not null)
                foreach (var k in e.Keywords)
                    if (FuzzyMatcher.Score(query, k) is { } ks && ks - 150 > (best ?? int.MinValue)) best = ks - 150;
            if (best is null) continue;
            scored.Add((e, best.Value));
        }
        return scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => (int)x.Entry.Kind)
            .ThenBy(x => x.Entry.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(max)
            .Select(x => x.Entry)
            .ToList();
    }
}
