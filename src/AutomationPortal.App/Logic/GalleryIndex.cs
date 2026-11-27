using System.Text.Json;

namespace AutomationPortal.App.Logic;

public sealed record GallerySectionPlan(string Title, IReadOnlyList<string> Files);

/// <summary>
/// Pure helpers for the Gallery page: reads the optional <c>gallery\index.json</c>
/// (<c>{"sections":[{"title":"…","files":["…"]}]}</c>) and arranges the *.autoportal files into sections.
/// </summary>
public static class GalleryIndex
{
    public static string FallbackSectionTitle => Strings.Get("GalleryIndex_MoreShortcuts");

    /// <summary>Tolerant: bad or missing JSON gives an empty plan so the gallery still shows every file.</summary>
    public static IReadOnlyList<GallerySectionPlan> Parse(string? indexJson)
    {
        var result = new List<GallerySectionPlan>();
        if (string.IsNullOrWhiteSpace(indexJson)) return result;
        try
        {
            using var doc = JsonDocument.Parse(indexJson, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            if (!TryGetIgnoreCase(doc.RootElement, "sections", out var sections) || sections.ValueKind != JsonValueKind.Array) return result;
            foreach (var s in sections.EnumerateArray())
            {
                if (s.ValueKind != JsonValueKind.Object) continue;
                var title = TryGetIgnoreCase(s, "title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                if (string.IsNullOrWhiteSpace(title)) continue;
                var files = new List<string>();
                if (TryGetIgnoreCase(s, "files", out var f) && f.ValueKind == JsonValueKind.Array)
                    foreach (var item in f.EnumerateArray())
                        if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())) files.Add(item.GetString()!.Trim());
                result.Add(new GallerySectionPlan(title!.Trim(), files));
            }
        }
        catch (JsonException) { }
        return result;
    }

    private static bool TryGetIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var p in obj.EnumerateObject())
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { value = p.Value; return true; }
        value = default;
        return false;
    }

    /// <summary>
    /// Puts the files found on disk into the index's sections (matching by file name, with or without the
    /// .autoportal extension). Files the index doesn't mention go to a final section. Empty sections are dropped.
    /// </summary>
    public static IReadOnlyList<GallerySectionPlan> Arrange(IReadOnlyList<string> filesOnDisk, IReadOnlyList<GallerySectionPlan> index)
    {
        var byKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in filesOnDisk.OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
            byKey.TryAdd(Key(f), f);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<GallerySectionPlan>();
        foreach (var section in index)
        {
            var files = new List<string>();
            foreach (var name in section.Files)
                if (byKey.TryGetValue(Key(name), out var path) && used.Add(path)) files.Add(path);
            if (files.Count > 0) result.Add(new GallerySectionPlan(section.Title, files));
        }

        var rest = byKey.Values.Where(p => !used.Contains(p)).ToList();
        if (rest.Count > 0)
            result.Add(new GallerySectionPlan(result.Count == 0 ? Strings.Get("GalleryIndex_AllShortcuts") : FallbackSectionTitle, rest));
        return result;
    }

    private static string Key(string nameOrPath)
    {
        var n = Path.GetFileName(nameOrPath.Trim());
        return n.EndsWith(".autoportal", StringComparison.OrdinalIgnoreCase) ? n[..^".autoportal".Length] : n;
    }
}
