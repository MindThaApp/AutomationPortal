using AutomationPortal.App.Logic;
using AutomationPortal.App.Services;
using Xunit;

namespace AutomationPortal.App.Tests;

public class RunFeedbackTests
{
    [Fact]
    public void Spinner_shows_when_the_same_run_is_still_going() =>
        Assert.True(RunFeedback.ShouldShowSpinner(TileRunState.Running, 3, 3));

    [Theory]
    [InlineData(TileRunState.Succeeded)]
    [InlineData(TileRunState.Failed)]
    [InlineData(TileRunState.Idle)]
    public void Quick_runs_never_flash_the_spinner(TileRunState finished) =>
        Assert.False(RunFeedback.ShouldShowSpinner(finished, 3, 3));

    [Fact]
    public void A_newer_state_cancels_a_waiting_spinner() =>
        Assert.False(RunFeedback.ShouldShowSpinner(TileRunState.Running, 3, 4));

    [Fact]
    public void Spinner_waits_150_ms() => Assert.Equal(150, RunFeedback.SpinnerDelayMs);
}
