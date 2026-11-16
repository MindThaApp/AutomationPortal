using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutomationPortal.Editor.Logic;

namespace AutomationPortal.Editor.UI;

/// <summary>The colored variable pills used in cards, token text boxes and pickers.</summary>
internal static class Pills
{
    public static Border Create(VarDisplay d, double fontSize = 12.5)
    {
        var pill = new Border
        {
            CornerRadius = new CornerRadius(11), Padding = new Thickness(7, 1, 9, 2), Margin = new Thickness(1, 0, 1, 0),
            BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Orientation = Orientation.Horizontal },
        };
        Update(pill, d, fontSize);
        return pill;
    }

    public static void Update(Border pill, VarDisplay d, double fontSize = 12.5)
    {
        var color = d.Missing ? "red" : d.ColorKey;
        pill.Background = Palette.Tint(color, 0x30);
        pill.BorderBrush = HighContrast.Active ? Palette.Solid(color) : Palette.Tint(color, 0x80);
        pill.ToolTip = d.Missing ? "This step no longer exists. Click to pick another variable." : d.Label;
        var panel = (StackPanel)pill.Child;
        panel.Children.Clear();
        var icon = Ui.Icon(d.Icon, fontSize);
        icon.Foreground = Palette.Solid(color);
        icon.Margin = new Thickness(0, 1, 5, 0);
        panel.Children.Add(icon);
        var label = Ui.Text(d.Label, Ui.Primary, fontSize);
        label.TextTrimming = TextTrimming.None;
        panel.Children.Add(label);
    }

    public static Border Ask(double fontSize = 12.5)
    {
        var pill = new Border
        {
            CornerRadius = new CornerRadius(11), Padding = new Thickness(7, 1, 9, 2), Margin = new Thickness(1, 0, 1, 0),
            BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Cursor = System.Windows.Input.Cursors.Hand,
            Background = Palette.Tint("indigo", 0x30), BorderBrush = Palette.Tint("indigo", 0x80),
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = Ui.Icon("QuestionCircle24", fontSize);
        icon.Foreground = Palette.Solid("indigo");
        icon.Margin = new Thickness(0, 1, 5, 0);
        panel.Children.Add(icon);
        panel.Children.Add(Ui.Text("Ask each time", Ui.Primary, fontSize));
        pill.Child = panel;
        pill.ToolTip = Strings.Get("Pills_YouWillBeAsked");
        return pill;
    }

    public static Border ColorSwatch(string hex)
    {
        Brush fill = Brushes.Transparent;
        try { fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); } catch { }
        return new Border
        {
            Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = fill, BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x80)), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center,
        };
    }
}
