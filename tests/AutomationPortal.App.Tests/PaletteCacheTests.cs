using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Logic;
using Xunit;

namespace AutomationPortal.App.Tests;

public class PaletteCacheTests
{
    private static IReadOnlyList<PaletteEntry> One() =>
        [new PaletteEntry(PaletteKind.Command, "Settings", null, "settings")];

    [Fact]
    public void BuildsOnceAcrossOpens()
    {
        var cache = new PaletteCache(One);
        var a = cache.Get();
        var b = cache.Get();
        Assert.Same(a, b);
        Assert.Equal(1, cache.Builds);
    }

    [Fact]
    public void InvalidateForcesARebuild()
    {
        var cache = new PaletteCache(One);
        cache.Get();
        cache.Invalidate();
        cache.Get();
        Assert.Equal(2, cache.Builds);
    }

    [Fact]
    public void AnOldListIsRebuilt()
    {
        var now = new DateTime(2026, 11, 17, 8, 0, 0, DateTimeKind.Utc);
        var cache = new PaletteCache(One, () => now);
        cache.Get();
        now += TimeSpan.FromMinutes(4);
        cache.Get();
        Assert.Equal(1, cache.Builds);
        now += TimeSpan.FromMinutes(2);
        cache.Get();
        Assert.Equal(2, cache.Builds);
    }

    [Fact]
    public void SpeedLineMarksOverBudget()
    {
        Assert.DoesNotContain("OVER", SpeedBudget.Describe("palette ready", 40, 80));
        Assert.Contains("OVER", SpeedBudget.Describe("palette ready", 120, 80));
    }
}
