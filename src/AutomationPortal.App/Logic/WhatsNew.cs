using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace AutomationPortal.App.Logic;

/// <summary>One highlight card: a picture area, a title, a sentence or two and an optional "Show me" target page.</summary>
public sealed record WhatsNewCard(string Title, string Body, string? Image, string? ShowMe);

/// <summary>What one release's What's new window shows. Read from Assets/WhatsNew/&lt;version&gt;.json, embedded in the app.</summary>
public sealed record WhatsNewContent(string Thanks, IReadOnlyList<string> Fixes, IReadOnlyList<WhatsNewCard> Cards);

/// <summary>The outcome of the start-up check: whether to open the window, and which version to remember.</summary>
public sealed record WhatsNewPlan(bool Show, string? RecordVersion, bool ContentMissing);

public static class WhatsNew
{
    /// <summary>Used when a release has no fix that is easy to explain.</summary>
    public const string GeneralFixLine = "Thanks to your reports we fixed several behind-the-scenes problems and slowdowns.";

    /// <summary>
    /// Decides at start-up. A fresh install (welcome showing, or nothing recorded and no welcome ever shown) never gets it and just records the
    /// version. An upgrade shows it once when this version has content; the version is recorded either way so it never repeats.
    /// A background start (sign-in) or a throwaway data root changes nothing.
    /// </summary>
    public static WhatsNewPlan Plan(string? lastSeen, Version current, bool welcomeShownBefore, bool welcomeShowingNow,
        bool background, bool customDataRoot, Func<Version, bool> hasContent)
    {
        if (background || customDataRoot) return new WhatsNewPlan(false, null, false);
        var seen = TryParse(lastSeen);
        if (seen is null && (welcomeShowingNow || !welcomeShownBefore))
            return new WhatsNewPlan(false, current.ToString(), false);
        if (seen is not null && Normalize(current) <= Normalize(seen)) return new WhatsNewPlan(false, null, false);
        return hasContent(current)
            ? new WhatsNewPlan(true, current.ToString(), false)
            : new WhatsNewPlan(false, current.ToString(), true);
    }

    private static Version? TryParse(string? s) => Version.TryParse(s, out var v) ? v : null;

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    public static string ResourceName(Version v) => "WhatsNew." + Normalize(v) + ".json";

    public static bool HasContent(Version v) => Assembly.GetExecutingAssembly().GetManifestResourceInfo(ResourceName(v)) is not null;

    /// <summary>Loads the release in the current UI language; see <see cref="Load(Version, CultureInfo)"/>.</summary>
    public static WhatsNewContent? Load(Version v) => Load(v, CultureInfo.CurrentUICulture);

    /// <summary>
    /// Loads the English file and, when there is one, the file for the culture (full name, then parent name). Anything the translation
    /// lacks falls back to the English entry. Null only when the English file is missing or invalid.
    /// </summary>
    public static WhatsNewContent? Load(Version v, CultureInfo culture)
    {
        var english = ReadResource(ResourceName(v));
        var en = english is null ? null : Parse(english);
        if (en is null) return null;
        foreach (var name in CultureNames(culture))
        {
            var text = ReadResource(ResourceName(v, name));
            var loc = text is null ? null : Parse(text);
            if (loc is not null) return Merge(loc, en);
        }
        return en;
    }

    public static string ResourceName(Version v, string culture) => "WhatsNew." + Normalize(v) + "." + culture + ".json";

    /// <summary>The culture names to try, most specific first, without the invariant culture.</summary>
    public static IEnumerable<string> CultureNames(CultureInfo culture)
    {
        if (!string.IsNullOrEmpty(culture.Name)) yield return culture.Name;
        var parent = culture.Parent;
        if (!string.IsNullOrEmpty(parent.Name) && parent.Name != culture.Name) yield return parent.Name;
    }

    private static string? ReadResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static WhatsNewContent Merge(WhatsNewContent loc, WhatsNewContent en)
    {
        var thanks = string.IsNullOrWhiteSpace(loc.Thanks) ? en.Thanks : loc.Thanks;
        var fixes = loc.Fixes.Count >= en.Fixes.Count ? loc.Fixes : en.Fixes;
        var cards = new List<WhatsNewCard>();
        for (var i = 0; i < en.Cards.Count; i++)
            cards.Add(i < loc.Cards.Count ? loc.Cards[i] with { Image = en.Cards[i].Image, ShowMe = en.Cards[i].ShowMe } : en.Cards[i]);
        return new WhatsNewContent(thanks, fixes, cards);
    }

    /// <summary>Reads the release JSON. Null when it isn't valid; missing parts become empty, and no fixes means the general line.</summary>
    public static WhatsNewContent? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var thanks = Str(root, "thanks") ?? "";
            var fixes = new List<string>();
            if (root.TryGetProperty("fixes", out var f) && f.ValueKind == JsonValueKind.Array)
                foreach (var e in f.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString())) fixes.Add(e.GetString()!);
            if (fixes.Count == 0) fixes.Add(GeneralFixLine);
            var cards = new List<WhatsNewCard>();
            if (root.TryGetProperty("cards", out var c) && c.ValueKind == JsonValueKind.Array)
                foreach (var e in c.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    var title = Str(e, "title");
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    cards.Add(new WhatsNewCard(title, Str(e, "body") ?? "", Str(e, "image"), Str(e, "showMe")));
                }
            return new WhatsNewContent(thanks, fixes, cards);
        }
        catch (JsonException) { return null; }
    }

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.GetString()) ? p.GetString() : null;
}
