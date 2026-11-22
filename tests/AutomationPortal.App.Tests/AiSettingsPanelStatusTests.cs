using AutomationPortal.Actions.Documents;
using AutomationPortal.App.Pages;
using Xunit;

namespace AutomationPortal.App.Tests;

public sealed class AiSettingsPanelStatusTests
{
    [Theory]
    [InlineData(PortalAiHostPhase.NotInstalled)]
    [InlineData(PortalAiHostPhase.NeedsAccess)]
    [InlineData(PortalAiHostPhase.NotRunning)]
    [InlineData(PortalAiHostPhase.NoModel)]
    public void Host_status_ends_with_a_single_period_and_does_not_name_its_own_page(PortalAiHostPhase phase)
    {
        var line = AiSettingsPanel.HostStatus(new PortalAiHostState(phase, "Portal AI Host isn't running."));
        Assert.EndsWith(".", line);
        Assert.False(line.EndsWith("..", StringComparison.Ordinal));
        Assert.DoesNotContain("Settings", line);
    }

    [Fact]
    public void Not_installed_status_is_one_plain_sentence()
    {
        Assert.Equal("Portal AI Host isn't installed.", AiSettingsPanel.HostStatus(new PortalAiHostState(PortalAiHostPhase.NotInstalled, "")));
    }
}
