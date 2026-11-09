using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace AutomationPortal.Editor;

/// <summary>
/// Makes sure the editor's styles and the WPF-UI Fluent dictionaries are available. The app normally merges the WPF-UI
/// dictionaries itself; if it didn't, they are added here (system theme).
/// </summary>
public static class EditorResources
{
    private static bool _done;

    public static void Ensure()
    {
        if (_done) return;
        var app = Application.Current;
        if (app is null) return;
        var merged = app.Resources.MergedDictionaries;
        if (app.TryFindResource("ApplicationBackgroundBrush") is null)
        {
            merged.Add(new ThemesDictionary { Theme = ApplicationTheme.Light });
            merged.Add(new ControlsDictionary());
            try { ApplicationThemeManager.ApplySystemTheme(false); } catch { }
        }
        merged.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/AutomationPortal.Editor;component/Resources/EditorResources.xaml", UriKind.Absolute) });
        _done = true;
    }
}
