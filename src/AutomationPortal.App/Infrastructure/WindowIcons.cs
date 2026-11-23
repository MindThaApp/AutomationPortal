using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace AutomationPortal.App.Infrastructure;

/// <summary>
/// Gives every WPF window the app icon when it has none of its own, so the taskbar button and its hover
/// preview show the app icon instead of the generic Windows one. Windows that set an Icon keep it.
/// </summary>
public static class WindowIcons
{
    private const string IconUri = "pack://application:,,,/AutomationPortal;component/Assets/automationportal.ico";

    /// <summary>Call once, from Application.OnStartup, before any window is created.</summary>
    public static void Register()
    {
        var icon = BitmapFrame.Create(new Uri(IconUri, UriKind.Absolute));
        icon.Freeze();

        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => { if (s is Window w && w.Icon is null) w.Icon = icon; }));
    }
}
