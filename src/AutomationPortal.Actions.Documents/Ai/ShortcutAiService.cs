namespace AutomationPortal.Actions.Documents;

/// <summary>Runs "Describe it" / "Describe a change" through the user's default AI provider.</summary>
public static class ShortcutAiService
{
    /// <summary>True when the default provider is set up, so the editor can offer "Describe it".</summary>
    public static bool IsReady(AutomationPortalServices services)
    {
        try { return Problem(services) is null; }
        catch (ActionException) { return false; }
    }

    /// <summary>Why "Describe it" can't be used yet (plain English), or null.</summary>
    public static string? Problem(AutomationPortalServices services)
    {
        var store = AiProviderStore.For(services);
        return store.SetupProblem(store.DefaultProviderId);
    }

    public static async Task<AiDraftResult> RunAsync(AutomationPortalServices services, ActionRegistry actions, TriggerRegistry? triggers,
        string description, Shortcut? current, CancellationToken ct = default)
    {
        try
        {
            var store = AiProviderStore.For(services);
            var id = await store.ResolveDefaultAsync(ct);
            var info = AiProviderRegistry.Get(id)!;
            if (store.SetupProblem(id) is { } problem) return AiDraftResult.Failed(problem);
            var provider = store.Create(id);
            var availability = await provider.IsAvailableAsync(ct);
            if (!availability.IsAvailable) return AiDraftResult.Failed(availability.Reason ?? $"{info.Name} isn't available on this PC.");
            var model = AiModels.ResolveModel(info, store.Get(id), services.Settings.Current.AiModel, null);
            var builder = new ShortcutAiBuilder(actions, triggers, provider, model, services.Settings.Current.ShowAdvancedActions, compact: info.IsLocal);
            return current is null ? await builder.CreateAsync(description, ct) : await builder.ChangeAsync(current, description, ct);
        }
        catch (ActionException ex) { return AiDraftResult.Failed(ex.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return AiDraftResult.Failed("Couldn't reach the AI. " + ex.Message); }
    }
}
