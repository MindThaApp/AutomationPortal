using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.App.Tests;

public class GalleryCardInfoTests
{
    [Theory]
    [InlineData("Resize Images", "Resize Images", true)]
    [InlineData("Resize Images", "resize images", true)]
    [InlineData("Resize Images", "Resize Images 2", true)]
    [InlineData("Resize Images", "Resize Images (2)", true)]
    [InlineData("Resize Images", "Resize Images for the web", false)]
    [InlineData("Resize Images", "Tidy Downloads", false)]
    public void Added_means_the_same_name_or_a_numbered_copy(string card, string inLibrary, bool added) =>
        Assert.Equal(added, GalleryCardInfo.IsAdded(card, new[] { inLibrary }));

    [Fact]
    public void A_shortcut_with_power_plan_steps_needs_that_app()
    {
        var s = new Shortcut { Name = "Presentation" };
        s.Actions.Add(new ActionInstance { ActionId = "ppm.setPlan" });
        Assert.Equal("Power Plan Manager", GalleryCardInfo.NeededApp(s));
        Assert.Null(GalleryCardInfo.NeededApp(new Shortcut { Name = "Plain" }));
    }
}
