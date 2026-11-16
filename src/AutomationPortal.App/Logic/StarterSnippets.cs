using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

/// <summary>The two snippets offered on an empty Snippets page (they replace the two "Text Expansion" Gallery items).</summary>
public static class StarterSnippets
{
    /// <summary>Gallery shortcuts that are really snippets; the Gallery leaves them out and the Snippets page offers them instead.</summary>
    public static bool IsGalleryDuplicate(string shortcutName) => shortcutName.StartsWith("Text Expansion:", StringComparison.OrdinalIgnoreCase);

    /// <summary>The starters whose abbreviation isn't used yet, as new snippets.</summary>
    public static IReadOnlyList<Snippet> Missing(IEnumerable<Snippet> existing)
    {
        var used = existing.Select(s => s.Abbreviation).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var list = new List<Snippet>();
        if (!used.Contains(";date")) list.Add(new Snippet { Abbreviation = ";date", Text = "{date:yyyy-MM-dd}", Enabled = true });
        if (!used.Contains(";addr")) list.Add(new Snippet { Abbreviation = ";addr", Text = "{input:My address}", Enabled = true });
        return list;
    }
}
