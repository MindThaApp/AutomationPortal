using System.Windows;
using AutomationPortal.Core;

namespace AutomationPortal.Editor;

/// <summary>
/// Turns the Appearance settings into design-token values (radii, paddings, type sizes, tile size) and motion, and writes them over
/// the defaults in Tokens.xaml. Views read the tokens with DynamicResource, so a change shows at once with no restart.
/// </summary>
public static class AppearanceTokens
{
    private static readonly (string Key, double Size)[] Ramp =
    {
        ("DsFontSizeCaption", TypeRamp.Caption), ("DsFontSizeReadable", TypeRamp.Readable), ("DsFontSizeBody", TypeRamp.Body),
        ("DsFontSizePanelTitle", TypeRamp.PanelTitle), ("DsFontSizeSubtitle", TypeRamp.Subtitle), ("DsFontSizeTitle", TypeRamp.Title),
    };

    private static readonly (string Key, double Space)[] Spacing =
    {
        ("DsSpace4", 4), ("DsSpace8", 8), ("DsSpace12", 12), ("DsSpace16", 16), ("DsSpace20", 20), ("DsSpace24", 24), ("DsSpace32", 32), ("DsSpace48", 48),
    };

    public const double CardTileWidth = 182, CardTileHeight = 156, CompactTileWidth = 164, CompactTileHeight = 120;

    /// <summary>Every token the settings change, by resource key.</summary>
    public static IReadOnlyDictionary<string, object> Tokens(AppSettings s)
    {
        var t = new Dictionary<string, object>();
        var (control, card) = AppearanceMath.Radii(s.CornerStyle);
        t["DsRadiusControl"] = new CornerRadius(control);
        t["DsRadiusCard"] = new CornerRadius(card);
        t["DsRadiusControlValue"] = control;
        t["DsRadiusCardValue"] = card;

        var d = s.Density;
        t["DsCardPadding"] = new Thickness(AppearanceMath.Scaled(16, d), AppearanceMath.Scaled(12, d), AppearanceMath.Scaled(16, d), AppearanceMath.Scaled(12, d));
        t["DsCardGap"] = new Thickness(0, 0, 0, AppearanceMath.Scaled(6, d));
        t["DsListItemMargin"] = new Thickness(0, 0, 8, AppearanceMath.Scaled(3, d));
        foreach (var (key, space) in Spacing) t[key] = AppearanceMath.Scaled(space, d);

        foreach (var (key, size) in Ramp) t[key] = AppearanceMath.FontSize(size, s.TextScale);

        var compact = s.TileLook == TileLook.Compact;
        t["DsTileWidth"] = compact ? CompactTileWidth : CardTileWidth;
        t["DsTileHeight"] = compact ? CompactTileHeight : CardTileHeight;
        return t;
    }

    public static MotionMode ToMotion(MotionSetting m) => m switch
    {
        MotionSetting.Reduced => MotionMode.Reduced,
        MotionSetting.Off => MotionMode.Off,
        _ => MotionMode.FollowWindows,
    };

    /// <summary>Applies the token values and the motion mode now.</summary>
    public static void Apply(Application app, AppSettings s)
    {
        foreach (var (key, value) in Tokens(s)) app.Resources[key] = value;
        Motion.Mode = ToMotion(s.MotionMode);
    }
}
