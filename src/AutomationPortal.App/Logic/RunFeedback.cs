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

/// <summary>Whether the result bar may hide itself: not while the pointer is over it or keyboard focus is inside it.</summary>
public sealed class BarHidePolicy
{
    public bool PointerOver { get; set; }
    public bool FocusInside { get; set; }

    public bool MayHide => !PointerOver && !FocusInside;
}

/// <summary>The one-time "That was your first shortcut" line with its "Give it a hotkey" button.</summary>
public static class FirstRunOffer
{
    /// <summary>
    /// Takes the offer when it has not been shown yet and the shortcut has no hotkey to give; records that it was shown.
    /// Call inside <see cref="LocalUiSettings.Update"/> so the flag is saved with it.
    /// </summary>
    public static bool TryTake(LocalUiSettingsData data, bool shortcutHasHotkey)
    {
        if (data.FirstRunOfferShown || shortcutHasHotkey) return false;
        data.FirstRunOfferShown = true;
        return true;
    }
}
