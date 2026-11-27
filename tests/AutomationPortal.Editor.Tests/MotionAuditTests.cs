using System.IO;
using System.Linq;
using System.Collections.Generic;
using System;
using System.Text.RegularExpressions;
using AutomationPortal.Editor;
using Xunit;

namespace AutomationPortal.Editor.Tests;

public class MotionAuditTests
{
    private static string SrcRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "AutomationPortal.Editor")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src");
    }

    private static readonly Regex Raw = new(
        @"\b(Cubic|Quadratic|Quartic|Quintic|Sine|Exponential|Back|Elastic|Bounce|Circle|Power)Ease\b|\bnew\s+KeySpline\b|\bKeySpline\s*=|\bKeySpline\b\s*\(|<\s*\w*KeySpline",
        RegexOptions.Compiled);

    [Fact]
    public void NoRawEasingOutsideTheMotionToken()
    {
        var allowed = new[] { "BezierEase.cs", "Motion.cs" };
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(SrcRoot(), "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml")) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                                 && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)))
        {
            if (allowed.Contains(Path.GetFileName(file))) continue;
            var n = 0;
            foreach (var line in File.ReadLines(file))
            {
                n++;
                if (Raw.IsMatch(line)) hits.Add($"{Path.GetRelativePath(SrcRoot(), file)}:{n}");
            }
        }
        Assert.True(hits.Count == 0, "Raw easing found, use Motion.Settle: " + string.Join(", ", hits));
    }

    [Fact]
    public void SettleHasTheSpecifiedShape()
    {
        double F(double x) => BezierEase.Evaluate(0.45, 0, 0.1, 1, x);
        Assert.Equal(0, F(0), 6);
        Assert.Equal(1, F(1), 6);
        // The last 40 % of the time covers well under 15 % of the distance.
        Assert.True(1 - F(0.6) < 0.15, $"1-F(0.6) = {1 - F(0.6)}");
        // Monotonic.
        var prev = 0.0;
        for (var i = 1; i <= 100; i++) { var v = F(i / 100.0); Assert.True(v >= prev); prev = v; }
    }

    [Fact]
    public void TokenIsTheCurveAnimationsUse()
    {
        Assert.Same(Motion.Settle, Motion.EaseOut);
        var e = Motion.Settle;
        Assert.Equal(BezierEase.Evaluate(0.45, 0, 0.1, 1, 0.3), e.Ease(0.3), 6);
    }
}

public class MenuMotionTests
{
    [Fact]
    public void MenuBelowTheAnchorGrowsFromItsTopLeft()
    {
        var o = MenuMotion.OriginFor(new System.Windows.Rect(100, 100, 40, 30), new System.Windows.Point(100, 130), new System.Windows.Size(200, 150));
        Assert.Equal(new System.Windows.Point(0, 0), o);
    }

    [Fact]
    public void MenuFlippedAboveGrowsFromTheBottom()
    {
        var o = MenuMotion.OriginFor(new System.Windows.Rect(100, 400, 40, 30), new System.Windows.Point(100, 250), new System.Windows.Size(200, 150));
        Assert.Equal(1.0, o.Y);
    }

    [Fact]
    public void MenuPushedLeftGrowsFromItsRight()
    {
        var o = MenuMotion.OriginFor(new System.Windows.Rect(1800, 100, 40, 30), new System.Windows.Point(1700, 130), new System.Windows.Size(200, 150));
        Assert.Equal(1.0, o.X);
    }
}

public class HoverOpenTests
{
    [Fact]
    public void ClosesOnlyAHoverOpenedMenuThePointerNeverEntered()
    {
        Assert.True(HoverOpen.ShouldClose(true, false, false, false));
        Assert.False(HoverOpen.ShouldClose(true, true, false, false));   // pointer went into the menu
        Assert.False(HoverOpen.ShouldClose(false, false, false, false)); // opened by click or keyboard
        Assert.False(HoverOpen.ShouldClose(true, false, true, false));   // still on the chevron
        Assert.False(HoverOpen.ShouldClose(true, false, false, true));   // on the menu now
    }

    [Fact]
    public void DelayIsAboutThreeHundredMilliseconds() => Assert.Equal(300, HoverOpen.DelayMs);
}
