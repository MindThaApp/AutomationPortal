using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Infrastructure;

/// <summary>Tiny helpers for building Fluent UI in code.</summary>
public static class Ui
{
    public static System.Windows.Controls.TextBlock Text(string text, bool secondary = false, double size = 14, FontWeight? weight = null, bool wrap = true)
    {
        var t = new System.Windows.Controls.TextBlock
        {
            Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
        };
        t.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, secondary ? "DsTextSecondaryBrush" : "DsTextPrimaryBrush");
        return t;
    }

    public static StackPanel Labeled(string label, FrameworkElement field, double bottom = 12)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 0, bottom) };
        var l = Text(label, secondary: true, size: 12);
        l.Margin = new Thickness(0, 0, 0, 4);
        p.Children.Add(l);
        p.Children.Add(field);
        return p;
    }

    public static SymbolIcon Icon(string name, double size = 20, Brush? foreground = null)
    {
        var i = new SymbolIcon(Icons.Parse(name)) { FontSize = size };
        if (foreground is not null) i.Foreground = foreground;
        return i;
    }

    /// <summary>A solid category-colour square with a white glyph (high contrast: the system highlight).</summary>
    public static Border Chip(string iconName, string colorKey, double size = 34, double iconSize = 18)
    {
        var chip = new Border
        {
            Width = size, Height = size, CornerRadius = AutomationPortal.Editor.Radius.Card,
            Child = new SymbolIcon(Icons.Parse(iconName)) { FontSize = iconSize, Foreground = AutomationPortal.Editor.HighContrast.OnChip, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        chip.SetResourceReference(Border.CornerRadiusProperty, "DsRadiusCard");
        if (AutomationPortal.Editor.HighContrast.Active) chip.Background = SystemColors.HighlightBrush;
        else chip.SetResourceReference(Border.BackgroundProperty, Palette.CategoryResourceKey(colorKey));
        return chip;
    }

    public static T? FindAncestor<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T t) return t;
            child = child is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(child) : LogicalTreeHelper.GetParent(child);
        }
        return null;
    }
}
