using AutomationPortal.Actions.Windows.Apps;
using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

/// <summary>When the welcome appears, and which gallery shortcuts it offers.</summary>
public static class FirstRun
{
    /// <summary>
    /// Only on a normal first start: not started in the background, not with a throwaway data root (tests, demos),
    /// not when a developer window was asked for, and not for someone who already has shortcuts (an upgrade).
    /// </summary>
    public static bool ShouldShowWelcome(AppSettings settings, bool background, bool customDataRoot, bool showRequested, int shortcutCount) =>
        !settings.WelcomeShown && !background && !customDataRoot && !showRequested && shortcutCount == 0;

    private static readonly string[] Preferred = { "unit-converter", "weather-now", "quick-note", "good-morning" };

    /// <summary>The file name (without extension) that starts ticked: it only converts what you give it.</summary>
    public const string TickedByDefault = "unit-converter";

    /// <summary>Up to four starters from the gallery: the preferred ones first, then whatever else opens the first section.</summary>
    public static IReadOnlyList<T> PickStarters<T>(IReadOnlyList<(string FileName, T Item)> available, int max = 4)
    {
        var picked = new List<T>();
        foreach (var name in Preferred)
            foreach (var a in available)
                if (picked.Count < max && string.Equals(Path.GetFileNameWithoutExtension(a.FileName), name, StringComparison.OrdinalIgnoreCase)) picked.Add(a.Item);
        foreach (var a in available)
            if (picked.Count < max && !picked.Contains(a.Item)) picked.Add(a.Item);
        return picked;
    }
}

public enum SiblingStatus { NotInstalled, Installed, Running, ControlOff, ComingSoon }

/// <summary>Status text and button rules for the "Connected apps" card.</summary>
public static class ConnectedApps
{
    public static SiblingStatus Map(bool released, bool installed, PpmLinkState? ppm)
    {
        if (!released) return SiblingStatus.ComingSoon;
        if (!installed) return SiblingStatus.NotInstalled;
        return ppm switch
        {
            PpmLinkState.Running => SiblingStatus.Running,
            PpmLinkState.LinksOff => SiblingStatus.ControlOff,
            _ => SiblingStatus.Installed,
        };
    }

    /// <summary>
    /// Desk Pal isn't in the Store yet and doesn't always register its link, so an answer on its control pipe counts most: running
    /// (or running with control off). Without one, a registered <c>deskpal:</c> link means installed; otherwise it shows as coming soon.
    /// </summary>
    public static SiblingStatus MapDeskPal(bool registered, DeskPalLinkState probe) => probe switch
    {
        DeskPalLinkState.Running => SiblingStatus.Running,
        DeskPalLinkState.ControlOff => SiblingStatus.ControlOff,
        _ => registered ? SiblingStatus.Installed : SiblingStatus.ComingSoon,
    };

    public static string StatusText(SiblingStatus s) => s switch
    {
        SiblingStatus.NotInstalled => "Not installed",
        SiblingStatus.Installed => "Installed",
        SiblingStatus.Running => "Running",
        SiblingStatus.ControlOff => "Control off",
        _ => "Coming soon",
    };

    public static string? FixText(SiblingStatus s, string app = "Power Plan Manager") => s != SiblingStatus.ControlOff ? null : app switch
    {
        "Desk Pal" => "Turn on \"Allow Automation Portal and other apps to control Desk Pal\" in Desk Pal Settings.",
        _ => "Turn on \"Allow links from other apps\" in Power Plan Manager.",
    };

    /// <summary>The Store page link, or null when the product id isn't known yet.</summary>
    public static string? StoreUri(string? productId) =>
        string.IsNullOrWhiteSpace(productId) ? null : "ms-windows-store://pdp/?productid=" + Uri.EscapeDataString(productId);
}
