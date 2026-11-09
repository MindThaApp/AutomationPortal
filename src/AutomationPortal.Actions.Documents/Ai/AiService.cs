namespace AutomationPortal.Actions.Documents;

/// <summary>Runs an AI request through the provider the action (or Settings) chose.</summary>
public static class AiService
{
    public const string NoKeyMessage = "Add your AI key in Settings › AI to use AI actions.";

    public static async Task<ContentValue> RunAsync(ActionContext ctx, AiRequest request)
    {
        var store = AiProviderStore.For(ctx.Services);
        var id = (await ctx.GetChoiceAsync("provider", "default")).Trim();
        if (id.Length == 0 || id.Equals("default", StringComparison.OrdinalIgnoreCase)) id = store.DefaultProviderId;
        var info = AiProviderRegistry.Get(id) ?? throw new ActionException($"Don't know the AI provider '{id}'. Choose another in the action or in Settings › AI.");
        if (store.SetupProblem(id) is { } problem) throw new ActionException(problem);

        var provider = store.Create(id);
        var availability = await provider.IsAvailableAsync(ctx.CancellationToken);
        if (!availability.IsAvailable) throw new ActionException(availability.Reason ?? $"{info.Name} isn't available on this PC.");

        var model = AiModels.ResolveModel(info, store.Get(id), ctx.Services.Settings.Current.AiModel, await ctx.GetTextAsync("model"));
        ctx.ReportProgress($"Asking {info.Name}…");
        var reply = await provider.CompleteAsync(model, request, ctx.CancellationToken);
        return AiAnswerParser.Parse(request.Shape, reply, request.Fields);
    }
}
