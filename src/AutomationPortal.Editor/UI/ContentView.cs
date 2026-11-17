using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AutomationPortal.Core;
using Wpf.Ui.Controls;

namespace AutomationPortal.Editor.UI;

/// <summary>Shows a <see cref="ContentValue"/> richly: text, images, lists, dictionaries, files, links.</summary>
internal static class ContentView
{
    private const int MaxItems = 100;

    public static FrameworkElement Build(ContentValue value, double maxImageHeight = 220)
    {
        var root = new StackPanel();
        if (value.Count == 0)
        {
            root.Children.Add(Ui.Text(Strings.Get("ContentView_NothingCameOutOf"), Ui.Secondary, 13));
            return root;
        }
        if (value.Count > 1)
        {
            var head = Ui.Text($"{value.Count} items", Ui.Secondary, 12, FontWeights.SemiBold);
            head.Margin = new Thickness(0, 0, 0, 6);
            root.Children.Add(head);
        }
        var shown = 0;
        foreach (var item in value)
        {
            if (shown++ >= MaxItems)
            {
                root.Children.Add(Ui.Text($"…and {value.Count - MaxItems} more", Ui.Secondary, 12));
                break;
            }
            var el = Item(item, maxImageHeight);
            el.Margin = new Thickness(0, 0, 0, 8);
            root.Children.Add(value.Count > 1 ? Bulleted(el) : el);
        }
        return root;
    }

    private static FrameworkElement Bulleted(FrameworkElement inner)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var dot = Ui.Text("•", Ui.Secondary, 14);
        dot.VerticalAlignment = VerticalAlignment.Top;
        dot.Margin = new Thickness(0, 0, 8, 0);
        g.Children.Add(dot);
        Grid.SetColumn(inner, 1);
        g.Children.Add(inner);
        return g;
    }

    private static FrameworkElement Item(ContentItem item, double maxImageHeight)
    {
        switch (item)
        {
            case ImageItem img: return Image(img, maxImageHeight);
            case PathItem path: return Path(path);
            case UrlItem url: return Link(url);
            case DictionaryItem dict: return Dictionary(dict);
            case TextItem text: return SelectableText(text.Value);
            default: return SelectableText(item.DisplayText);
        }
    }

    private static FrameworkElement SelectableText(string text)
    {
        var box = new System.Windows.Controls.TextBox
        {
            Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(2), Margin = new Thickness(-2), BorderBrush = Brushes.Transparent, Background = Brushes.Transparent,
            Padding = new Thickness(0), MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 14, FocusVisualStyle = null,
        };
        box.SetResourceReference(Control.ForegroundProperty, Ui.Primary);
        box.GotKeyboardFocus += (_, _) => box.SetResourceReference(Control.BorderBrushProperty, "TextFillColorPrimaryBrush");
        box.LostKeyboardFocus += (_, _) => box.BorderBrush = Brushes.Transparent;
        return box;
    }

    private static FrameworkElement Image(ImageItem img, double maxHeight)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(img.Path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 640;
            bmp.EndInit();
            bmp.Freeze();
            var stack = new StackPanel();
            stack.Children.Add(new System.Windows.Controls.Image { Source = bmp, MaxHeight = maxHeight, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left });
            var caption = Ui.Text(img.FullName, Ui.Secondary, 12);
            caption.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(caption);
            return stack;
        }
        catch
        {
            return Path(img);
        }
    }

    private static FrameworkElement Path(PathItem p)
    {
        var folder = p is FolderItem;
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Ui.Icon(folder ? SymbolRegular.Folder24 : SymbolRegular.Document24, 22, Ui.Secondary);
        icon.Margin = new Thickness(0, 0, 10, 0);
        g.Children.Add(icon);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Ui.Text(p.FullName, Ui.Primary, 14));
        texts.Children.Add(Ui.Text(p.Path, Ui.Tertiary, 12));
        Grid.SetColumn(texts, 1);
        g.Children.Add(texts);
        var show = Ui.TextButton("Show", () =>
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", p.Exists ? $"/select,\"{p.Path}\"" : $"\"{System.IO.Path.GetDirectoryName(p.Path)}\"") { UseShellExecute = true }); } catch { }
        });
        show.ToolTip = Strings.Get("ContentView_ShowInFileExplorer");
        Grid.SetColumn(show, 2);
        g.Children.Add(show);
        return g;
    }

    private static FrameworkElement Link(UrlItem url)
    {
        var tb = new TextBlock { FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis };
        var link = new Hyperlink(new Run(url.Value.OriginalString)) { NavigateUri = url.Value };
        link.RequestNavigate += (_, e) =>
        {
            try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
            e.Handled = true;
        };
        tb.Inlines.Add(link);
        return tb;
    }

    private static FrameworkElement Dictionary(DictionaryItem dict)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 90, MaxWidth = 200 });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var row = 0;
        foreach (var (key, value) in dict.Entries)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var k = Ui.Text(key, Ui.Secondary, 13, FontWeights.SemiBold);
            k.Margin = new Thickness(0, 2, 14, 2);
            k.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetRow(k, row);
            grid.Children.Add(k);
            var v = Ui.Text(Logic.ValueFormat.Shorten(value.AsText(", "), 200), Ui.Primary, 13, wrap: true);
            v.Margin = new Thickness(0, 2, 0, 2);
            v.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetRow(v, row);
            Grid.SetColumn(v, 1);
            grid.Children.Add(v);
            row++;
            if (row >= 60) break;
        }
        if (row == 0) return Ui.Text("(empty dictionary)", Ui.Secondary, 13);
        return grid;
    }
}
