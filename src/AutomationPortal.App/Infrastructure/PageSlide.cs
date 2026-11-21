using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AutomationPortal.Editor;

namespace AutomationPortal.App.Infrastructure;

/// <summary>The slide of a wizard page: 24 px and a fade over the EnterLong time, from the right when going forward and from the left when going back.</summary>
public static class PageSlide
{
    public const double Distance = 24;

    /// <summary>Where the page starts, relative to its place: positive is to the right.</summary>
    public static double StartOffset(bool forward) => forward ? Distance : -Distance;

    public static void Play(FrameworkElement page, bool forward)
    {
        if (Motion.Reduced) return;
        var move = new TranslateTransform(StartOffset(forward), 0);
        page.RenderTransform = move;
        var ease = Motion.EasingFor(MotionRole.EnterLong);
        var slide = new DoubleAnimation(StartOffset(forward), 0, TimeSpan.FromMilliseconds(Motion.Nominal(MotionRole.EnterLong))) { EasingFunction = ease };
        slide.Completed += (_, _) => { if (ReferenceEquals(page.RenderTransform, move)) page.RenderTransform = Transform.Identity; };
        Motion.Begin(move, TranslateTransform.XProperty, slide);
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Motion.Nominal(MotionRole.EnterLong))) { EasingFunction = ease };
        fade.Completed += (_, _) => page.BeginAnimation(UIElement.OpacityProperty, null);
        Motion.Begin(page, UIElement.OpacityProperty, fade);
    }
}
