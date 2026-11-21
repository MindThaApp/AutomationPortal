using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;

namespace AutomationPortal.Editor;

/// <summary>The few named lengths of movement the app uses. Nothing is longer than <see cref="Motion.MaxMs"/>.</summary>
public enum MotionRole
{
    /// <summary>A press on a button or tile (83 ms, the fast token).</summary>
    Press,
    /// <summary>Small state changes such as hover (83 ms, the fast token).</summary>
    Fast,
    /// <summary>Something arriving: a dialog, a palette, a row (167 ms, the normal token).</summary>
    Enter,
    /// <summary>Something arriving that moves further: a bar, a drawer, a page slide (250 ms, the nav token).</summary>
    EnterLong,
    /// <summary>Something leaving, about a third quicker than arriving (111 ms).</summary>
    Leave,
    /// <summary>Something leaving that moved a long way (167 ms).</summary>
    LeaveLong,
    /// <summary>A tile fading and shrinking away, or arriving the same way (167 ms).</summary>
    Tile,
    /// <summary>The one bouncy moment, the success tick (333 ms, the slow token).</summary>
    Emphasis,
}

/// <summary>How much the app moves. <see cref="FollowWindows"/> is full motion unless Windows "Animation effects" is off.</summary>
public enum MotionMode
{
    /// <summary>Follow the Windows setting: full motion while it is on, reduced motion while it is off.</summary>
    FollowWindows,
    /// <summary>Slides and scales jump to the end state, fades last at most 100 ms.</summary>
    Reduced,
    /// <summary>Everything jumps to the end state.</summary>
    Off,
}

/// <summary>
/// The one place animations are started. It honours the Windows "Animation effects" setting plus <see cref="Mode"/>,
/// and no transition runs longer than <see cref="MaxMs"/>.
/// </summary>
public static class Motion
{
    public const double MaxMs = 333;

    /// <summary>Longest a fade may last when motion is reduced.</summary>
    public const double ReducedFadeMs = 100;

    private static bool? _testReduced;
    private static MotionMode _mode = MotionMode.FollowWindows;

    static Motion()
    {
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
    }

    /// <summary>The user's motion choice. Defaults to following Windows; the setting UI sets it.</summary>
    public static MotionMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            ReducedChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>What actually applies now: <see cref="MotionMode.FollowWindows"/> means full motion, otherwise reduced or off.</summary>
    public static MotionMode Effective =>
        _testReduced is bool t ? (t ? MotionMode.Reduced : MotionMode.FollowWindows)
        : _mode != MotionMode.FollowWindows ? _mode
        : SystemParameters.ClientAreaAnimation ? MotionMode.FollowWindows : MotionMode.Reduced;

    /// <summary>True when motion is reduced or off. Read at the moment of use, so a change made while the app runs applies to the next movement.</summary>
    public static bool Reduced => Effective != MotionMode.FollowWindows;

    /// <summary>Raised on the UI thread when the Windows "Animation effects" setting or <see cref="Mode"/> changes while the app runs.</summary>
    public static event EventHandler? ReducedChanged;

    private static void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
            ReducedChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Test hook: pretends the Windows setting is on or off. Null goes back to the real setting.</summary>
    internal static void SetReducedForTests(bool? reduced) => _testReduced = reduced;

    /// <summary>Length of a role in milliseconds for slides and scales, 0 when motion is reduced or off.</summary>
    public static double Ms(MotionRole role) => Reduced ? 0 : Nominal(role);

    /// <summary>The length a role has when motion is full.</summary>
    public static double Nominal(MotionRole role) => role switch
    {
        MotionRole.Press => 83,
        MotionRole.Fast => 83,
        MotionRole.Enter => 167,
        MotionRole.EnterLong => 250,
        MotionRole.Leave => 111,
        MotionRole.LeaveLong => 167,
        MotionRole.Tile => 167,
        MotionRole.Emphasis => 333,
        _ => 83,
    };

    /// <summary>The duration of a role (zero when motion is reduced or off).</summary>
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

    private static Duration Dur(double ms) => Reduced ? new Duration(TimeSpan.Zero) : new Duration(TimeSpan.FromMilliseconds(Math.Min(ms, MaxMs)));

    /// <summary>Durations for XAML storyboards (zero when motion is reduced or off when the control is built).</summary>
    public static Duration D100 => Dur(Nominal(MotionRole.Leave));
    public static Duration D120 => Dur(Nominal(MotionRole.Fast));
    public static Duration D180 => Dur(Nominal(MotionRole.EnterLong));
    public static Duration D280 => Dur(Nominal(MotionRole.Emphasis));

    /// <summary>
    /// Starts an animation. Every animation in the app goes through here: the duration is capped at <see cref="MaxMs"/>;
    /// with reduced motion a fade is cut to <see cref="ReducedFadeMs"/> and anything else (slide, scale) lasts no time, and with
    /// motion off everything does. A zero-length animation still ends on its To value and still raises Completed.
    /// Endless pulses are not transitions: they do not run at all unless motion is full.
    /// </summary>
    public static void Begin(IAnimatable target, DependencyProperty property, DoubleAnimation animation)
    {
        var effective = Effective;
        if (animation.RepeatBehavior == RepeatBehavior.Forever)
        {
            if (effective == MotionMode.FollowWindows) target.BeginAnimation(property, animation);
            return;
        }
        var ms = animation.Duration.HasTimeSpan ? animation.Duration.TimeSpan.TotalMilliseconds : 0;
        var isFade = property.Name == nameof(UIElement.Opacity);
        var allowed = effective switch
        {
            MotionMode.FollowWindows => MaxMs,
            MotionMode.Reduced => isFade ? ReducedFadeMs : 0,
            _ => 0,
        };
        animation.Duration = new Duration(TimeSpan.FromMilliseconds(Math.Min(ms, allowed)));
        target.BeginAnimation(property, animation);
    }

    /// <summary>Animates a property to a value, or sets it at once when motion is off.</summary>
    public static void Animate(UIElement target, DependencyProperty property, double? from, double to, double ms, IEasingFunction? easing = null)
    {
        var anim = from is double f ? new DoubleAnimation(f, to, TimeSpan.FromMilliseconds(ms)) : new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms));
        if (easing is not null) anim.EasingFunction = easing;
        Begin(target, property, anim);
    }

    /// <summary>Animates for a named role, with the role's usual easing unless one is given.</summary>
    public static void Animate(UIElement target, DependencyProperty property, double? from, double to, MotionRole role, IEasingFunction? easing = null) =>
        Animate(target, property, from, to, Nominal(role), easing ?? EasingFor(role));

    /// <summary>Same as <see cref="Animate(UIElement, DependencyProperty, double?, double, double, IEasingFunction?)"/> for an animatable such as a transform.</summary>
    public static void Animate(Animatable target, DependencyProperty property, double from, double to, double ms, IEasingFunction? easing = null)
    {
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms));
        if (easing is not null) anim.EasingFunction = easing;
        Begin(target, property, anim);
    }

    /// <summary>Animates a transform for a named role, with the role's usual easing unless one is given.</summary>
    public static void Animate(Animatable target, DependencyProperty property, double from, double to, MotionRole role, IEasingFunction? easing = null) =>
        Animate(target, property, from, to, Nominal(role), easing ?? EasingFor(role));
}