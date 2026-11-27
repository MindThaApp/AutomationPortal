using System.Windows;
using System.Windows.Media.Animation;

namespace AutomationPortal.Editor;

/// <summary>
/// An easing that follows a CSS-style cubic Bezier (0,0)-(x1,y1)-(x2,y2)-(1,1). This file and Motion.cs are the only
/// places easing curves are defined; everything else asks Motion for a token.
/// </summary>
public sealed class BezierEase : EasingFunctionBase
{
    public static readonly DependencyProperty X1Property = DependencyProperty.Register(nameof(X1), typeof(double), typeof(BezierEase), new PropertyMetadata(0.45));
    public static readonly DependencyProperty Y1Property = DependencyProperty.Register(nameof(Y1), typeof(double), typeof(BezierEase), new PropertyMetadata(0.0));
    public static readonly DependencyProperty X2Property = DependencyProperty.Register(nameof(X2), typeof(double), typeof(BezierEase), new PropertyMetadata(0.1));
    public static readonly DependencyProperty Y2Property = DependencyProperty.Register(nameof(Y2), typeof(double), typeof(BezierEase), new PropertyMetadata(1.0));

    public BezierEase() { EasingMode = EasingMode.EaseIn; }

    public BezierEase(double x1, double y1, double x2, double y2) : this()
    {
        X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
    }

    public double X1 { get => (double)GetValue(X1Property); set => SetValue(X1Property, value); }
    public double Y1 { get => (double)GetValue(Y1Property); set => SetValue(Y1Property, value); }
    public double X2 { get => (double)GetValue(X2Property); set => SetValue(X2Property, value); }
    public double Y2 { get => (double)GetValue(Y2Property); set => SetValue(Y2Property, value); }

    protected override Freezable CreateInstanceCore() => new BezierEase();

    /// <summary>The eased progress for a time fraction, with the curve applied as is (no in/out mirroring).</summary>
    protected override double EaseInCore(double normalizedTime) => Evaluate(X1, Y1, X2, Y2, normalizedTime);

    /// <summary>Evaluates y for the given x on the Bezier curve. Pure, so tests can check the curve shape.</summary>
    public static double Evaluate(double x1, double y1, double x2, double y2, double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        double lo = 0, hi = 1, t = x;
        for (var i = 0; i < 24; i++)
        {
            var cx = Coord(x1, x2, t);
            if (Math.Abs(cx - x) < 1e-6) break;
            if (cx < x) lo = t; else hi = t;
            t = (lo + hi) / 2;
        }
        return Coord(y1, y2, t);
    }

    private static double Coord(double p1, double p2, double t)
    {
        var u = 1 - t;
        return 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t;
    }
}
