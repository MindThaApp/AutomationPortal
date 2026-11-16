using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace AutomationPortal.Editor;

/// <summary>
/// Gives controls a screen-reader name when none was set: an icon-only button or a switch takes its tooltip, a text box
/// takes its placeholder or tooltip. Controls that already have a name or visible text are left alone.
/// </summary>
public static class AccessibleNames
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        foreach (var type in new[] { typeof(ButtonBase), typeof(System.Windows.Controls.TextBox), typeof(PasswordBox), typeof(ComboBox) })
            EventManager.RegisterClassHandler(type, FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        try { Apply(fe); } catch { }
    }

    internal static void Apply(FrameworkElement fe)
    {
        if (!string.IsNullOrWhiteSpace(AutomationProperties.GetName(fe))) return;
        if (fe is ContentControl { Content: string { Length: > 0 } }) return;
        string? name = null;
        if (fe is Wpf.Ui.Controls.TextBox ui && !string.IsNullOrWhiteSpace(ui.PlaceholderText)) name = ui.PlaceholderText;
        name ??= fe.ToolTip as string;
        if (!string.IsNullOrWhiteSpace(name)) AutomationProperties.SetName(fe, name);
    }
}
