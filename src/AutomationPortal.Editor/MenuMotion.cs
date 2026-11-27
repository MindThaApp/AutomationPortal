using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace AutomationPortal.Editor;

/// <summary>
/// Menus and drop-downs grow from the corner they are anchored to: a short scale and fade on the motion token.
/// Every ContextMenu gets this through one class handler; the two custom popups call <see cref="Attach"/>.
/// </summary>
public static class MenuMotion
{
    /// <summary>Scale a menu starts from.</summary>
    public const double StartScale = 0.94;

    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(OnMenuOpened));
    }

    /// <summary>Makes a popup's content grow from its anchor each time it opens.</summary>
    public static void Attach(Popup popup)
    {
        popup.PopupAnimation = PopupAnimation.None;
        popup.Opened += (_, _) =>
        {
            if (popup.Child is FrameworkElement child) Grow(child, popup.PlacementTarget as FrameworkElement);
        };
    }

    /// <summary>
    /// Which corner of the menu touches the anchor, as 0 or 1 on each axis. A menu at or right of the anchor's left edge grows
    /// from its left, one that was pushed left of it grows from its right; likewise a menu that was flipped above the anchor grows from its bottom.
    /// </summary>
    public static Point OriginFor(Rect anchor, Point menuTopLeft, Size menuSize)
    {
        var ox = menuTopLeft.X < anchor.Left - 1 ? 1.0 : 0.0;
        var oy = menuTopLeft.Y + menuSize.Height <= anchor.Top + 1 ? 1.0 : 0.0;
        return new Point(ox, oy);
    }

    private static void OnMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu) Grow(menu, menu.PlacementTarget as FrameworkElement);
    }

    private static void Grow(FrameworkElement element, FrameworkElement? anchor)
    {
        try
        {
            var origin = new Point(0, 0);
            if (anchor is not null && anchor.IsVisible && PresentationSource.FromVisual(element) is not null && PresentationSource.FromVisual(anchor) is not null)
            {
                var a = anchor.PointToScreen(new Point(0, 0));
                var b = anchor.PointToScreen(new Point(anchor.ActualWidth, anchor.ActualHeight));
                var m = element.PointToScreen(new Point(0, 0));
                var mb = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
                origin = OriginFor(new Rect(a, b), m, new Size(mb.X - m.X, mb.Y - m.Y));
            }
            element.RenderTransformOrigin = origin;
            var scale = new ScaleTransform(StartScale, StartScale);
            element.RenderTransform = scale;
            Motion.Animate(scale, ScaleTransform.ScaleXProperty, StartScale, 1, MotionRole.Enter);
            Motion.Animate(scale, ScaleTransform.ScaleYProperty, StartScale, 1, MotionRole.Enter);
            Motion.Animate(element, UIElement.OpacityProperty, 0, 1, MotionRole.Enter);
        }
        catch (InvalidOperationException) { element.RenderTransform = Transform.Identity; element.Opacity = 1; }
    }
}
