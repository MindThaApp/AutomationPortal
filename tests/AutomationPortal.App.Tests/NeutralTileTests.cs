using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;
using AutomationPortal.App.Controls;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.ViewModels;
using AutomationPortal.Core;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;
using Xunit;

namespace AutomationPortal.App.Tests;

[Collection("Wpf application")]
public class NeutralTileTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string Editor = "pack://application:,,,/AutomationPortal.Editor;component/Resources/";

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "AutomationPortal.App"))) return dir.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }

    private static string Src(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root(), "src", "AutomationPortal.App" }.Concat(parts).ToArray()));

    private static void OnSta(Action body) => SharedSta.Run(body);

    private static void NewApp()
    {
        _ = typeof(AutomationPortal.App.Services.ThemeService).Assembly;
        _ = typeof(AutomationPortal.Editor.Motion).Assembly;
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var d = new ResourceDictionary();
        d.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Light });
        d.MergedDictionaries.Add(new ControlsDictionary());
        d.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(Editor + "Tokens.xaml", UriKind.Absolute) });
        d.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(Editor + "Tokens.Light.xaml", UriKind.Absolute) });
        d.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/AutomationPortal;component/Resources/Styles.xaml", UriKind.Absolute) });
        app.Resources = d;
    }

    private static ShortcutTile NewTile(out List<string> raised)
    {
        NewApp();
        var log = new List<string>();
        var tile = new ShortcutTile { DataContext = new ShortcutTileViewModel(new Shortcut { Name = "Tidy downloads", Description = "Sorts the folder", Color = "mint" }, explicitActions: true) };
        tile.RunRequested += (_, _) => log.Add("run");
        tile.SelectRequested += (_, _) => log.Add("select");
        tile.EditRequested += (_, _) => log.Add("edit");
        tile.Measure(new Size(182, 156));
        tile.Arrange(new Rect(0, 0, 182, 156));
        tile.UpdateLayout();
        raised = log;
        return tile;
    }

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public void A_single_click_selects_the_tile_and_does_not_run_it()
    {
        OnSta(() =>
        {
            var tile = NewTile(out var log);
            Click(tile.Button);
            Assert.Equal(new[] { "select" }, log);
        });
    }

    [Fact]
    public void Run_button_Enter_and_double_click_all_ask_for_a_run()
    {
        OnSta(() =>
        {
            var tile = NewTile(out var log);
            var run = (Wpf.Ui.Controls.Button)tile.FindName("RunButton");
            Click(run);
            Assert.Contains("run", log);

            log.Clear();
            using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("tile-test"));
            var enter = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = tile.Button };
            tile.Button.RaiseEvent(enter);
            Assert.Contains("run", log);

            log.Clear();
            var dbl = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = tile.Button };
            tile.Button.RaiseEvent(dbl);
            Assert.Contains("run", log);
        });
    }

    [Fact]
    public void Edit_button_asks_for_an_edit_and_never_a_run()
    {
        OnSta(() =>
        {
            var tile = NewTile(out var log);
            Click((Wpf.Ui.Controls.Button)tile.FindName("EditButton"));
            Assert.Contains("edit", log);
            Assert.DoesNotContain("run", log);
        });
    }

    [Fact]
    public void Run_and_Edit_buttons_are_visible_at_rest_and_named_after_the_shortcut()
    {
        OnSta(() =>
        {
            var tile = NewTile(out _);
            var run = (Wpf.Ui.Controls.Button)tile.FindName("RunButton");
            var edit = (Wpf.Ui.Controls.Button)tile.FindName("EditButton");
            Assert.Equal(Visibility.Visible, ((UIElement)tile.FindName("Actions")).Visibility);
            Assert.Equal(1.0, run.Opacity);
            Assert.Equal(1.0, edit.Opacity);
            Assert.Equal("Run Tidy downloads", AutomationProperties.GetName(run));
            Assert.Equal("Edit Tidy downloads", AutomationProperties.GetName(edit));
            Assert.Equal("More options for Tidy downloads", AutomationProperties.GetName((Button)tile.FindName("MoreButton")));
        });
    }

    [Fact]
    public void The_tile_reads_as_a_sentence_and_exposes_its_selection_state()
    {
        OnSta(() =>
        {
            var tile = NewTile(out _);
            Assert.Equal("Tidy downloads. Sorts the folder. No file changes", ((ShortcutTileViewModel)tile.DataContext).AutomationName);
            Assert.Equal("Tidy downloads. Sorts the folder. No file changes", AutomationProperties.GetName(tile.Button));
            var peer = UIElementAutomationPeer.CreatePeerForElement(tile.Button);
            var item = (ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)!;
            Assert.False(item.IsSelected);
            ((ShortcutTileViewModel)tile.DataContext).IsSelected = true;
            Assert.True(item.IsSelected);
        });
    }

    [Fact]
    public void The_library_sends_every_tile_run_through_the_run_gate()
    {
        var code = Src("Pages", "LibraryPage.xaml.cs");
        var start = code.IndexOf("private void OnTileRun", StringComparison.Ordinal);
        Assert.True(start > 0);
        var body = code.Substring(start, code.IndexOf("\n    }", start, StringComparison.Ordinal) - start);
        Assert.Contains("TileRunGate.Start(", body);
        var xaml = Src("Pages", "LibraryPage.xaml");
        Assert.Contains("RunRequested=\"OnTileRun\"", xaml);
        Assert.DoesNotContain("Runs.Toggle", code.Substring(0, start));
    }

    [Fact]
    public void Every_palette_colour_maps_to_one_of_the_ten_category_tokens()
    {
        var tokens = XDocument.Load(Path.Combine(Root(), "src", "AutomationPortal.Editor", "Resources", "Tokens.xaml")).Descendants()
            .Select(e => (string?)e.Attribute(X + "Key")).Where(k => k is not null && k.StartsWith("DsCategory") && k.EndsWith("Brush")).ToHashSet();
        Assert.Equal(10, tokens.Count);
        foreach (var key in Palette.Keys) Assert.Contains(Palette.CategoryResourceKey(key), tokens);
        Assert.Contains(Palette.CategoryResourceKey(null), tokens);
        Assert.Contains(Palette.CategoryResourceKey("not-a-colour"), tokens);
    }

    [Fact]
    public void Old_colours_keep_their_hue_family()
    {
        Assert.Equal("DsCategoryRedBrush", Palette.CategoryResourceKey("red"));
        Assert.Equal("DsCategoryGreenBrush", Palette.CategoryResourceKey("green"));
        Assert.Equal("DsCategoryBlueBrush", Palette.CategoryResourceKey("blue"));
        Assert.Equal("DsCategoryVioletBrush", Palette.CategoryResourceKey("purple"));
    }

    [Theory]
    [InlineData("Controls", "ShortcutTile.xaml")]
    [InlineData("Controls", "ShortcutTile.xaml.cs")]
    [InlineData("Pages", "GalleryPage.xaml")]
    [InlineData("Pages", "GalleryPage.xaml.cs")]
    [InlineData("Shell", "QuickPanelWindow.xaml")]
    [InlineData("ViewModels", "ShortcutTileViewModel.cs")]
    public void No_gradient_brushes_remain_on_tiles_and_gallery_cards(string folder, string file)
    {
        var text = Src(folder, file);
        Assert.DoesNotContain("LinearGradientBrush", text);
        Assert.DoesNotContain("RadialGradientBrush", text);
        Assert.DoesNotContain("Palette.Gradient", text);
    }

    [Fact]
    public void The_tile_style_is_a_card_not_a_gradient()
    {
        var styles = Src("Resources", "Styles.xaml");
        var start = styles.IndexOf("x:Key=\"FlowTileButton\"", StringComparison.Ordinal);
        var body = styles.Substring(start, styles.IndexOf("</Style>", start, StringComparison.Ordinal) - start);
        Assert.DoesNotContain("Gradient", body);
        Assert.Contains("DsCardBrush", body);
        Assert.Contains("DsRadiusCard", body);
    }
}
