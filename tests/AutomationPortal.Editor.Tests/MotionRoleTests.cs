using System.Windows;
using AutomationPortal.Editor;
using Xunit;

namespace AutomationPortal.Editor.Tests;

[Collection("Motion")]
public class MotionRoleTests
{
    public static IEnumerable<object[]> Roles() => Enum.GetValues<MotionRole>().Select(r => new object[] { r });

    [Theory, MemberData(nameof(Roles))]
    public void NoRoleIsLongerThanThreeHundredAndThirtyThreeMilliseconds(MotionRole role)
    {
        Assert.InRange(Motion.Nominal(role), 1, Motion.MaxMs);
    }

    [Fact]
    public void RolesHaveTheSpecifiedLengths()
    {
        Assert.Equal(83, Motion.Nominal(MotionRole.Press));
        Assert.Equal(83, Motion.Nominal(MotionRole.Fast));
        Assert.Equal(333, Motion.Nominal(MotionRole.Emphasis));
        Assert.Equal(167, Motion.Nominal(MotionRole.Enter));
        Assert.InRange(Motion.Nominal(MotionRole.Leave), 100, 120);
        Assert.True(Motion.Nominal(MotionRole.Leave) < Motion.Nominal(MotionRole.Enter));
    }

    [Theory, MemberData(nameof(Roles))]
    public void EveryRoleIsInstantWhenAnimationEffectsAreOff(MotionRole role)
    {
        Motion.SetReducedForTests(true);
        try
        {
            Assert.Equal(0, Motion.Ms(role));
            Assert.Equal(new Duration(TimeSpan.Zero), Motion.Of(role));
        }
        finally { Motion.SetReducedForTests(null); }
    }

    [Fact]
    public void RolesKeepTheirLengthWhenAnimationEffectsAreOn()
    {
        Motion.SetReducedForTests(false);
        try { Assert.Equal(TimeSpan.FromMilliseconds(167), Motion.Of(MotionRole.Enter).TimeSpan); }
        finally { Motion.SetReducedForTests(null); }
    }

    [Fact]
    public void ArrivalsEaseOutAndDeparturesEaseIn()
    {
        Assert.Same(Motion.EaseOut, Motion.EasingFor(MotionRole.Enter));
        Assert.Same(Motion.EaseIn, Motion.EasingFor(MotionRole.Leave));
        Assert.Null(Motion.EasingFor(MotionRole.Emphasis));
    }

    private static double BeginMs(System.Windows.DependencyProperty property, double ms)
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms));
        if (ReferenceEquals(property, System.Windows.Media.Brush.OpacityProperty)) Motion.Begin(new System.Windows.Media.SolidColorBrush(), property, anim);
        else Motion.Begin(new System.Windows.Media.TranslateTransform(), property, anim);
        return anim.Duration.TimeSpan.TotalMilliseconds;
    }

    [Fact]
    public void FullMotionCapsEveryAnimationAtThreeHundredAndThirtyThree()
    {
        Motion.SetReducedForTests(false);
        try
        {
            Assert.Equal(333, BeginMs(System.Windows.Media.Brush.OpacityProperty, 2000));
            Assert.Equal(250, BeginMs(System.Windows.Media.TranslateTransform.XProperty, 250));
        }
        finally { Motion.SetReducedForTests(null); }
    }

    [Fact]
    public void ReducedMotionMakesSlidesInstantAndFadesAtMostOneHundred()
    {
        Motion.SetReducedForTests(true);
        try
        {
            Assert.Equal(100, BeginMs(System.Windows.Media.Brush.OpacityProperty, 300));
            Assert.Equal(83, BeginMs(System.Windows.Media.Brush.OpacityProperty, 83));
            Assert.Equal(0, BeginMs(System.Windows.Media.TranslateTransform.XProperty, 250));
        }
        finally { Motion.SetReducedForTests(null); }
    }

    [Fact]
    public void ModeOffMakesEverythingInstantAndModeReducedBehavesLikeWindowsAnimationsOff()
    {
        try
        {
            Motion.Mode = MotionMode.Off;
            Assert.Equal(0, BeginMs(System.Windows.Media.Brush.OpacityProperty, 300));
            Motion.Mode = MotionMode.Reduced;
            Assert.Equal(100, BeginMs(System.Windows.Media.Brush.OpacityProperty, 300));
            Assert.Equal(0, BeginMs(System.Windows.Media.TranslateTransform.YProperty, 300));
        }
        finally { Motion.Mode = MotionMode.FollowWindows; }
    }
}