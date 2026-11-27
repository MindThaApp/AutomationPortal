using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace AutomationPortal.App.Logic;

/// <summary>One stop of the feature tour: a screenshot, a title, a sentence or two and the page "Show me" opens.</summary>
public sealed record TourStop(string Id, string Title, string Body, string Image, string? ShowMe);

/// <summary>The feature tour's content. Read from Assets/Tour/tour.json, embedded in the app; the images sit beside it as resources.</summary>
public static class Tour
{
    public const string ResourceName = "Tour.tour.json";
    public const int MinStops = 10, MaxStops = 14;

    public static IReadOnlyList<TourStop>? Load() => Load(CultureInfo.CurrentUICulture);

    /// <summary>
    /// Loads the English tour and, when there is one, the file for the culture (full name, then parent name). A stop the translation
    /// lacks falls back to the English one; images and "Show me" targets always come from English.
    /// </summary>
    public static IReadOnlyList<TourStop>? Load(CultureInfo culture)
    {
        var english = ReadResource(ResourceName);
        var en = english is null ? null : Parse(english);
        if (en is null) return null;
        foreach (var name in WhatsNew.CultureNames(culture))
        {
            var text = ReadResource(ResourceNameFor(name));
            var loc = text is null ? null : Parse(text);
            if (loc is null) continue;
            return en.Select(e => loc.FirstOrDefault(l => l.Id == e.Id) is { } l ? e with { Title = l.Title, Body = l.Body } : e).ToList();
        }
        return en;
    }

    public static string ResourceNameFor(string culture) => "Tour.tour." + culture + ".json";

    private static string? ReadResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Reads the tour JSON. Null when it isn't valid; stops without an id, title or image are left out.</summary>
    public static IReadOnlyList<TourStop>? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("stops", out var arr) || arr.ValueKind != JsonValueKind.Array) return null;
            var stops = new List<TourStop>();
            foreach (var e in arr.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var id = Str(e, "id");
                var title = Str(e, "title");
                var image = Str(e, "image");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(image)) continue;
                stops.Add(new TourStop(id, title, Str(e, "body") ?? "", image, Str(e, "showMe")));
            }
            return stops;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>The pack URI of a stop's image.</summary>
    public static Uri ImageUri(string image) => new("pack://application:,,,/" + image.TrimStart('/'));

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}

/// <summary>Where the tour is: which stop, and whether it has ended. No windows, so it can be tested.</summary>
public sealed class TourNav
{
    public TourNav(int count) { Count = Math.Max(1, count); }

    public int Count { get; }
    public int Current { get; private set; }
    public bool Ended { get; private set; }

    public bool CanGoBack => Current > 0;
    public bool IsLast => Current == Count - 1;

    /// <summary>Moves to the next stop. On the last stop it ends the tour and returns false.</summary>
    public bool Next()
    {
        if (IsLast) { Ended = true; return false; }
        Current++;
        return true;
    }

    public bool Back()
    {
        if (!CanGoBack) return false;
        Current--;
        return true;
    }

    /// <summary>Ends the tour from any stop.</summary>
    public void Skip() => Ended = true;
}
