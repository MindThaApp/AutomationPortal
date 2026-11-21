using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.Core;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Dialogs;

/// <summary>Builds the rich "Show Result" / "Preview" view: text, images, lists, dictionaries and files.</summary>
public static class ResultView
{
    public static FrameworkElement Build(ContentValue value)
    {
        if (value.IsEmpty)
            return Ui.Text("There's nothing to show.", secondary: true);

        // One block of text: show it large and selectable.
        if (value.Count == 1 && value[0] is TextItem or NumberItem or BooleanItem or DateItem or UrlItem)
        {
            var box = new System.Windows.Controls.TextBox
            {
                Text = value.AsText(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
                BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = 15,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 60, MaxHeight = 420,
            };
            return box;
        }

        var stack = new StackPanel();
        var index = 0;
        foreach (var item in value.Items.Take(200))
        {
            var row = BuildItem(item);
            row.Margin = new Thickness(0, index++ == 0 ? 0 : 8, 0, 0);
            stack.Children.Add(row);
        }
        if (value.Count > 200)
            stack.Children.Add(Ui.Text($"…and {value.Count - 200} more", secondary: true));
        return new ScrollViewer { Content = stack, MaxHeight = 460, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static FrameworkElement Card(UIElement child)
    {
        var b = new Border { CornerRadius = AutomationPortal.Editor.Radius.Card, Padding = new Thickness(12, 10, 12, 10), BorderThickness = new Thickness(1), Child = child };
        b.SetResourceReference(Border.BackgroundProperty, "DsCardBrush");
        b.SetResourceReference(Border.BorderBrushProperty, "DsStrokeBrush");
        return b;
    }

    private static FrameworkElement BuildItem(ContentItem item)
    {
        switch (item)
        {
            case ImageItem img: return Card(BuildImage(img));
            case PathItem p: return Card(BuildFile(p));
            case DictionaryItem d: return Card(BuildDictionary(d));
            default:
                var t = Ui.Text(item.DisplayText, size: 14);
                t.SetValue(System.Windows.Documents.TextElement.FontSizeProperty, 14.0);
                return Card(t);
        }
    }

    private static UIElement BuildImage(ImageItem img)
    {
        var panel = new StackPanel();
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 720;
            bmp.UriSource = new Uri(img.Path);
            bmp.EndInit();
            bmp.Freeze();
            panel.Children.Add(new System.Windows.Controls.Image { Source = bmp, MaxHeight = 360, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left });
        }
        catch { panel.Children.Add(Ui.Text(Strings.Get("ResultView_ThisPictureCouldntBe"), secondary: true)); }
        panel.Children.Add(Ui.Text(img.FullName, secondary: true, size: 12));
        return panel;
    }

    private static UIElement BuildFile(PathItem p)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Ui.Icon(p is FolderItem ? "Folder24" : "Document24", 24);
        icon.Margin = new Thickness(0, 0, 12, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(icon);

        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Ui.Text(p.FullName.Length > 0 ? p.FullName : p.Path, wrap: false));
        names.Children.Add(Ui.Text(p.Path, secondary: true, size: 12, wrap: false));
        Grid.SetColumn(names, 1);
        grid.Children.Add(names);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var open = new Wpf.Ui.Controls.Button { Content = "Open", Margin = new Thickness(8, 0, 0, 0), ToolTip = Strings.Get("ResultView_OpenItWithThe") };
        open.Click += (_, _) => Launch(p.Path);
        var show = new Wpf.Ui.Controls.Button { Content = Strings.Get("ResultView_ShowInFolder"), Margin = new Thickness(8, 0, 0, 0) };
        show.Click += (_, _) => Reveal(p);
        buttons.Children.Add(open);
        buttons.Children.Add(show);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
        return grid;
    }

    private static UIElement BuildDictionary(DictionaryItem d)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "k" });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var r = 0;
        foreach (var (k, v) in d.Entries)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var key = Ui.Text(k, secondary: true, size: 13);
            key.Margin = new Thickness(0, 2, 16, 2);
            key.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetRow(key, r);
            var val = Ui.Text(v.AsText(", "), size: 14);
            val.Margin = new Thickness(0, 2, 0, 2);
            Grid.SetRow(val, r); Grid.SetColumn(val, 1);
            grid.Children.Add(key); grid.Children.Add(val);
            r++;
        }
        if (d.Count == 0) return Ui.Text("(empty)", secondary: true);
        return grid;
    }

    private static void Launch(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception) { /* the file may be gone; nothing useful to tell the user in a preview */ }
    }

    private static void Reveal(PathItem p)
    {
        try
        {
            if (p is FolderItem && Directory.Exists(p.Path)) Process.Start(new ProcessStartInfo(p.Path) { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{p.Path}\"") { UseShellExecute = true });
        }
        catch (Exception) { }
    }
}
