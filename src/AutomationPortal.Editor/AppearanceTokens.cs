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

    public const double CardTileWidth = 182, CardTileHeight = 156, CompactTileWidth = 182, CompactTileHeight = 108;

    /// <summary>Every token the settings change, by resource key.</summary>
    public static IReadOnlyDictionary<string, object> Tokens(AppSettings s)
    {
        var t = new Dictionary<string, object>();
        var (control, card) = AppearanceMath.Radii(s.CornerStyle);
        t["DsRadiusControl"] = new CornerRadius(control);
        t["DsRadiusCard"] = new CornerRadius(card);
        t["DsRadiusControlValue"] = control;
        t["DsRadiusCardValue"] = card;
        t["ControlCornerRadius"] = new CornerRadius(control);   // WPF-UI controls read these two keys
        t["OverlayCornerRadius"] = new CornerRadius(card);

        var d = s.Density;
        t["DsCardPadding"] = new Thickness(AppearanceMath.Scaled(16, d), AppearanceMath.Scaled(12, d), AppearanceMath.Scaled(16, d), AppearanceMath.Scaled(12, d));
        t["DsCardGap"] = new Thickness(0, 0, 0, AppearanceMath.Scaled(6, d));
        t["DsListItemMargin"] = new Thickness(0, 0, 8, AppearanceMath.Scaled(3, d));
        foreach (var (key, space) in Spacing) t[key] = AppearanceMath.Scaled(space, d);

        foreach (var (key, size) in Ramp) t[key] = AppearanceMath.FontSize(size, s.TextScale);

        // Tile size grows with the text scale so the title and the Run and Edit buttons always fit.
        var compact = s.TileLook == TileLook.Compact;
        var scale = AppearanceMath.ClampTextScale(s.TextScale) / 100.0;
        t["DsTileWidth"] = Math.Round((compact ? CompactTileWidth : CardTileWidth) * scale, 1);
        t["DsTileHeight"] = Math.Round((compact ? CompactTileHeight : CardTileHeight) * scale, 1);
        // Cards: icon on top, title below. Compact: icon and title on one row, title to the right of the icon.
        t["DsTileNameRow"] = compact ? 0 : 1;
        t["DsTileNameColumn"] = compact ? 1 : 0;
        t["DsTileNameColumnSpan"] = compact ? 1 : 4;
        t["DsTileNameMargin"] = compact ? new Thickness(10, 0, 4, 0) : new Thickness(0, 6, 0, 0);
        t["DsTileNameVerticalAlignment"] = compact ? VerticalAlignment.Center : VerticalAlignment.Bottom;
        var lineHeight = Math.Round(AppearanceMath.FontSize(TypeRamp.Body, s.TextScale) * 1.3, 1);
        t["DsTileTitleLineHeight"] = lineHeight;
        t["DsTileTitleMaxHeight"] = lineHeight * 2;   // two lines, then an ellipsis
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

/// <summary>The current corner radii, for views built in code. Views that must follow a settings change live also bind the Ds radius tokens with a resource reference.</summary>
public static class Radius
{
    public static CornerRadius Card => Read("DsRadiusCard", 8);
    public static CornerRadius Control => Read("DsRadiusControl", 4);

    private static CornerRadius Read(string key, double fallback) =>
        System.Windows.Application.Current?.TryFindResource(key) is CornerRadius r ? r : new CornerRadius(fallback);
}
