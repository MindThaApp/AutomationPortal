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
        AiProviderIds.PortalAiHost, "Portal AI Host (on this PC)", null, NeedsKey: false, NeedsEndpoint: false, UsesDeployment: false,
        SupportsImages: false, IsLocal: true, JsonMode: AiJsonMode.JsonObject,
        Help: "Free and private. Portal AI Host runs a model on this PC for all your Portal apps. Allow Automation Portal once, and it starts working.");

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
        PortalAiHostPhase.NotInstalled => "Portal AI Host isn't installed. Get it from Settings › AI.",
        PortalAiHostPhase.NeedsAccess => "Automation Portal isn't allowed in Portal AI Host yet. Use the Allow button in Settings › AI.",
        _ => state.Message,
    };

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
    {
        var state = await StateAsync(ct);
        if (!state.IsReady || state.Endpoint is null) throw new ActionException(Describe(state));
        PortalAiHostReply reply;
        try { reply = await PortalAiHostApi.SendAsync(Http, HttpMethod.Get, state.Endpoint.BaseUrl + "/v1/models", _ctx.Key, null, TimeSpan.FromSeconds(10), ct); }
        catch (HttpRequestException ex) { throw new ActionException("Portal AI Host isn't running", ex); }
        if (!reply.Ok) throw new ActionException("Couldn't get the list of models from Portal AI Host.");
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
        catch (JsonException) { throw new ActionException("Portal AI Host sent a list of models that couldn't be read."); }
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
            "access_denied" => new ActionException("Portal AI Host says Automation Portal isn't allowed. You can allow it again in Settings › AI.", cre),
            "no_model" => new ActionException("No model is loaded in Portal AI Host", cre),
            "insufficient_memory" => new ActionException("Portal AI Host can't load its model with the memory free right now. Close something and try again.", cre),
            "host_stopping" => new ActionException("Portal AI Host is closing. Start it again, then try again.", cre),
            "context_length_exceeded" => new ActionException("That is too much text for the model in Portal AI Host. Try a shorter piece.", cre),
            "deadline_exceeded" => new ActionException("Portal AI Host took too long to answer. Try again.", cre),
            "device_paused" => new ActionException("Portal AI Host has paused Automation Portal on this PC. You can turn it back on in Portal AI Host.", cre),
            "device_quiet_hours" => new ActionException("Portal AI Host is in quiet hours right now. Try again later.", cre),
            "host_busy_gaming" => new ActionException("Portal AI Host is paused while a game is running. Try again later.", cre),
            "device_limit_reached" => new ActionException("Portal AI Host has reached its limit for this device. Try again later.", cre),
            "remote_unreachable" => new ActionException("Portal AI Host can't reach the home PC it forwards to. Check that PC is on, then try again.", cre),
            "queue_full" or "model_loading" or "engine_restarting" => new ActionException("Portal AI Host is busy. Try again in a minute.", cre),
            _ => original,
        };
    }
}
