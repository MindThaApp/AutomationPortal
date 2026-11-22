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
            var (builder, problem) = await DescribeBuilderAsync(services, actions, triggers, ct);
            if (builder is null) return AiDraftResult.Failed(problem!);
            return current is null ? await builder.CreateAsync(description, ct) : await builder.ChangeAsync(current, description, ct);
        }
        catch (ActionException ex) { return AiDraftResult.Failed(ex.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return AiDraftResult.Failed("Couldn't reach the AI. " + ex.Message); }
    }

    /// <summary>The questions to ask before building (at most three). Empty when none are needed or anything goes wrong: build straight away then.</summary>
    public static async Task<IReadOnlyList<AiQuestion>> ClarifyAsync(AutomationPortalServices services, ActionRegistry actions, TriggerRegistry? triggers,
        string description, CancellationToken ct = default)
    {
        try
        {
            var (builder, _) = await DescribeBuilderAsync(services, actions, triggers, ct);
            return builder is null ? Array.Empty<AiQuestion>() : await builder.ClarifyAsync(description, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Array.Empty<AiQuestion>(); }
    }

    private static async Task<(ShortcutAiBuilder? Builder, string? Problem)> DescribeBuilderAsync(AutomationPortalServices services, ActionRegistry actions,
        TriggerRegistry? triggers, CancellationToken ct)
    {
        var store = AiProviderStore.For(services);
        var id = await store.ResolveDefaultAsync(ct);
        var info = AiProviderRegistry.Get(id)!;
        if (store.SetupProblem(id) is { } problem) return (null, problem);
        var provider = store.Create(id);
        var availability = await provider.IsAvailableAsync(ct);
        if (!availability.IsAvailable) return (null, availability.Reason ?? $"{info.Name} isn't available on this PC.");
        var model = AiModels.ResolveModel(info, store.Get(id), services.Settings.Current.AiModel, null);
        return (new ShortcutAiBuilder(actions, triggers, provider, model, services.Settings.Current.ShowAdvancedActions, compact: info.IsLocal, jsonMode: info.JsonMode), null);
    }

    /// <summary>The line shown in the Describe it dialog: where the description and the answers go.</summary>
    public static string DescribePrivacyLine(AutomationPortalServices services)
    {
        try
        {
            var info = AiProviderRegistry.Get(AiProviderStore.For(services).DefaultProviderId);
            if (info is null) return "";
            return info.IsLocal ? "Stays on this PC" : $"Sends your description and answers to {info.Name}";
        }
        catch (ActionException) { return ""; }
    }

    /// <summary>The line shown under Explain / Fix it: where the question goes.</summary>
    public static string PrivacyLine(AutomationPortalServices services, bool forFix = false)
    {
        try { return PrivacyLineFor(AiProviderRegistry.Get(AiProviderStore.For(services).DefaultProviderId), forFix); }
        catch (ActionException) { return ""; }
    }

    /// <summary>Fix sends the steps' settings (including typed text); Explain sends only step names and the error.</summary>
    public static string PrivacyLineFor(AiProviderInfo? info, bool forFix)
    {
        if (info is null) return "";
        if (info.IsLocal) return "Stays on this PC";
        return forFix ? $"Fix sends this shortcut's steps, including text you typed in them, and the error to {info.Name}"
                      : $"Sends step names and the error to {info.Name}";
    }

    public static Task<AiExplainResult> ExplainErrorAsync(AutomationPortalServices services, ActionRegistry actions, TriggerRegistry? triggers,
        Shortcut shortcut, RunRecord run, CancellationToken ct = default) =>
        WithExplainer(services, actions, triggers, (e, token) => e.ExplainErrorAsync(shortcut, run, token), ct);

    public static Task<AiExplainResult> ExplainShortcutAsync(AutomationPortalServices services, ActionRegistry actions, TriggerRegistry? triggers,
        Shortcut shortcut, CancellationToken ct = default) =>
        WithExplainer(services, actions, triggers, (e, token) => e.ExplainShortcutAsync(shortcut, token), ct);

    /// <summary>A corrected copy of <paramref name="shortcut"/> as a draft. Nothing is saved or run.</summary>
    public static async Task<AiDraftResult> FixAsync(AutomationPortalServices services, ActionRegistry actions, TriggerRegistry? triggers,
        Shortcut shortcut, RunRecord run, CancellationToken ct = default)
    {
        var prepared = await PrepareAsync(services, actions, triggers, ct);
        if (prepared.Problem is { } problem) return AiDraftResult.Failed(problem.Error ?? "The AI isn't ready.");
        try { return await prepared.Explainer!.FixAsync(shortcut, run, ct); }
        catch (ActionException ex) { return AiDraftResult.Failed(ex.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return AiDraftResult.Failed("Couldn't reach the AI. " + ex.Message); }
    }

    private static async Task<AiExplainResult> WithExplainer(AutomationPortalServices services, ActionRegistry actions, TriggerRegistry? triggers,
        Func<ShortcutAiExplainer, CancellationToken, Task<AiExplainResult>> ask, CancellationToken ct)
    {
        var prepared = await PrepareAsync(services, actions, triggers, ct);
        if (prepared.Problem is { } problem) return problem;
        try { return await ask(prepared.Explainer!, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return AiExplainResult.Failed("Couldn't reach the AI. " + ex.Message); }
    }

    private static async Task<(ShortcutAiExplainer? Explainer, AiExplainResult? Problem)> PrepareAsync(AutomationPortalServices services,
        ActionRegistry actions, TriggerRegistry? triggers, CancellationToken ct)
    {
        try
        {
            var store = AiProviderStore.For(services);
            var id = store.DefaultProviderId;
            var info = AiProviderRegistry.Get(id)!;
            if (store.SetupProblem(id) is { } problem) return (null, AiExplainResult.Setup(problem));
            var provider = store.Create(id);
            var availability = await provider.IsAvailableAsync(ct);
            if (!availability.IsAvailable) return (null, AiExplainResult.Setup(availability.Reason ?? $"{info.Name} isn't available on this PC."));
            var model = AiModels.ResolveModel(info, store.Get(id), services.Settings.Current.AiModel, null);
            return (new ShortcutAiExplainer(actions, triggers, provider, model, services.Settings.Current.ShowAdvancedActions, compact: info.IsLocal), null);
        }
        catch (ActionException ex) { return (null, AiExplainResult.Setup(ex.Message)); }
    }
}
