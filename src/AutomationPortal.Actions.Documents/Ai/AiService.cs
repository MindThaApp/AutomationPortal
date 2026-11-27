namespace AutomationPortal.Actions.Documents;

/// <summary>Runs an AI request through the provider the action (or Settings) chose.</summary>
public static class AiService
{
    public static string NoKeyMessage => Strings.Get("AiProviderStore_Message_AddYourAIKey");

    public static async Task<ContentValue> RunAsync(ActionContext ctx, AiRequest request)
    {
        var store = AiProviderStore.For(ctx.Services);
        var id = (await ctx.GetChoiceAsync("provider", "default")).Trim();
        if (id.Length == 0 || id.Equals("default", StringComparison.OrdinalIgnoreCase)) id = await store.ResolveDefaultAsync(ctx.CancellationToken);
        var info = AiProviderRegistry.Get(id) ?? throw new ActionException(Strings.Format("AiService_Error_DonTKnowThe", id));
        if (store.SetupProblem(id) is { } problem) throw new ActionException(problem);

        var provider = store.Create(id);
        var availability = await provider.IsAvailableAsync(ctx.CancellationToken);
        if (!availability.IsAvailable) throw new ActionException(availability.Reason ?? Strings.Format("AiProviderStore_Message_IsnTAvailableOn", info.Name));

        var model = AiModels.ResolveModel(info, store.Get(id), ctx.Services.Settings.Current.AiModel, await ctx.GetTextAsync("model"));
        ctx.ReportProgress($"Asking {info.Name}…");
        var reply = await provider.CompleteAsync(model, request, ctx.CancellationToken);
        return AiAnswerParser.Parse(request.Shape, reply, request.Fields);
    }
}
