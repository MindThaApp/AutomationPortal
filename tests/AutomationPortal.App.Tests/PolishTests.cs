using AutomationPortal.App.Pages;
using AutomationPortal.App.Shell;
using Xunit;

namespace AutomationPortal.App.Tests;

public class PolishTests
{
    [Theory]
    [InlineData("", "", true)]
    [InlineData("51.5", "-0.12", true)]
    [InlineData("90", "180", true)]
    [InlineData("-90", "-180", true)]
    [InlineData("91", "0", false)]
    [InlineData("0", "180.5", false)]
    [InlineData("abc", "0", false)]
    [InlineData("10", "", false)]
    [InlineData("NaN", "0", false)]
    public void Home_location_is_validated(string lat, string lon, bool ok)
    {
        var r = HomeLocationInput.Check(lat, lon);
        Assert.Equal(ok, r.Problem is null);
    }

    [Fact]
    public void Home_location_empty_clears_both()
    {
        var r = HomeLocationInput.Check(" ", " ");
        Assert.Null(r.Latitude); Assert.Null(r.Longitude);
    }

    [Fact]
    public void Quick_run_hints_wrap_and_never_return_empty()
    {
        for (var i = -3; i < QuickRunHints.All.Length * 2 + 3; i++) Assert.False(string.IsNullOrWhiteSpace(QuickRunHints.At(i)));
        Assert.Equal(QuickRunHints.At(0), QuickRunHints.At(QuickRunHints.All.Length));
    }
}
