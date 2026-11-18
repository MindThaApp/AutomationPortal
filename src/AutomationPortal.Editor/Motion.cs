using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;

namespace AutomationPortal.Editor;

/// <summary>The few named lengths of movement the app uses. Nothing is longer than <see cref="Motion.MaxMs"/>.</summary>
public enum MotionRole
{
    /// <summary>A press on a button or tile (80 ms).</summary>
    Press,
    /// <summary>Small state changes such as hover (120 ms).</summary>
    Fast,
    /// <summary>Something arriving: a dialog, a palette, a row (160 ms).</summary>
    Enter,
    /// <summary>Something arriving that moves further: a bar, a drawer, a page slide (180 ms).</summary>
    EnterLong,
    /// <summary>Something leaving (100 ms).</summary>
    Leave,
    /// <summary>Something leaving that moved a long way (120 ms).</summary>
    LeaveLong,
    /// <summary>A tile fading and shrinking away, or arriving the same way (140 ms).</summary>
    Tile,
    /// <summary>The one bouncy moment, the success tick (280 ms).</summary>
    Emphasis,
}

/// <summary>Honors the Windows "Animation effects" setting: when it is off, transitions jump straight to the end state.</summary>
public static class Motion
{
    public const double MaxMs = 300;

    private static bool? _testReduced;

    static Motion()
    {
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
    }

    /// <summary>True when Windows "Animation effects" is off. Read at the moment of use, so a change made while the app runs applies to the next movement.</summary>
    public static bool Reduced => _testReduced ?? !SystemParameters.ClientAreaAnimation;

    /// <summary>Raised on the UI thread when the Windows "Animation effects" setting changes while the app runs.</summary>
    public static event EventHandler? ReducedChanged;

    private static void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
            ReducedChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Test hook: pretends the Windows setting is on or off. Null goes back to the real setting.</summary>
    internal static void SetReducedForTests(bool? reduced) => _testReduced = reduced;

    /// <summary>Length of a role in milliseconds, 0 when animation effects are off.</summary>
    public static double Ms(MotionRole role) => Reduced ? 0 : Nominal(role);

    /// <summary>The length a role has when animation effects are on.</summary>
    public static double Nominal(MotionRole role) => role switch
    {
        MotionRole.Press => 80,
        MotionRole.Fast => 120,
        MotionRole.Enter => 160,
        MotionRole.EnterLong => 180,
        MotionRole.Leave => 100,
        MotionRole.LeaveLong => 120,
        MotionRole.Tile => 140,
        MotionRole.Emphasis => 280,
        _ => 120,
    };

    /// <summary>The duration of a role (zero when animation effects are off).</summary>
    public static Duration Of(MotionRole role) => new(TimeSpan.FromMilliseconds(Ms(role)));

    /// <summary>Things arriving slow down.</summary>
    public static IEasingFunction EaseOut { get; } = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });

    /// <summary>Things leaving speed up.</summary>
    public static IEasingFunction EaseIn { get; } = Frozen(new CubicEase { EasingMode = EasingMode.EaseIn });

    private static IEasingFunction Frozen(CubicEase e) { e.Freeze(); return e; }

    private static bool IsLeaving(MotionRole role) => role is MotionRole.Leave or MotionRole.LeaveLong;

    /// <summary>The default easing of a role: arrivals ease out, departures ease in, the others are linear.</summary>
    public static IEasingFunction? EasingFor(MotionRole role) =>
        IsLeaving(role) ? EaseIn : role is MotionRole.Enter or MotionRole.EnterLong ? EaseOut : null;

    private static Duration Dur(double ms) => Reduced ? new Duration(TimeSpan.Zero) : new Duration(TimeSpan.FromMilliseconds(ms));

    /// <summary>Durations for XAML storyboards (zero when animation effects are off when the control is built).</summary>
    public static Duration D100 => Dur(Nominal(MotionRole.Leave));
    public static Duration D120 => Dur(Nominal(MotionRole.Fast));
    public static Duration D180 => Dur(Nominal(MotionRole.EnterLong));
    public static Duration D280 => Dur(Nominal(MotionRole.Emphasis));

    /// <summary>Animates a property to a value, or sets it at once when animation effects are off.</summary>
    public static void Animate(UIElement target, DependencyProperty property, double? from, double to, double ms, IEasingFunction? easing = null)
    {
        if (Reduced)
        {
            target.BeginAnimation(property, null);
            target.SetValue(property, to);
            return;
        }
        var anim = from is double f ? new DoubleAnimation(f, to, TimeSpan.FromMilliseconds(Math.Min(ms, MaxMs))) : new DoubleAnimation(to, TimeSpan.FromMilliseconds(Math.Min(ms, MaxMs)));
        if (easing is not null) anim.EasingFunction = easing;
        target.BeginAnimation(property, anim);
    }

    /// <summary>Animates for a named role, with the role's usual easing unless one is given.</summary>
    public static void Animate(UIElement target, DependencyProperty property, double? from, double to, MotionRole role, IEasingFunction? easing = null) =>
        Animate(target, property, from, to, Nominal(role), easing ?? EasingFor(role));

    /// <summary>Same as <see cref="Animate(UIElement, DependencyProperty, double?, double, double, IEasingFunction?)"/> for an animatable such as a transform.</summary>
    public static void Animate(Animatable target, DependencyProperty property, double from, double to, double ms, IEasingFunction? easing = null)
    {
        if (Reduced)
        {
            target.BeginAnimation(property, null);
            target.SetValue(property, to);
            return;
        }
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(Math.Min(ms, MaxMs)));
        if (easing is not null) anim.EasingFunction = easing;
        target.BeginAnimation(property, anim);
    }

    /// <summary>Animates a transform for a named role, with the role's usual easing unless one is given.</summary>
    public static void Animate(Animatable target, DependencyProperty property, double from, double to, MotionRole role, IEasingFunction? easing = null) =>
        Animate(target, property, from, to, Nominal(role), easing ?? EasingFor(role));
}
