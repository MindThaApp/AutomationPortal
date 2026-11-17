using System.Windows;
using AutomationPortal.Editor;
using Xunit;

namespace AutomationPortal.Editor.Tests;

[Collection("Motion")]
public class MotionRoleTests
{
    public static IEnumerable<object[]> Roles() => Enum.GetValues<MotionRole>().Select(r => new object[] { r });

    [Theory, MemberData(nameof(Roles))]
    public void NoRoleIsLongerThanThreeHundredMilliseconds(MotionRole role)
    {
        Assert.InRange(Motion.Nominal(role), 1, Motion.MaxMs);
    }

    [Fact]
    public void RolesHaveTheSpecifiedLengths()
    {
        Assert.Equal(80, Motion.Nominal(MotionRole.Press));
        Assert.Equal(120, Motion.Nominal(MotionRole.Fast));
        Assert.Equal(280, Motion.Nominal(MotionRole.Emphasis));
        Assert.InRange(Motion.Nominal(MotionRole.Enter), 160, 180);
        Assert.InRange(Motion.Nominal(MotionRole.Leave), 100, 120);
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
        try { Assert.Equal(TimeSpan.FromMilliseconds(160), Motion.Of(MotionRole.Enter).TimeSpan); }
        finally { Motion.SetReducedForTests(null); }
    }

    [Fact]
    public void ArrivalsEaseOutAndDeparturesEaseIn()
    {
        Assert.Same(Motion.EaseOut, Motion.EasingFor(MotionRole.Enter));
        Assert.Same(Motion.EaseIn, Motion.EasingFor(MotionRole.Leave));
        Assert.Null(Motion.EasingFor(MotionRole.Emphasis));
    }
}
