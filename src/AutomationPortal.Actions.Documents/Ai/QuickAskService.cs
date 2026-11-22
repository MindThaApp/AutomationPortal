namespace AutomationPortal.Actions.Documents;

/// <param name="Answer">The reply text, or null when it didn't work.</param>
/// <param name="Problem">Plain-English reason when there is no answer.</param>
/// <param name="NeedsSetup">True when no AI is set up (the caller offers the setup sheet instead of an error).</param>
/// <param name="Privacy">Where the question goes ("Stays on this PC" / "Sends text to ..."), empty when unknown.</param>
public sealed record QuickAskResult(string? Answer, string? Problem, bool NeedsSetup, string Privacy, string ProviderName)
{
    public bool Ok => Answer is not null;
}

/// <summary>One free-text question to the user's default AI provider, for Quick Run's "Ask AI".</summary>
public static class QuickAskService
{
    /// <summary>The privacy line and readiness of the default provider, without sending anything.</summary>
    public static (bool Ready, string Privacy, string Name) Status(AutomationPortalServices services)
    {
        try
        {
            var store = AiProviderStore.For(services);
            var id = store.DefaultProviderId;
            var info = AiProviderRegistry.Get(id);
            return (store.SetupProblem(id) is null, info is null ? "" : AiPrivacy.Label(info), info?.Name ?? "");
        }
        catch (ActionException) { return (false, "", ""); }
    }

    public static async Task<QuickAskResult> AskAsync(AutomationPortalServices services, string question, string? selectedText, CancellationToken ct)
    {
        try
        {
            var store = AiProviderStore.For(services);
            var id = await store.ResolveDefaultAsync(ct);
            var info = AiProviderRegistry.Get(id)!;
            var privacy = AiPrivacy.Label(info);
            if (store.SetupProblem(id) is { } problem) return new QuickAskResult(null, problem, true, privacy, info.Name);
            var provider = store.Create(id);
            var availability = await provider.IsAvailableAsync(ct);
            if (!availability.IsAvailable) return new QuickAskResult(null, availability.Reason ?? $"{info.Name} isn't available on this PC.", false, privacy, info.Name);
            var model = AiModels.ResolveModel(info, store.Get(id), services.Settings.Current.AiModel, null);
            var input = string.IsNullOrWhiteSpace(selectedText) ? Array.Empty<AiPart>() : new AiPart[] { new AiPart.Text(selectedText) };
            var reply = await provider.CompleteAsync(model, AiRequestBuilder.Ask(question, input, "text"), ct);
            var text = reply.Trim();
            return text.Length == 0
                ? new QuickAskResult(null, $"{info.Name} sent back an empty answer. Try asking again.", false, privacy, info.Name)
                : new QuickAskResult(text, null, false, privacy, info.Name);
        }
        catch (ActionException ex) { return new QuickAskResult(null, ex.Message, false, "", ""); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new QuickAskResult(null, "Couldn't reach the AI. " + ex.Message, false, "", ""); }
    }
}
