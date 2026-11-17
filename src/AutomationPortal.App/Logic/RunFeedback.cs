using AutomationPortal.App.Services;

namespace AutomationPortal.App.Logic;

/// <summary>Timing decisions for the tile badge.</summary>
public static class RunFeedback
{
    /// <summary>A run shows its spinner only if it is still going after this long, so quick runs never flash it.</summary>
    public const int SpinnerDelayMs = 150;

    /// <summary>True when the delay has passed and the same run is still the current one and still running.</summary>
    public static bool ShouldShowSpinner(TileRunState current, int startedToken, int currentToken) =>
        current == TileRunState.Running && startedToken == currentToken;
}
