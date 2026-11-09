using System.Windows.Media;

namespace AutomationPortal.Editor;

/// <summary>The 15 shortcut colours (SPEC §6): vertical gradient top to bottom; action category icons use the bottom colour.</summary>
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
        var f = Find(key);
        if (GradientCache.TryGetValue(f.Key, out var b)) return b;
        var g = new LinearGradientBrush(Top(f.Key), Bottom(f.Key), 90);
        g.Freeze();
        return GradientCache[f.Key] = g;
    }

    /// <summary>Solid bottom colour, used for category icons and pills.</summary>
    public static Brush Solid(string? key)
    {
        var f = Find(key);
        if (SolidCache.TryGetValue(f.Key, out var b)) return b;
        var s = new SolidColorBrush(Bottom(f.Key));
        s.Freeze();
        return SolidCache[f.Key] = s;
    }

    /// <summary>A translucent tint of the colour for pill backgrounds.</summary>
    public static Brush Tint(string? key, byte alpha = 0x2E)
    {
        var c = Bottom(key);
        var s = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        s.Freeze();
        return s;
    }
}
