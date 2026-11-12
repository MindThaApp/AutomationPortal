using AutomationPortal.Actions.Windows.Devices;

namespace AutomationPortal.Actions.Windows.Apps;

public enum PpmLinkState { NotRunning, Running, LinksOff }

/// <summary>Read-only checks for the sibling apps (is it installed, is it running) and a launcher for Power Plan Manager. Nothing is written.</summary>
public static class SiblingApps
{
    /// <summary>True when a URL protocol with this scheme is registered (reads the registry only).</summary>
    public static bool IsRegistered(string scheme)
    {
        try { return new RegistryProtocols().IsRegistered(scheme); }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }

    /// <summary>Asks Power Plan Manager's read-only status pipe. A missing, slow or unreadable reply counts as not running.</summary>
    public static async Task<PpmLinkState> ProbePowerPlanManagerAsync(CancellationToken ct)
    {
        try
        {
            var json = await PpmPipe.Client.RequestAsync("GET STATUS", ct).ConfigureAwait(false);
            if (json is null) return PpmLinkState.NotRunning;
            return PpmPipe.ParseStatus(json).AllowExternalLinks ? PpmLinkState.Running : PpmLinkState.LinksOff;
        }
        catch (Exception ex) when (ex is TimeoutException or ActionException or IOException) { return PpmLinkState.NotRunning; }
    }

    /// <summary>Asks Desk Pal's read-only status over its control pipe. A missing, slow or unreadable reply counts as not running. Never starts Desk Pal.</summary>
    public static Task<DeskPalLinkState> ProbeDeskPalAsync(CancellationToken ct) => DeskPalLink.ProbeAsync(ct);

    /// <summary>Opens Power Plan Manager (<c>powerplanmanager:show</c>). Throws <see cref="ActionException"/> with a plain message when it can't.</summary>
    public static Task OpenPowerPlanManagerAsync(CancellationToken ct) => PowerPlanManagerLinks.SendAsync(false, "show", ct);
}
