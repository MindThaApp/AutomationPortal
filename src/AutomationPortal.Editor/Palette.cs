using System.Windows.Media;

namespace AutomationPortal.Editor;

/// <summary>The 15 shortcut colors (SPEC §6): vertical gradient top to bottom; action category icons use the bottom color.</summary>
public static class Palette
{
    private static readonly (string Key, string Top, string Bottom)[] Table =
    {
        ("red", "#FF5F57", "#D92D20"), ("orange", "#FFA94D", "#E8590C"), ("yellow", "#FFD43B", "#F08C00"),
        ("green", "#8CE99A", "#2B8A3E"), ("mint", "#63E6BE", "#099268"), ("teal", "#66D9E8", "#0C8599"),
        ("cyan", "#74C0FC", "#1971C2"), ("blue", "#5C9DFF", "#2B5DD8"), ("indigo", "#8C8CFF", "#4C3FD1"),
        ("purple", "#C08CFF", "#8132D6"), ("pink", "#FF8FC7", "#D6336C"), ("brown", "#D2A679", "#8B5E34"),
        ("gray", "#ADB5BD", "#6C757D"), ("graphite", "#6C757D", "#343A40"), ("slate", "#94A3B8", "#475569"),
    };

    public static IReadOnlyList<string> Keys { get; } = Table.Select(t => t.Key).ToArray();

    /// <summary>The ten colours the picker offers: one stored key per design-token category colour. Every other key still loads and maps onto one of these.</summary>
    public static IReadOnlyList<string> PickerKeys { get; } = new[] { "blue", "indigo", "purple", "pink", "red", "orange", "yellow", "green", "teal", "slate" };

    private static readonly Dictionary<string, string> CategoryTable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = "Red", ["orange"] = "Orange", ["yellow"] = "Amber", ["green"] = "Green", ["mint"] = "Teal", ["teal"] = "Teal",
        ["cyan"] = "Blue", ["blue"] = "Blue", ["indigo"] = "Indigo", ["purple"] = "Violet", ["pink"] = "Magenta", ["brown"] = "Amber",
        ["gray"] = "Slate", ["graphite"] = "Slate", ["slate"] = "Slate",
    };

    /// <summary>The design-token category colour name (Blue, Violet, Amber ...) a colour key maps to; unknown keys get Blue.</summary>
    public static string CategoryName(string? key) => key is not null && CategoryTable.TryGetValue(key, out var n) ? n : "Blue";

    /// <summary>The category colour name for a colour key (old keys such as "yellow" included), or null when the key is not one the app knows.</summary>
    public static string? CategoryNameOrNull(string? key) =>
        key is null ? null
        : CategoryTable.TryGetValue(key, out var n) ? n
        : AutomationPortal.Core.AppearanceMath.CategoryColors.FirstOrDefault(c => string.Equals(c, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The picker key a colour key belongs to, so an old key such as "mint" shows as the Teal swatch.</summary>
    public static string PickerKey(string? key) => PickerKeys.First(k => CategoryName(k) == CategoryName(key));

    /// <summary>The token resource key (DsCategory...Brush) of the solid square for a colour key.</summary>
    public static string CategoryResourceKey(string? key) => "DsCategory" + CategoryName(key) + "Brush";

    /// <summary>The solid category brush for a colour key: the selection colour in high contrast, a fixed colour if the token dictionaries are not loaded.</summary>
    public static Brush Category(string? key) =>
        HighContrast.Active ? System.Windows.SystemColors.HighlightBrush
        : System.Windows.Application.Current?.TryFindResource(CategoryResourceKey(key)) as Brush ?? Solid(key);

    public static bool IsValid(string? key) => key is not null && Table.Any(t => t.Key == key);

    private static (string Key, string Top, string Bottom) Find(string? key) =>
        Table.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase)) is { Key: not null } f ? f : Table[7];

    public static Color Top(string? key) => (Color)ColorConverter.ConvertFromString(Find(key).Top);
    public static Color Bottom(string? key) => (Color)ColorConverter.ConvertFromString(Find(key).Bottom);

    private static readonly Dictionary<string, Brush> GradientCache = new();
    private static readonly Dictionary<string, Brush> SolidCache = new();

    /// <summary>Vertical gradient brush for tiles and the icon chip.</summary>
    public static Brush Gradient(string? key)
    {
        if (HighContrast.Active) return System.Windows.SystemColors.HighlightBrush;
        var f = Find(key);
        if (GradientCache.TryGetValue(f.Key, out var b)) return b;
        var g = new LinearGradientBrush(Top(f.Key), Bottom(f.Key), 90);
        g.Freeze();
        return GradientCache[f.Key] = g;
    }

    /// <summary>Solid bottom color, used for category icons and pills.</summary>
    public static Brush Solid(string? key)
    {
        if (HighContrast.Active) return System.Windows.SystemColors.WindowTextBrush;
        var f = Find(key);
        if (SolidCache.TryGetValue(f.Key, out var b)) return b;
        var s = new SolidColorBrush(Bottom(f.Key));
        s.Freeze();
        return SolidCache[f.Key] = s;
    }

    /// <summary>Solid fill behind a white icon (a chip): the category colour, or the selection colour in high contrast.</summary>
    public static Brush Fill(string? key) => HighContrast.Active ? System.Windows.SystemColors.HighlightBrush : Solid(key);

    /// <summary>A translucent tint of the color for pill backgrounds.</summary>
    public static Brush Tint(string? key, byte alpha = 0x2E)
    {
        if (HighContrast.Active) return Brushes.Transparent;
        var c = Bottom(key);
        var s = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        s.Freeze();
        return s;
    }
}
