using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

/// <summary>The current tile style (set from settings) and the text colour rule for coloured tiles.</summary>
public static class TileAppearance
{
    /// <summary>Kept in step with the setting by the host; tiles read it when they are created.</summary>
    public static TileStyleSetting Style { get; set; } = TileStyleSetting.Neutral;

    /// <summary>WCAG relative luminance of an sRGB colour.</summary>
    public static double Luminance(byte r, byte g, byte b)
    {
        static double Lin(byte v) { var c = v / 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        return 0.2126 * Lin(r) + 0.7152 * Lin(g) + 0.0722 * Lin(b);
    }

    public static double ContrastRatio(double l1, double l2)
    {
        var hi = Math.Max(l1, l2); var lo = Math.Min(l1, l2);
        return (hi + 0.05) / (lo + 0.05);
    }

    /// <summary>True when dark text reads better than white text on this background.</summary>
    public static bool UseDarkText(byte r, byte g, byte b)
    {
        var l = Luminance(r, g, b);
        return ContrastRatio(l, 0) > ContrastRatio(l, 1);
    }

    /// <summary>Same, for a "#RRGGBB" colour.</summary>
    public static bool UseDarkText(string hex)
    {
        var h = hex.TrimStart('#');
        return UseDarkText(Convert.ToByte(h[..2], 16), Convert.ToByte(h.Substring(2, 2), 16), Convert.ToByte(h.Substring(4, 2), 16));
    }

    /// <summary>The colour half way between two "#RRGGBB" colours (what sits behind an icon on a gradient chip).</summary>
    public static string Mid(string a, string b)
    {
        static int C(string h, int i) => Convert.ToInt32(h.TrimStart('#').Substring(i * 2, 2), 16);
        return "#" + string.Concat(Enumerable.Range(0, 3).Select(i => ((C(a, i) + C(b, i)) / 2).ToString("X2")));
    }

    public const string DarkText = "#000000";
    public const string LightText = "#FFFFFF";

    /// <summary>The name's colour: judged on the bottom colour, where the name sits.</summary>
    public static string TextFor(string bottom) => UseDarkText(bottom) ? DarkText : LightText;
    public static string IconFor(string top) => UseDarkText(top) ? DarkText : LightText;
}
