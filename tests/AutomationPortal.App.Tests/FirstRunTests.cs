using AutomationPortal.Actions.Windows.Apps;
using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.App.Tests;

public class FirstRunTests
{
    [Fact]
    public void Welcome_shows_on_a_plain_first_start()
        => Assert.True(FirstRun.ShouldShowWelcome(new AppSettings(), background: false, customDataRoot: false, showRequested: false, shortcutCount: 0));

    [Theory]
    [InlineData(true, false, false, 0)]
    [InlineData(false, true, false, 0)]
    [InlineData(false, false, true, 0)]
    [InlineData(false, false, false, 3)]
    public void Welcome_stays_away_for_background_test_show_and_existing_users(bool background, bool customRoot, bool show, int count)
        => Assert.False(FirstRun.ShouldShowWelcome(new AppSettings(), background, customRoot, show, count));

    [Fact]
    public void Welcome_shows_only_once()
        => Assert.False(FirstRun.ShouldShowWelcome(new AppSettings { WelcomeShown = true }, false, false, false, 0));

    [Fact]
    public void Starters_prefer_the_chosen_ones_and_cap_at_four()
    {
        var files = new[] { "a.autoportal", "quick-note.autoportal", "b.autoportal", "unit-converter.autoportal", "weather-now.autoportal", "c.autoportal" };
        var picked = FirstRun.PickStarters(files.Select(f => (f, f)).ToList());
        Assert.Equal(new[] { "unit-converter.autoportal", "weather-now.autoportal", "quick-note.autoportal", "a.autoportal" }, picked);
    }

    [Fact]
    public void Starters_handle_an_empty_gallery()
        => Assert.Empty(FirstRun.PickStarters(new List<(string, string)>()));

    [Theory]
    [InlineData(false, false, null, SiblingStatus.ComingSoon)]
    [InlineData(true, false, null, SiblingStatus.NotInstalled)]
    [InlineData(true, true, null, SiblingStatus.Installed)]
    [InlineData(true, true, PpmLinkState.NotRunning, SiblingStatus.Installed)]
    [InlineData(true, true, PpmLinkState.Running, SiblingStatus.Running)]
    [InlineData(true, true, PpmLinkState.LinksOff, SiblingStatus.ControlOff)]
    [InlineData(true, false, PpmLinkState.Running, SiblingStatus.NotInstalled)]
    public void Status_mapping(bool released, bool installed, PpmLinkState? ppm, SiblingStatus expected)
        => Assert.Equal(expected, ConnectedApps.Map(released, installed, ppm));

    [Fact]
    public void Only_control_off_has_a_fix_line()
    {
        Assert.NotNull(ConnectedApps.FixText(SiblingStatus.ControlOff));
        Assert.Null(ConnectedApps.FixText(SiblingStatus.Running));
    }

    [Fact]
    public void Store_link_is_hidden_without_a_product_id()
    {
        Assert.Null(ConnectedApps.StoreUri(""));
        Assert.Null(ConnectedApps.StoreUri(null));
        Assert.Equal("ms-windows-store://pdp/?productid=9ABC", ConnectedApps.StoreUri("9ABC"));
    }
}
