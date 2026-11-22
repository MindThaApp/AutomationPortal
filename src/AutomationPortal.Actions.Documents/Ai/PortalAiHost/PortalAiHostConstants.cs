namespace AutomationPortal.Actions.Documents;

/// <summary>
/// Everything about Portal AI Host that may still change (its name, package, link scheme or port) lives here and nowhere else.
/// </summary>
public static class PortalAiHostConstants
{
    public const string DisplayName = "Portal AI Host";

    /// <summary>The link scheme that starts the host ("portal-ai-host:start").</summary>
    public const string Scheme = "portal-ai-host";
    public const string StartUri = Scheme + ":start";

    public const int DefaultPort = 41870;

    /// <summary>What <c>GET /host/ping</c> answers in <c>service</c>.</summary>
    public const string ServiceName = "portal-ai-host";

    /// <summary>Development override for the host's address.</summary>
    public const string UrlEnvironmentVariable = "PORTAL_AI_HOST_URL";

    /// <summary>
    /// The host's package family names, Store build first. TODO: fill in once the Store identity is reserved. While this is empty,
    /// discovery looks through the package folders for a discovery file whose host answers the ping.
    /// </summary>
    public static readonly IReadOnlyList<string> PackageFamilyNames = Array.Empty<string>();

    /// <summary>What the host shows the user when asking whether to allow this app.</summary>
    public const string AppId = "com.mindthaapp.automationportal";
    public const string AppName = "Automation Portal";
    public const string AccessReason = "Describe it and the AI actions in your shortcuts";

    /// <summary>
    /// Where "Get Portal AI Host" goes. TODO: replace the Store search with the product page once its Store ID is known.
    /// </summary>
    public const string StoreUri = "ms-windows-store://search/?query=Portal%20AI%20Host";
}
