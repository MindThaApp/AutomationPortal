using System.Windows;
using AutomationPortal.Core;

namespace AutomationPortal.Editor;

/// <summary>WPF side of <see cref="WindowActivation"/>: window helpers that honour the no-activate switch.</summary>
public static class WpfWindowActivation
{
    /// <summary>Activates the window unless activation is suppressed.</summary>
    public static void TryActivate(Window window) => WindowActivation.Run(() => window.Activate());

    /// <summary>
    /// Makes every Window show without activation while the switch is on, whatever sets ShowActivated.
    /// Call once at startup before any window exists.
    /// </summary>
    public static void Install()
    {
        Window.ShowActivatedProperty.OverrideMetadata(typeof(Window), new FrameworkPropertyMetadata(
            true, null, (_, value) => WindowActivation.Suppressed ? false : value));
    }
}
