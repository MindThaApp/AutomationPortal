using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutomationPortal.Editor.UI;
using Wpf.Ui.Controls;

namespace AutomationPortal.Editor;

/// <summary>Pick a shortcut's colour (15 palette colours) and icon (a searchable grid of curated icons).</summary>
public static class IconPickerDialog
{
    /// <summary>Shows the picker. Returns the chosen icon name and colour key, or null when cancelled.</summary>
    public static (string Icon, string Color)? Show(Window owner, string icon, string color)
    {
        EditorResources.Ensure();
        var w = new IconPickerWindow(IconNames.Normalize(icon), Palette.IsValid(color) ? color : "blue") { Owner = owner };
        return w.ShowDialog() == true ? (w.SelectedIcon, w.SelectedColor) : null;
    }
}

internal sealed class IconPickerWindow : FluentWindow
{
    private readonly Border _preview;
    private readonly WrapPanel _swatches = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly WrapPanel _grid = new();
    private readonly Wpf.Ui.Controls.TextBox _search;
    private readonly TextBlock _empty;
    private readonly List<(string Name, string Words, Border Cell)> _cells = new();

    public string SelectedIcon { get; private set; }
    public string SelectedColor { get; private set; }

    public IconPickerWindow(string icon, string color)
    {
        SelectedIcon = icon;
        SelectedColor = color;
        Title = "Choose an icon";
        Width = 580;
        Height = 700;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowBackdropType = WindowBackdropType.Mica;
        ExtendsContentIntoTitleBar = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        FontFamily = Ui.Font;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TitleBar { Title = "Choose an icon", ShowMaximize = false, ShowMinimize = false });

        // preview + colours
        var top = new Grid { Margin = new Thickness(24, 8, 24, 8) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition());
        _preview = new Border { Width = 76, Height = 76, CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Top };
        top.Children.Add(_preview);
        var colours = new StackPanel();
        colours.Children.Add(Ui.Text("Colour", Ui.Secondary, 12, FontWeights.SemiBold));
        colours.Children.Add(_swatches);
        Grid.SetColumn(colours, 1);
        top.Children.Add(colours);
        Grid.SetRow(top, 1);
        root.Children.Add(top);

        // search + grid
        var middle = new Grid { Margin = new Thickness(24, 0, 24, 0) };
        middle.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        middle.RowDefinitions.Add(new RowDefinition());
        _search = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Search icons (for example sun, mail, music)", Margin = new Thickness(0, 0, 0, 10), ClearButtonEnabled = true };
        _search.TextChanged += (_, _) => Filter();
        middle.Children.Add(_search);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _grid, Focusable = false };
        Grid.SetRow(scroll, 1);
        middle.Children.Add(scroll);
        _empty = new TextBlock { Text = "No icons match. Try a simpler word.", Visibility = Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 0) };
        _empty.SetResourceReference(TextBlock.ForegroundProperty, Ui.Secondary);
        Grid.SetRow(_empty, 1);
        middle.Children.Add(_empty);
        Grid.SetRow(middle, 2);
        root.Children.Add(middle);

        // buttons
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 14, 24, 18) };
        var cancel = Ui.TextButton("Cancel", () => { DialogResult = false; });
        cancel.IsCancel = true;
        var ok = Ui.TextButton("Choose", () => { DialogResult = true; }, null, ControlAppearance.Primary);
        ok.IsDefault = true;
        ok.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);
        Content = root;

        var names = IconNames.Curated.ToList();
        if (!names.Contains(icon)) names.Insert(0, icon);
        foreach (var name in names) AddCell(name);
        foreach (var key in Palette.Keys) AddSwatch(key);
        Refresh();
        Loaded += (_, _) => _search.Focus();
    }

    private void AddSwatch(string key)
    {
        var ring = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Margin = new Thickness(0, 0, 8, 8), BorderThickness = new Thickness(2), Cursor = Cursors.Hand, Tag = key, ToolTip = char.ToUpper(key[0]) + key[1..] };
        ring.Child = new Border { CornerRadius = new CornerRadius(12), Margin = new Thickness(3), Background = Palette.Gradient(key) };
        ring.MouseLeftButtonUp += (_, _) => { SelectedColor = key; Refresh(); };
        System.Windows.Automation.AutomationProperties.SetName(ring, key);
        _swatches.Children.Add(ring);
    }

    private void AddCell(string name)
    {
        var cell = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 6, 6), BorderThickness = new Thickness(2), Cursor = Cursors.Hand, Tag = name, ToolTip = IconNames.Words(name) };
        cell.Child = Ui.Icon(name, 24, Ui.Primary);
        cell.MouseLeftButtonUp += (_, e) =>
        {
            SelectedIcon = name;
            Refresh();
            if (e.ClickCount == 2) DialogResult = true;
        };
        cell.MouseEnter += (_, _) => { if (SelectedIcon != name) cell.Dyn(Border.BackgroundProperty, Ui.SubtleHover); };
        cell.MouseLeave += (_, _) => { if (SelectedIcon != name) cell.Background = Brushes.Transparent; };
        System.Windows.Automation.AutomationProperties.SetName(cell, IconNames.Words(name));
        _cells.Add((name, IconNames.Words(name), cell));
        _grid.Children.Add(cell);
    }

    private void Filter()
    {
        var words = _search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var shown = 0;
        foreach (var (name, text, cell) in _cells)
        {
            var visible = words.All(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
            cell.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible) shown++;
        }
        _empty.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Refresh()
    {
        _preview.Background = Palette.Gradient(SelectedColor);
        var icon = Ui.Icon(SelectedIcon, 40);
        icon.Foreground = Brushes.White;
        _preview.Child = icon;
        foreach (Border ring in _swatches.Children)
        {
            var on = (string)ring.Tag == SelectedColor;
            if (on) ring.SetResourceReference(Border.BorderBrushProperty, "AccentFillColorDefaultBrush"); else ring.BorderBrush = Brushes.Transparent;
        }
        foreach (var (name, _, cell) in _cells)
        {
            var on = name == SelectedIcon;
            if (on)
            {
                cell.SetResourceReference(Border.BorderBrushProperty, "AccentFillColorDefaultBrush");
                cell.SetResourceReference(Border.BackgroundProperty, Ui.SubtleHover);
            }
            else { cell.BorderBrush = Brushes.Transparent; cell.Background = Brushes.Transparent; }
        }
    }
}
