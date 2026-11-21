using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;
using Xunit;

namespace AutomationPortal.App.Tests;

[Collection("Wpf application")]
public class XamlResourceLoadTests
{
    private const string Editor = "pack://application:,,,/AutomationPortal.Editor;component/Resources/";

    private static void OnSta(Action body) => SharedSta.Run(body);

    private static Application NewApp()
    {
        // Pack URIs only find assemblies that are already loaded.
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
        return app;
    }

    [Fact]
    public void App_styles_and_all_token_dictionaries_load_and_the_shared_focus_ring_applies()
    {
        OnSta(() =>
        {
            var app = NewApp();
            foreach (var name in new[] { "Tokens.Light.xaml", "Tokens.Dark.xaml", "Tokens.HighContrast.xaml" })
                Assert.IsType<SolidColorBrush>(new ResourceDictionary { Source = new Uri(Editor + name, UriKind.Absolute) }["DsTextPrimaryBrush"]);
            foreach (var key in new[] { "FlowTileButton", "GhostIconButton", "PlainTileButton", "BigCardButton", "TokenButton" })
            {
                var b = new Button { Style = (Style)app.Resources[key] };
                b.ApplyTemplate();
                Assert.NotNull(b.FocusVisualStyle);
                Assert.Contains(b.FocusVisualStyle!.Setters.OfType<Setter>(), s => s.Property == Control.TemplateProperty);
            }
            Assert.IsType<Style>(app.Resources["DefaultControlFocusVisualStyle"]);
        });
    }

    [Fact]
    public void Switching_the_theme_swaps_the_token_dictionary_and_follows_the_accent()
    {
        OnSta(() =>
        {
            var app = NewApp();
            AutomationPortal.Editor.DesignTokens.Attach(app);
            ApplicationThemeManager.Apply(ApplicationTheme.Dark, updateAccent: false);
            Assert.Equal(Colors.White, ((SolidColorBrush)app.Resources["DsTextPrimaryBrush"]).Color);
            ApplicationThemeManager.Apply(ApplicationTheme.Light, updateAccent: false);
            Assert.Equal(Color.FromArgb(0xE6, 0, 0, 0), ((SolidColorBrush)app.Resources["DsTextPrimaryBrush"]).Color);
            Assert.NotNull(app.Resources["DsAccentBrush"]);
        });
    }
}
