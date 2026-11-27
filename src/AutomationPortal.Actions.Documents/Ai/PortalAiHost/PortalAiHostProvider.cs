using System.ClientModel;
using System.Text.Json;

namespace AutomationPortal.Actions.Documents;

/// <summary>
/// Portal AI Host: a local app on this PC that serves an OpenAI-compatible API. The address is found at run time and the
/// access token comes from the secret store; the request itself goes through <see cref="OpenAiProvider"/>.
/// </summary>
public sealed class PortalAiHostProvider : IAiProvider
{
    public static readonly AiProviderInfo Info = new(
        AiProviderIds.PortalAiHost, Strings.Get("PortalAiHostProvider_Name_PortalAIHostOn"), null, NeedsKey: false, NeedsEndpoint: false, UsesDeployment: false,
        SupportsImages: false, IsLocal: true, JsonMode: AiJsonMode.JsonObject,
        Help: Strings.Get("PortalAiHostProvider_Help_FreeAndPrivatePortal"));

    private readonly AiProviderContext _ctx;
    private readonly PortalAiHostDiscovery _discovery;

    /// <summary>Called when the host says the token is gone, so the owner of the secret store can delete it.</summary>
    public Action? OnTokenRejected { get; set; }

    public PortalAiHostProvider(AiProviderContext context, PortalAiHostDiscovery? discovery = null)
    {
        _ctx = context;
        _discovery = discovery ?? new PortalAiHostDiscovery(context.Http);
    }

    public string Id => AiProviderIds.PortalAiHost;

    private HttpClient Http => _ctx.Http ?? _discovery.Http;

    private Task<PortalAiHostState> StateAsync(CancellationToken ct) =>
        PortalAiHostSession.EvaluateAsync(Http, _discovery, _ctx.Key, OnTokenRejected, ct);

    public async Task<AiAvailability> IsAvailableAsync(CancellationToken ct = default)
    {
        var state = await StateAsync(ct);
        return state.IsReady ? AiAvailability.Yes : new AiAvailability(false, Describe(state));
    }

    /// <summary>The sentence shown when the host can't answer yet.</summary>
    public static string Describe(PortalAiHostState state) => state.Phase switch
    {
        PortalAiHostPhase.NotInstalled => Strings.Get("PortalAiHostProvider_Message_PortalAIHostIsn"),
        PortalAiHostPhase.NeedsAccess => Strings.Get("PortalAiHostProvider_Message_AutomationPortalIsnT"),
        _ => state.Message,
    };

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
    {
        var state = await StateAsync(ct);
        if (!state.IsReady || state.Endpoint is null) throw new ActionException(Describe(state));
        PortalAiHostReply reply;
        try { reply = await PortalAiHostApi.SendAsync(Http, HttpMethod.Get, state.Endpoint.BaseUrl + "/v1/models", _ctx.Key, null, TimeSpan.FromSeconds(10), ct); }
        catch (HttpRequestException ex) { throw new ActionException(Strings.Get("PortalAiHostEmbedder_Message_PortalAIHostIsn"), ex); }
        if (!reply.Ok) throw new ActionException(Strings.Get("PortalAiHostProvider_Error_CouldnTGetThe"));
        var ids = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(reply.Body);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                foreach (var m in data.EnumerateArray())
                {
                    var kind = m.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : "chat";
                    if (kind == "chat" && m.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String) ids.Add(id.GetString()!);
                }
        }
        catch (JsonException) { throw new ActionException(Strings.Get("PortalAiHostProvider_Error_PortalAIHostSent")); }
        return ids;
    }

    public async Task<string> CompleteAsync(string model, AiRequest request, CancellationToken ct)
    {
        var state = await StateAsync(ct);
        if (!state.IsReady || state.Endpoint is null || state.Capabilities is null) throw new ActionException(Describe(state));

        // What the host says it can do decides how structured answers are asked for; nothing is assumed.
        var info = Info with { JsonMode = state.Capabilities.JsonMode };
        var settings = new AiProviderSettings { Enabled = true, Model = model, Endpoint = state.Endpoint.BaseUrl + "/v1" };
        var inner = new OpenAiProvider(new AiProviderContext(info, settings, _ctx.Key, PortalAiHostApi.WithClientHeader(Http)));
        try
        {
            // "default" always means the model the user chose in the host.
            return await inner.CompleteAsync(string.IsNullOrWhiteSpace(model) ? "default" : model, request, ct);
        }
        catch (ActionException ex) when (ex.InnerException is ClientResultException cre)
        {
            throw Translate(cre, ex);
        }
    }

    private ActionException Translate(ClientResultException cre, ActionException original)
    {
        string? code = null;
        try { code = new PortalAiHostReply(cre.Status, cre.GetRawResponse()?.Content.ToString() ?? "").ErrorCode; }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException) { }
        if (cre.Status == 401 && PortalAiHostSession.IsTokenRejection(code))
        {
            PortalAiHostSession.RejectToken(OnTokenRejected);
            return new ActionException(PortalAiHostStatus.AccessRemovedMessage, cre);
        }
        return code switch
        {
            "access_denied" => new ActionException(Strings.Get("PortalAiHostProvider_Error_PortalAIHostSays"), cre),
            "no_model" => new ActionException(Strings.Get("PortalAiHostProvider_Error_NoModelIsLoaded"), cre),
            "insufficient_memory" => new ActionException(Strings.Get("PortalAiHostProvider_Error_PortalAIHostCan"), cre),
            "host_stopping" => new ActionException(Strings.Get("PortalAiHostProvider_Error_PortalAIHostIs"), cre),
            "context_length_exceeded" => new ActionException(Strings.Get("PortalAiHostProvider_Error_ThatIsTooMuch"), cre),
            "deadline_exceeded" => new ActionException(Strings.Get("PortalAiHostProvider_Error_PortalAIHostTook"), cre),
            "device_paused" => new ActionException(Strings.Get("PortalAiHostProvider_Error_PortalAIHostHas"), cre),
            "device_quiet_hours" => new ActionException(Strings.Get("PortalAiHostProvider_Error_PortalAIHostIs2"), cre),
            "host_busy_gaming" => new ActionException(Strings.Get("PortalAiHostProvider_Error_PortalAIHostIs3"), cre),
            "device_limit_reached" => new ActionException(Strings.Get("PortalAiHostProvider_Error_PortalAIHostHas2"), cre),
            "remote_unreachable" => new ActionException(Strings.Get("PortalAiHostProvider_Error_PortalAIHostCan2"), cre),
            "queue_full" or "model_loading" or "engine_restarting" => new ActionException(Strings.Get("PortalAiHostProvider_Error_PortalAIHostIs4"), cre),
            _ => original,
        };
    }
}
