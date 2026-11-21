using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace AutomationPortal.Editor.UI;

/// <summary>Small helpers for building the editor's UI in code with theme-aware (dynamic) brushes.</summary>
internal static class Ui
{
    public const string Primary = "DsTextPrimaryBrush";
    public const string Secondary = "DsTextSecondaryBrush";
    public const string Tertiary = "DsTextTertiaryBrush";
    public const string Accent = "DsAccentTextBrush";
    public const string Critical = "DsCriticalBrush";
    public const string Success = "DsSuccessBrush";
    public const string Caution = "DsCautionBrush";
    public const string CardFill = "DsCardBrush";
    public const string CardStroke = "DsStrokeBrush";
    public const string ControlStroke = "DsStrokeBrush";
    public const string PopupFill = "SolidBackgroundFillColorSecondaryBrush";
    public const string SubtleHover = "DsHoverBrush";
    public const string ControlFill = "ControlFillColorDefaultBrush";

    public static readonly FontFamily Font = new(Strings.Get("Ui_SegoeUIVariableText"));

    public static T Dyn<T>(this T element, DependencyProperty property, string resourceKey) where T : FrameworkElement
    {
        element.SetResourceReference(property, resourceKey);
        return element;
    }

    public static TextBlock Text(string text, string fgKey = Primary, double size = 14, FontWeight? weight = null, bool wrap = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis };
        if (wrap) t.TextWrapping = TextWrapping.Wrap;
        if (weight is { } w) t.FontWeight = w;
        t.SetResourceReference(TextBlock.ForegroundProperty, fgKey);
        return t;
    }

    public static SymbolIcon Icon(string name, double size = 16, string? fgKey = null)
    {
        var icon = new SymbolIcon { Symbol = IconNames.Resolve(name), FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        if (fgKey is not null) icon.SetResourceReference(Control.ForegroundProperty, fgKey);
        return icon;
    }

    public static SymbolIcon Icon(SymbolRegular symbol, double size = 16, string? fgKey = null)
    {
        var icon = new SymbolIcon { Symbol = symbol, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        if (fgKey is not null) icon.SetResourceReference(Control.ForegroundProperty, fgKey);
        return icon;
    }

    /// <summary>Rounded square with a white icon on the category color.</summary>
    public static Border IconChip(string iconName, string colorKey, double size = 32, double iconSize = 18, double radius = 8)
    {
        var icon = Icon(iconName, iconSize);
        icon.Foreground = HighContrast.OnChip;
        var chip = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(radius),
            Background = Palette.Fill(colorKey), Child = icon,
        };
        if (radius >= 8) chip.SetResourceReference(Border.CornerRadiusProperty, "DsRadiusCard");
        return chip;
    }

    public static Wpf.Ui.Controls.Button IconButton(SymbolRegular symbol, string tooltip, Action click, double size = 30, double iconSize = 16)
    {
        var b = new Wpf.Ui.Controls.Button
        {
            Icon = Icon(symbol, iconSize), Appearance = ControlAppearance.Transparent,
            Width = size, Height = size, Padding = new Thickness(0), ToolTip = tooltip, VerticalAlignment = VerticalAlignment.Center,
        };
        System.Windows.Automation.AutomationProperties.SetName(b, tooltip);
        b.Click += (_, _) => click();
        return b;
    }

    public static Wpf.Ui.Controls.Button TextButton(string text, Action click, SymbolRegular? icon = null, ControlAppearance appearance = ControlAppearance.Secondary)
    {
        var b = new Wpf.Ui.Controls.Button { Content = text, Appearance = appearance, Padding = new Thickness(10, 4, 10, 4) };
        if (icon is { } i) b.Icon = Icon(i, 14);
        b.Click += (_, _) => click();
        return b;
    }

    public static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T t) return t;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    public static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var n = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) yield return t;
            foreach (var d in Descendants<T>(c)) yield return d;
        }
    }

    public static Border Divider() => new Border { Height = 1, Margin = new Thickness(0, 6, 0, 6) }.Dyn(Border.BackgroundProperty, "DsDividerBrush");
}
