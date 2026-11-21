using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace AutomationPortal.Editor;

/// <summary>
/// Keeps the shared design tokens (Resources/Tokens*.xaml) in step with the theme: the light, dark or high contrast
/// dictionary is swapped in when the theme or the Windows high contrast setting changes, and the accent tokens follow the Windows accent.
/// </summary>
public static class DesignTokens
{
    private const string Folder = "pack://application:,,,/AutomationPortal.Editor;component/Resources/";

    private static bool _attached;

    /// <summary>Merges the token dictionaries and follows theme changes. Safe to call more than once.</summary>
    public static void Attach(Application app)
    {
        if (_attached) return;
        _attached = true;
        var merged = app.Resources.MergedDictionaries;
        if (!merged.Any(d => IsNamed(d, "Tokens")))
            merged.Add(new ResourceDictionary { Source = new Uri(Folder + "Tokens.xaml", UriKind.Absolute) });
        Apply(app);
        ApplicationThemeManager.Changed += (_, _) => Apply(app);
    }

    /// <summary>Which token set is in force right now.</summary>
    public static string CurrentSet =>
        HighContrast.Active ? "HighContrast" : ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark ? "Dark" : "Light";

    /// <summary>Swaps in the token set for the current theme and refreshes the accent tokens.</summary>
    public static void Apply(Application app)
    {
        var merged = app.Resources.MergedDictionaries;
        var set = CurrentSet;
        var current = merged.FirstOrDefault(d => d.Source is { } s && s.OriginalString.Contains("/Tokens.", StringComparison.Ordinal) && !IsNamed(d, "Tokens"));
        if (current is null || !IsNamed(current, "Tokens." + set))
        {
            if (current is not null) merged.Remove(current);
            merged.Add(new ResourceDictionary { Source = new Uri(Folder + "Tokens." + set + ".xaml", UriKind.Absolute) });
        }
        ApplyAccent(app, set == "HighContrast");
    }

    private static bool IsNamed(ResourceDictionary d, string name) =>
        d.Source is { } s && s.OriginalString.EndsWith("/" + name + ".xaml", StringComparison.Ordinal);

    private static readonly string[] AccentKeys = { "DsAccentBrush", "DsAccentTextBrush", "DsFocusRingBrush", "DsSelectionFillBrush", "DsSelectionHoverFillBrush" };

    private static void ApplyAccent(Application app, bool highContrast)
    {
        var r = app.Resources;
        if (highContrast)
        {
            foreach (var k in AccentKeys) r.Remove(k);
            return;
        }
        var accent = app.TryFindResource("AccentFillColorDefaultBrush") as SolidColorBrush;
        var accentText = app.TryFindResource("AccentTextFillColorPrimaryBrush") as Brush;
        if (accent is null) return;
        var dark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
        r["DsAccentBrush"] = accent;
        r["DsFocusRingBrush"] = accent;
        if (accentText is not null) r["DsAccentTextBrush"] = accentText;
        r["DsSelectionFillBrush"] = Tint(accent.Color, dark ? 0.22 : 0.14);
        r["DsSelectionHoverFillBrush"] = Tint(accent.Color, dark ? 0.28 : 0.18);
    }

    private static SolidColorBrush Tint(Color c, double alpha)
    {
        var b = new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255), c.R, c.G, c.B));
        b.Freeze();
        return b;
    }
}
