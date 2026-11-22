using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutomationPortal.Core;

// The first member of each enum is the default, so a missing or unknown value in settings.json falls back to it.
public enum AccentMode { Windows, Preset }
public enum MaterialSetting { Mica, Acrylic, Solid }
public enum CornerStyle { Rounded, Square, ExtraRound }
public enum Density { Standard, Compact, Comfortable }
public enum MotionSetting { FollowWindows, Reduced, Off }
public enum TileLook { Cards, Compact }

/// <summary>Reads an enum from its name (or number) and falls back to the first member for anything else, so a settings file written by another version still loads.</summary>
public sealed class TolerantEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var named) && Enum.IsDefined(named)) return named;
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var n) && Enum.IsDefined(typeof(T), n)) return (T)Enum.ToObject(typeof(T), n);
        reader.Skip();
        return default;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
}

/// <summary>The numbers behind the Appearance settings, kept apart from any window code so they can be checked on their own.</summary>
public static class AppearanceMath
{
    public const int MinTextScale = 90, MaxTextScale = 150, DefaultTextScale = 100;
    public static readonly IReadOnlyList<int> TextScaleSteps = new[] { 90, 100, 110, 125, 150 };

    /// <summary>Colour keys a preset accent can use: the ten category colours.</summary>
    public static readonly IReadOnlyList<string> CategoryColors =
        new[] { "Blue", "Indigo", "Violet", "Magenta", "Red", "Orange", "Amber", "Green", "Teal", "Slate" };

    public static int ClampTextScale(int percent) => Math.Clamp(percent, MinTextScale, MaxTextScale);

    /// <summary>Corner radius (control, card) in pixels.</summary>
    public static (double Control, double Card) Radii(CornerStyle style) => style switch
    {
        CornerStyle.Square => (0, 2),
        CornerStyle.ExtraRound => (8, 16),
        _ => (4, 8),
    };

    /// <summary>Multiplier for paddings and row heights.</summary>
    public static double DensityFactor(Density density) => density switch
    {
        Density.Compact => 0.85,
        Density.Comfortable => 1.15,
        _ => 1.0,
    };

    public static double Scaled(double value, Density density) => Math.Round(value * DensityFactor(density), 1);

    /// <summary>A type-ramp size after the in-app step. It multiplies Windows text scaling and never drops below 12 x the step.</summary>
    public static double FontSize(double baseSize, int textScalePercent)
    {
        var scale = ClampTextScale(textScalePercent) / 100.0;
        return Math.Round(Math.Max(baseSize, 12) * scale, 1);
    }

    public static double RelativeLuminance(byte r, byte g, byte b)
    {
        static double Lin(byte c) { var s = c / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Lin(r) + 0.7152 * Lin(g) + 0.0722 * Lin(b);
    }

    /// <summary>WCAG contrast ratio between two colours, 1 to 21.</summary>
    public static double ContrastRatio((byte R, byte G, byte B) a, (byte R, byte G, byte B) b)
    {
        var la = RelativeLuminance(a.R, a.G, a.B);
        var lb = RelativeLuminance(b.R, b.G, b.B);
        var hi = Math.Max(la, lb);
        var lo = Math.Min(la, lb);
        return (hi + 0.05) / (lo + 0.05);
    }

    /// <summary>
    /// Moves a colour towards black (on a lighter background) or white (on a darker one), a little at a time, until it reaches
    /// <paramref name="minRatio"/> against the background. A colour that already passes is returned unchanged.
    /// </summary>
    public static (byte R, byte G, byte B) EnsureContrast((byte R, byte G, byte B) color, (byte R, byte G, byte B) background, double minRatio = 4.5)
    {
        if (ContrastRatio(color, background) >= minRatio) return color;
        var towardsBlack = RelativeLuminance(background.R, background.G, background.B) > 0.18;
        var target = towardsBlack ? (byte)0 : (byte)255;
        for (var i = 1; i <= 100; i++)
        {
            var t = i / 100.0;
            var c = (Mix(color.R, target, t), Mix(color.G, target, t), Mix(color.B, target, t));
            if (ContrastRatio(c, background) >= minRatio) return c;
        }
        return (target, target, target);
    }

    private static byte Mix(byte from, byte to, double t) => (byte)Math.Round(from + (to - from) * t);
}
