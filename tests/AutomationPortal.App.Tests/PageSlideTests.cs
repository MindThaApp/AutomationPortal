using AutomationPortal.App.Infrastructure;
using Xunit;

namespace AutomationPortal.App.Tests;

public class PageSlideTests
{
    [Fact]
    public void Forward_comes_from_the_right_by_24_pixels() => Assert.Equal(24, PageSlide.StartOffset(true));

    [Fact]
    public void Back_comes_from_the_left_by_24_pixels() => Assert.Equal(-24, PageSlide.StartOffset(false));
}
