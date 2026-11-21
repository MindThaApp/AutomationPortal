using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutomationPortal.App.Infrastructure;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Dialogs;

/// <summary>Colour swatches plus a small grid of icons. Used by the fallback editor and the folder dialogs.</summary>
public sealed class IconColorPanel : StackPanel
{
    private static readonly string[] Candidates =
    {
        "Flash24", "Globe24", "Home24", "Mail24", "Folder24", "Document24", "Image24", "Camera24", "Calendar24", "Clock24",
        "Alert24", "Bookmark24", "Star24", "Heart24", "Person24", "People24", "Settings24", "Search24", "Link24", "Cloud24",
        "WeatherSunny24", "Lightbulb24", "Phone24", "Laptop24", "Desktop24", "Print24", "Save24", "Delete24", "Edit24", "Copy24",
        "ClipboardPaste24", "Play24", "Speaker224", "Mic24", "Video24", "Wifi124", "Bluetooth24", "Battery824", "Rocket24", "Gift24",
        "Briefcase24", "Book24", "Games24", "Music24", "Shield24", "Key24", "Lock24", "Timer24", "Tag24", "Cart24",
        "Money24", "Food24", "VehicleCar24", "Airplane24", "Building24", "Translate24", "TextT24", "Code24", "Backpack24", "Notebook24",
    };

    private readonly WrapPanel _icons = new();
    private readonly WrapPanel _colors = new();
    private readonly Border _preview;

    public string Icon { get; private set; }
    public string Color { get; private set; }

    public event EventHandler? Changed;

    public IconColorPanel(string icon, string color)
    {
        Icon = Icons.IsKnown(icon) ? icon : "Flash24";
        Color = Palette.Keys.Contains(color) ? color : "blue";

        _preview = Ui.Chip(Icon, Color, 56, 28);
        _preview.HorizontalAlignment = HorizontalAlignment.Left;
        _preview.Margin = new Thickness(0, 0, 0, 12);
        Children.Add(_preview);

        foreach (var key in Palette.Keys)
        {
            var swatch = new RadioButton
            {
                GroupName = "colors", Tag = key, IsChecked = key == Color, Margin = new Thickness(0, 0, 6, 6), ToolTip = char.ToUpper(key[0]) + key[1..],
                Width = 28, Height = 28, Padding = new Thickness(0),
                Template = SwatchTemplate(key),
            };
            swatch.Checked += (_, _) => { Color = key; Refresh(); };
            _colors.Children.Add(swatch);
        }
        Children.Add(_colors);

        var grid = new System.Windows.Controls.ScrollViewer { MaxHeight = 190, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 0), Content = _icons };
        foreach (var name in Candidates.Where(Icons.IsKnown))
        {
            var b = new RadioButton
            {
                GroupName = "icons", Tag = name, IsChecked = name == Icon, Margin = new Thickness(0, 0, 4, 4), ToolTip = name.Replace("24", ""),
                Width = 40, Height = 40, Template = IconTemplate(name),
            };
            b.Checked += (_, _) => { Icon = name; Refresh(); };
            _icons.Children.Add(b);
        }
        Children.Add(grid);
    }

    private void Refresh()
    {
        if (AutomationPortal.Editor.HighContrast.Active) _preview.Background = SystemColors.HighlightBrush;
        else _preview.SetResourceReference(Border.BackgroundProperty, Palette.CategoryResourceKey(Color));
        if (_preview.Child is SymbolIcon s) s.Symbol = Icons.Parse(Icon);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal static ControlTemplate SwatchTemplate(string key)
    {
        var t = new ControlTemplate(typeof(RadioButton));
        var grid = new FrameworkElementFactory(typeof(Grid));
        var fill = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse), "fill");
        if (AutomationPortal.Editor.HighContrast.Active) fill.SetValue(System.Windows.Shapes.Shape.FillProperty, SystemColors.HighlightBrush);
        else fill.SetValue(System.Windows.Shapes.Shape.FillProperty, new DynamicResourceExtension(Palette.CategoryResourceKey(key)));
        var ring = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse), "ring");
        ring.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 2.5);
        ring.SetValue(System.Windows.Shapes.Shape.StrokeProperty, Brushes.Transparent);
        ring.SetValue(FrameworkElement.MarginProperty, new Thickness(-1));
        grid.AppendChild(fill);
        grid.AppendChild(ring);
        t.VisualTree = grid;
        var checkedTrigger = new Trigger { Property = ToggleButton_IsChecked, Value = true };
        checkedTrigger.Setters.Add(new Setter(System.Windows.Shapes.Shape.StrokeProperty, new DynamicResourceExtension("DsTextPrimaryBrush"), "ring"));
        t.Triggers.Add(checkedTrigger);
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(System.Windows.Shapes.Shape.StrokeProperty, new DynamicResourceExtension("DsTextSecondaryBrush"), "ring"));
        t.Triggers.Insert(0, hover);
        return t;
    }

    private static readonly DependencyProperty ToggleButton_IsChecked = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty;

    private static ControlTemplate IconTemplate(string name)
    {
        var t = new ControlTemplate(typeof(RadioButton));
        var border = new FrameworkElementFactory(typeof(Border), "bd");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var icon = new FrameworkElementFactory(typeof(SymbolIcon));
        icon.SetValue(SymbolIcon.SymbolProperty, Icons.Parse(name));
        icon.SetValue(Control.FontSizeProperty, 20.0);
        icon.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(icon);
        t.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("ControlFillColorSecondaryBrush"), "bd"));
        var on = new Trigger { Property = ToggleButton_IsChecked, Value = true };
        on.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("DsAccentBrush"), "bd"));
        t.Triggers.Add(hover);
        t.Triggers.Add(on);
        return t;
    }
}
