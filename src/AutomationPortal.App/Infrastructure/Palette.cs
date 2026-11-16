using System.Windows;
using System.Windows.Media;

namespace AutomationPortal.App.Infrastructure;

/// <summary>The SPEC §6 color palette: each key has a top and bottom gradient color.</summary>
public static class Palette
{
    public static readonly IReadOnlyList<string> Keys =
        new[] { "red", "orange", "yellow", "green", "mint", "teal", "cyan", "blue", "indigo", "purple", "pink", "brown", "gray", "graphite", "slate" };

    private static readonly Dictionary<string, (string Top, string Bottom)> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = ("#FF5F57", "#D92D20"), ["orange"] = ("#FFA94D", "#E8590C"), ["yellow"] = ("#FFD43B", "#F08C00"),
        ["green"] = ("#8CE99A", "#2B8A3E"), ["mint"] = ("#63E6BE", "#099268"), ["teal"] = ("#66D9E8", "#0C8599"),
        ["cyan"] = ("#74C0FC", "#1971C2"), ["blue"] = ("#5C9DFF", "#2B5DD8"), ["indigo"] = ("#8C8CFF", "#4C3FD1"),
        ["purple"] = ("#C08CFF", "#8132D6"), ["pink"] = ("#FF8FC7", "#D6336C"), ["brown"] = ("#D2A679", "#8B5E34"),
        ["gray"] = ("#ADB5BD", "#6C757D"), ["graphite"] = ("#6C757D", "#343A40"), ["slate"] = ("#94A3B8", "#475569"),
    };

    public static (string Top, string Bottom) Hex(string? key) =>
        key is not null && Table.TryGetValue(key, out var v) ? v : Table["blue"];

    public static Color Top(string? key) => (Color)ColorConverter.ConvertFromString(Hex(key).Top);
    public static Color Bottom(string? key) => (Color)ColorConverter.ConvertFromString(Hex(key).Bottom);

    private static readonly Dictionary<string, Brush> GradientCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Brush> SolidCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Vertical top to bottom gradient used for shortcut tiles.</summary>
    public static Brush Gradient(string? key)
    {
        var k = key is not null && Table.ContainsKey(key) ? key : "blue";
        if (GradientCache.TryGetValue(k, out var b)) return b;
        var g = new LinearGradientBrush(Top(k), Bottom(k), new Point(0.5, 0), new Point(0.5, 1));
        g.Freeze();
        return GradientCache[k] = g;
    }

    /// <summary>Bottom color as a solid brush (action category icons).</summary>
    public static Brush Solid(string? key)
    {
        var k = key is not null && Table.ContainsKey(key) ? key : "blue";
        if (SolidCache.TryGetValue(k, out var b)) return b;
        var s = new SolidColorBrush(Bottom(k));
        s.Freeze();
        return SolidCache[k] = s;
    }
}
