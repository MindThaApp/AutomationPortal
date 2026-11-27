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

    public static WhatsNewContent? Load(Version v)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName(v));
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
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
