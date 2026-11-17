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

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void Result_bar_hides_only_when_not_pointed_at_or_focused(bool pointer, bool focus, bool mayHide) =>
        Assert.Equal(mayHide, new BarHidePolicy { PointerOver = pointer, FocusInside = focus }.MayHide);

    [Fact]
    public void First_shortcut_offer_is_taken_once_only()
    {
        var d = new LocalUiSettingsData();
        Assert.True(FirstRunOffer.TryTake(d, false));
        Assert.True(d.FirstRunOfferShown);
        Assert.False(FirstRunOffer.TryTake(d, false));
    }

    [Fact]
    public void First_shortcut_offer_is_kept_for_a_shortcut_without_a_hotkey()
    {
        var d = new LocalUiSettingsData();
        Assert.False(FirstRunOffer.TryTake(d, true));
        Assert.False(d.FirstRunOfferShown);
    }
}
