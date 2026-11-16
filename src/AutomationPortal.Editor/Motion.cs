using System.Windows;
using System.Windows.Media.Animation;

namespace AutomationPortal.Editor;

/// <summary>Honors the Windows "Animation effects" setting: when it is off, transitions jump straight to the end state.</summary>
public static class Motion
{
    public static bool Reduced => !SystemParameters.ClientAreaAnimation;

    private static Duration Dur(double ms) => Reduced ? new Duration(TimeSpan.Zero) : new Duration(TimeSpan.FromMilliseconds(ms));

    /// <summary>Durations for XAML storyboards (zero when animation effects are off).</summary>
    public static Duration D100 => Dur(100);
    public static Duration D120 => Dur(120);
    public static Duration D180 => Dur(180);
    public static Duration D280 => Dur(280);

    /// <summary>Animates a property to a value, or sets it at once when animation effects are off.</summary>
    public static void Animate(UIElement target, DependencyProperty property, double? from, double to, double ms, IEasingFunction? easing = null)
    {
        if (Reduced)
        {
            target.BeginAnimation(property, null);
            target.SetValue(property, to);
            return;
        }
        var anim = from is double f ? new DoubleAnimation(f, to, TimeSpan.FromMilliseconds(ms)) : new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms));
        if (easing is not null) anim.EasingFunction = easing;
        target.BeginAnimation(property, anim);
    }

    /// <summary>Same as <see cref="Animate(UIElement, DependencyProperty, double?, double, double, IEasingFunction?)"/> for an animatable such as a transform.</summary>
    public static void Animate(Animatable target, DependencyProperty property, double from, double to, double ms, IEasingFunction? easing = null)
    {
        if (Reduced)
        {
            target.BeginAnimation(property, null);
            target.SetValue(property, to);
            return;
        }
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms));
        if (easing is not null) anim.EasingFunction = easing;
        target.BeginAnimation(property, anim);
    }
}
