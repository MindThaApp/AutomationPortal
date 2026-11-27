using AutomationPortal.App.Logic;
using Xunit;

namespace AutomationPortal.App.Tests;

public class WindowSizeFitTests
{
    [Fact]
    public void At150PercentOn1080pTheWindowShrinksToTheWorkArea()
    {
        var work = WindowSizeFit.WorkAreaDips(1920, 1032, 1.5);   // 1280 x 688 DIPs
        var (size, min) = WindowSizeFit.Clamp(new DipSize(1180, 760), new DipSize(760, 520), work);
        Assert.Equal(1180, size.Width);
        Assert.Equal(688, size.Height);
        Assert.Equal(520, min.Height);
    }

    [Fact]
    public void MinimumNeverExceedsTheClampedSize()
    {
        var work = WindowSizeFit.WorkAreaDips(1920, 1032, 2.0);   // 960 x 516 DIPs
        var (size, min) = WindowSizeFit.Clamp(new DipSize(1180, 760), new DipSize(760, 520), work);
        Assert.Equal(960, size.Width);
        Assert.Equal(516, size.Height);
        Assert.True(min.Height <= size.Height && min.Width <= size.Width);
    }

    [Fact]
    public void RoomyScreenLeavesTheSizeAlone()
    {
        var (size, min) = WindowSizeFit.Clamp(new DipSize(1180, 760), new DipSize(760, 520), new DipSize(2560, 1400));
        Assert.Equal(new DipSize(1180, 760), size);
        Assert.Equal(new DipSize(760, 520), min);
    }

    [Fact]
    public void EmptyWorkAreaChangesNothing()
    {
        var (size, _) = WindowSizeFit.Clamp(new DipSize(1180, 760), new DipSize(760, 520), new DipSize(0, 0));
        Assert.Equal(new DipSize(1180, 760), size);
    }
}
