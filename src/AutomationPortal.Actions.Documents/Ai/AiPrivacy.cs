namespace AutomationPortal.Actions.Documents;

/// <summary>The one-line promise about where an AI action's text goes, which depends on the provider that will answer.</summary>
public static class AiPrivacy
{
    public static string StaysOnThisPc => Strings.Get("AiPrivacy_Message_StaysOnThisPC");

    public static string Label(AiProviderInfo info) =>
        info.IsLocal ? StaysOnThisPc : Strings.Format("AiPrivacy_Message_SendsTextTo", Destination(info));

    private static string Destination(AiProviderInfo info) =>
        info.Id == AiProviderIds.Custom ? Strings.Get("AiPrivacy_Message_TheAIServiceYou") : info.Name.Contains(" (", StringComparison.Ordinal) ? info.Name[..info.Name.IndexOf(" (", StringComparison.Ordinal)] : info.Name;

    /// <summary>True for the actions that ask an AI provider (the descriptor says "needs internet" for all of them).</summary>
    public static bool IsAiAction(ActionDescriptor d) => d.Id.StartsWith("ai.", StringComparison.Ordinal);

    /// <summary>The label for an AI action given the default provider, or null when the action isn't an AI action or nothing is known.</summary>
    public static string? NoteFor(ActionDescriptor d, AutomationPortalServices services)
    {
        if (!IsAiAction(d)) return null;
        try
        {
            var store = AiProviderStore.For(services);
            return AiProviderRegistry.Get(store.DefaultProviderId) is { } info ? Label(info) : null;
        }
        catch (ActionException) { return null; }
    }
}
