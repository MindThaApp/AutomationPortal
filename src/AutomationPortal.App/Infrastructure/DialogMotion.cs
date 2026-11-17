using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using AutomationPortal.Editor;

namespace AutomationPortal.App.Infrastructure;

/// <summary>
/// The shared open and close motion of the app's dialogs: fade in while growing from 97% (Enter role), and a short fade
/// when closing (Leave role). Both are instant when Windows "Animation effects" is off, and closing never waits during shutdown.
/// </summary>
public static class DialogMotion
{
    public const double StartScale = 0.97;

    public static void Attach(Window window)
    {
        var closing = false;
        window.Loaded += (_, _) => PlayOpen(window);
        window.Closing += (_, e) =>
        {
            if (closing || e.Cancel || Motion.Reduced || !window.IsVisible || Application.Current is not { } app || app.Dispatcher.HasShutdownStarted) return;
            closing = true;
            e.Cancel = true;
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(Motion.Nominal(MotionRole.Leave))) { EasingFunction = Motion.EaseIn };
            fade.Completed += (_, _) => { try { window.Close(); } catch (InvalidOperationException) { } };
            window.BeginAnimation(UIElement.OpacityProperty, fade);
        };
    }

    private static void PlayOpen(Window window)
    {
        if (Motion.Reduced || window.Content is not FrameworkElement content) return;
        var scale = new ScaleTransform(StartScale, StartScale);
        content.RenderTransformOrigin = new Point(0.5, 0.5);
        content.RenderTransform = scale;
        Motion.Animate(scale, ScaleTransform.ScaleXProperty, StartScale, 1, MotionRole.Enter);
        Motion.Animate(scale, ScaleTransform.ScaleYProperty, StartScale, 1, MotionRole.Enter);
        // Windows opened through ShowCentered fade themselves in; a plain Show or ShowDialog starts fully visible.
        if (window.Opacity >= 1) Motion.Animate(window, UIElement.OpacityProperty, 0, 1, MotionRole.Enter);
    }
}
