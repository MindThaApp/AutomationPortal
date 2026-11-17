using AutomationPortal.Editor;
using Xunit;

namespace AutomationPortal.Editor.Tests;

public class RowMotionPlanTests
{
    [Fact]
    public void RowsBelowARemovedRowStartFromWhereTheyWere()
    {
        var before = new Dictionary<string, double> { ["a"] = 0, ["b"] = 60, ["c"] = 120 };
        var after = new Dictionary<string, double> { ["a"] = 0, ["c"] = 60 };
        var shifts = RowMotionPlan.Shifts(before, after);
        Assert.Equal(new Dictionary<string, double> { ["c"] = 60 }, shifts);
    }

    [Fact]
    public void AMovedRowAndItsNeighbourSwapPlaces()
    {
        var before = new Dictionary<string, double> { ["a"] = 0, ["b"] = 60 };
        var after = new Dictionary<string, double> { ["b"] = 0, ["a"] = 60 };
        var shifts = RowMotionPlan.Shifts(before, after);
        Assert.Equal(-60, shifts["a"]);
        Assert.Equal(60, shifts["b"]);
    }

    [Fact]
    public void RowsThatDidNotMoveAreLeftAlone()
    {
        var same = new Dictionary<string, double> { ["a"] = 0, ["b"] = 60.2 };
        Assert.Empty(RowMotionPlan.Shifts(same, new Dictionary<string, double> { ["a"] = 0, ["b"] = 60 }));
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 0, true)]
    [InlineData(0, 3, true)]
    [InlineData(2, 2, false)]
    [InlineData(40, 0, false)]
    public void OnlySmallChangesAreAnimated(int added, int removed, bool expected) =>
        Assert.Equal(expected, RowMotionPlan.IsSmallChange(added, removed));
}
