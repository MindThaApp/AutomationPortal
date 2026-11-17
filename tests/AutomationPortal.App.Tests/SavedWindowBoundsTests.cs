using AutomationPortal.App.Logic;
using Xunit;

namespace AutomationPortal.App.Tests;

public class SavedWindowBoundsTests
{
    private static readonly PixelRect Main = new(0, 0, 1920, 1040);
    private static readonly PixelRect Second = new(1920, 0, 1920, 1040);

    [Fact]
    public void KeepsAPlaceThatIsOnScreen()
    {
        var r = SavedWindowBounds.Fit(new PixelRect(100, 80, 1180, 760), [Main]);
        Assert.Equal(new PixelRect(100, 80, 1180, 760), r);
    }

    [Fact]
    public void NothingSavedOrNoScreensFallsBack()
    {
        Assert.Null(SavedWindowBounds.Fit(null, [Main]));
        Assert.Null(SavedWindowBounds.Fit(new PixelRect(0, 0, 800, 600), []));
    }

    [Fact]
    public void MinimizedValuesAreIgnored() =>
        Assert.Null(SavedWindowBounds.Fit(new PixelRect(-32000, -32000, 160, 28), [Main]));

    [Fact]
    public void AnUnpluggedMonitorFallsBack() =>
        Assert.Null(SavedWindowBounds.Fit(new PixelRect(2100, 100, 1180, 760), [Main]));

    [Fact]
    public void AMonitorStillThereKeepsItsPlace()
    {
        var r = SavedWindowBounds.Fit(new PixelRect(2100, 100, 1180, 760), [Main, Second]);
        Assert.Equal(new PixelRect(2100, 100, 1180, 760), r);
    }

    [Fact]
    public void ATooBigWindowIsShrunkAndPulledIn()
    {
        var r = SavedWindowBounds.Fit(new PixelRect(-500, -100, 3000, 2000), [Main])!.Value;
        Assert.Equal(new PixelRect(0, 0, 1920, 1040), r);
    }

    [Fact]
    public void MostlyOffTheEdgeIsPulledBack()
    {
        var r = SavedWindowBounds.Fit(new PixelRect(1800, 900, 1180, 760), [Main])!.Value;
        Assert.True(r.Right <= Main.Right && r.Bottom <= Main.Bottom && r.X >= 0 && r.Y >= 0);
    }
}
