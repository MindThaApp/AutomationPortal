using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutomationPortal.Actions.Documents;

/// <summary>What the host says its chat model can do right now. Anything the host doesn't state is treated as not supported.</summary>
public sealed record PortalAiHostCapabilities(int Revision, bool ChatAvailable, string? UnavailableReason, string? ModelName,
    int ContextTokens, int MaxOutputTokens, bool Streaming, bool JsonObject, bool JsonSchema, bool Tools,
    bool EmbeddingsAvailable = false, int EmbeddingsMaxBatch = 0, int EmbeddingsMaxTokens = 0)
{
    public AiJsonMode JsonMode => JsonSchema ? AiJsonMode.JsonSchema : JsonObject ? AiJsonMode.JsonObject : AiJsonMode.None;

    public static PortalAiHostCapabilities Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var chat = Obj(root, "chat");
        var features = chat is { } c ? Obj(c, "features") : null;
        var model = chat is { } c2 ? Obj(c2, "model") : null;
        var emb = Obj(root, "embeddings");
        return new PortalAiHostCapabilities(
            Int(root, "capabilities_revision"),
            chat is { } c3 && Bool(c3, "available"),
            chat is { } c4 ? Text(c4, "reason") : null,
            model is { } m ? Text(m, "display_name") ?? Text(m, "id") : null,
            chat is { } c5 ? Int(c5, "context_tokens") : 0,
            chat is { } c6 ? Int(c6, "max_output_tokens") : 0,
            features is { } f1 && Bool(f1, "streaming"),
            features is { } f2 && Bool(f2, "json_object"),
            features is { } f3 && Bool(f3, "json_schema"),
            features is { } f4 && Bool(f4, "tools"),
            emb is { } e1 && Bool(e1, "available"),
            emb is { } e2 ? Int(e2, "max_batch_inputs") : 0,
            emb is { } e3 ? Int(e3, "max_input_tokens") : 0);
    }

    private static JsonElement? Obj(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;
    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    private static int Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : 0;
    private static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

public enum PortalAiHostPhase { NotInstalled, NotRunning, NeedsAccess, WaitingForAccess, AccessRemoved, NoModel, Ready }

public sealed record PortalAiHostState(PortalAiHostPhase Phase, string Message, PortalAiHostCapabilities? Capabilities = null, PortalAiHostEndpoint? Endpoint = null)
{
    public bool IsReady => Phase == PortalAiHostPhase.Ready;
}

public enum PortalAiHostAccessOutcome { Approved, Denied, Expired, NotRunning, Failed }

public sealed record PortalAiHostAccessResult(PortalAiHostAccessOutcome Outcome, string Message);

/// <summary>What this run of the app last learned about the host. Nothing here is saved to disk.</summary>
public static class PortalAiHostStatus
{
    public static string AccessRemovedMessage => Strings.Get("PortalAiHostStatus_Message_AccessToPortalAI");

    private static volatile bool _ready;
    private static volatile bool _pending;
    private static volatile string? _removed;

    /// <summary>True once the host last answered as ready. Lets the sync "default provider" choice prefer the host.</summary>
    public static bool ReadyHint => _ready;
    public static bool Pending => _pending;
    public static string? AccessRemoved => _removed;

    internal static void SetReady(bool ready) => _ready = ready;
    internal static void SetPending(bool pending) => _pending = pending;
    internal static void SetRemoved(string? message) { _removed = message; if (message is not null) _ready = false; }

    public static void Reset() { _ready = false; _pending = false; _removed = null; }
}

/// <summary>The app's side of the host's access flow: look at its state, ask to be allowed, start it, and forget the token.</summary>
public sealed class PortalAiHostSession
{
    private readonly HttpClient _http;
    private readonly ISecretStore _secrets;
    private readonly PortalAiHostDiscovery _discovery;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public PortalAiHostSession(HttpClient? http, ISecretStore secrets, PortalAiHostDiscovery? discovery = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _secrets = secrets;
        _discovery = discovery ?? new PortalAiHostDiscovery(http);
        _http = http ?? _discovery.Http;
        _delay = delay ?? Task.Delay;
    }

    public static string TokenName => AiProviderStore.KeyName(AiProviderIds.PortalAiHost);

    public bool HasToken => !string.IsNullOrWhiteSpace(_secrets.Get(TokenName));

    public Task<PortalAiHostState> GetStateAsync(CancellationToken ct = default) =>
        EvaluateAsync(_http, _discovery, _secrets.Get(TokenName), RemoveToken, ct);

    private void RemoveToken() => _secrets.Remove(TokenName);

    /// <summary>Looks at the host and says, in plain words, what is missing before it can answer. Only reads.</summary>
    public static async Task<PortalAiHostState> EvaluateAsync(HttpClient http, PortalAiHostDiscovery discovery, string? token, Action? removeToken, CancellationToken ct)
    {
        var endpoint = await discovery.DiscoverAsync(ct);
        if (endpoint is null)
        {
            PortalAiHostStatus.SetReady(false);
            return await discovery.IsInstalledAsync()
                ? new PortalAiHostState(PortalAiHostPhase.NotRunning, Strings.Get("PortalAiHostEmbedder_Message_PortalAIHostIsn"))
                : new PortalAiHostState(PortalAiHostPhase.NotInstalled, Strings.Get("PortalAiHostSession_Message_PortalAIHostIsn"));
        }
        if (string.IsNullOrWhiteSpace(token))
        {
            PortalAiHostStatus.SetReady(false);
            if (PortalAiHostStatus.AccessRemoved is { } removed) return new PortalAiHostState(PortalAiHostPhase.AccessRemoved, removed, Endpoint: endpoint);
            return PortalAiHostStatus.Pending
                ? new PortalAiHostState(PortalAiHostPhase.WaitingForAccess, Strings.Get("PortalAiHostSession_Message_WaitingForYouTo"), Endpoint: endpoint)
                : new PortalAiHostState(PortalAiHostPhase.NeedsAccess, Strings.Get("PortalAiHostSession_Message_AutomationPortalIsnT"), Endpoint: endpoint);
        }

        PortalAiHostReply reply;
        try { reply = await PortalAiHostApi.SendAsync(http, HttpMethod.Get, endpoint.BaseUrl + "/host/capabilities", token, null, TimeSpan.FromSeconds(10), ct); }
        catch (HttpRequestException)
        {
            PortalAiHostStatus.SetReady(false);
            return new PortalAiHostState(PortalAiHostPhase.NotRunning, Strings.Get("PortalAiHostEmbedder_Message_PortalAIHostIsn"));
        }
        if (reply.Status == 401 && IsTokenRejection(reply.ErrorCode))
        {
            RejectToken(removeToken);
            return new PortalAiHostState(PortalAiHostPhase.AccessRemoved, PortalAiHostStatus.AccessRemovedMessage, Endpoint: endpoint);
        }
        if (!reply.Ok)
        {
            PortalAiHostStatus.SetReady(false);
            return new PortalAiHostState(PortalAiHostPhase.NotRunning, Strings.Get("PortalAiHostSession_Message_PortalAIHostIsn2"), Endpoint: endpoint);
        }
        PortalAiHostCapabilities caps;
        try { caps = PortalAiHostCapabilities.Parse(reply.Body); }
        catch (JsonException)
        {
            PortalAiHostStatus.SetReady(false);
            return new PortalAiHostState(PortalAiHostPhase.NotRunning, Strings.Get("PortalAiHostSession_Message_PortalAIHostIsn2"), Endpoint: endpoint);
        }
        if (!caps.ChatAvailable)
        {
            PortalAiHostStatus.SetReady(false);
            var message = caps.UnavailableReason == "insufficient_memory"
                ? Strings.Get("PortalAiHostSession_Message_PortalAIHostCan")
                : Strings.Get("PortalAiHostProvider_Error_NoModelIsLoaded");
            return new PortalAiHostState(PortalAiHostPhase.NoModel, message, caps, endpoint);
        }
        PortalAiHostStatus.SetReady(true);
        PortalAiHostStatus.SetRemoved(null);
        return new PortalAiHostState(PortalAiHostPhase.Ready, Strings.Get("PortalAiHostSession_Message_Ready"), caps, endpoint);
    }

    public static bool IsTokenRejection(string? code) => code is "token_revoked" or "token_invalid";

    /// <summary>The host no longer accepts the token: delete it and remember, for this run, to tell the user.</summary>
    public static void RejectToken(Action? removeToken)
    {
        removeToken?.Invoke();
        PortalAiHostStatus.SetRemoved(PortalAiHostStatus.AccessRemovedMessage);
    }

    /// <summary>
    /// Asks the host to allow this app, then waits (up to its 10 minute limit) for the user's answer. Call this only from a button.
    /// On approval the token is saved in the secret store.
    /// </summary>
    public async Task<PortalAiHostAccessResult> RequestAccessAsync(string appVersion, CancellationToken ct = default)
    {
        var endpoint = await _discovery.DiscoverAsync(ct);
        if (endpoint is null) return new(PortalAiHostAccessOutcome.NotRunning, Strings.Get("PortalAiHostEmbedder_Message_PortalAIHostIsn"));
        PortalAiHostStatus.SetPending(true);
        try
        {
            var body = new JsonObject
            {
                ["app_id"] = PortalAiHostConstants.AppId, ["app_name"] = PortalAiHostConstants.AppName,
                ["app_version"] = appVersion, ["reason"] = PortalAiHostConstants.AccessReason,
            }.ToJsonString();
            var start = await PortalAiHostApi.SendAsync(_http, HttpMethod.Post, endpoint.BaseUrl + "/host/auth/requests", null, body, TimeSpan.FromSeconds(10), ct);
            if (start.Status == 403) return new(PortalAiHostAccessOutcome.Denied, Strings.Get("PortalAiHostSession_Message_YouSaidNoIn"));
            if (start.Status == 429) return new(PortalAiHostAccessOutcome.Failed, Strings.Get("PortalAiHostSession_Message_TooManyTriesWait"));
            if (!start.Ok) return new(PortalAiHostAccessOutcome.Failed, Strings.Get("PortalAiHostSession_Message_PortalAIHostCouldn"));
            string? id;
            try { using var doc = JsonDocument.Parse(start.Body); id = doc.RootElement.TryGetProperty("request_id", out var r) ? r.GetString() : null; }
            catch (JsonException) { id = null; }
            if (string.IsNullOrEmpty(id)) return new(PortalAiHostAccessOutcome.Failed, Strings.Get("PortalAiHostSession_Message_PortalAIHostCouldn"));

            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromMinutes(11))
            {
                var poll = await PortalAiHostApi.SendAsync(_http, HttpMethod.Get,
                    $"{endpoint.BaseUrl}/host/auth/requests/{Uri.EscapeDataString(id)}?wait_ms=25000", null, null, TimeSpan.FromSeconds(40), ct);
                if (!poll.Ok) return new(PortalAiHostAccessOutcome.Failed, Strings.Get("PortalAiHostSession_Message_PortalAIHostLost"));
                string? status, token = null;
                try
                {
                    using var doc = JsonDocument.Parse(poll.Body);
                    status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
                    if (doc.RootElement.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String) token = t.GetString();
                }
                catch (JsonException) { return new(PortalAiHostAccessOutcome.Failed, Strings.Get("PortalAiHostSession_Message_PortalAIHostSent")); }
                switch (status)
                {
                    case "approved" when !string.IsNullOrWhiteSpace(token):
                        _secrets.Set(TokenName, token!);
                        PortalAiHostStatus.SetRemoved(null);
                        return new(PortalAiHostAccessOutcome.Approved, Strings.Get("PortalAiHostSession_Message_Allowed"));
                    case "denied": return new(PortalAiHostAccessOutcome.Denied, Strings.Get("PortalAiHostSession_Message_YouSaidNoIn2"));
                    case "expired": return new(PortalAiHostAccessOutcome.Expired, Strings.Get("PortalAiHostSession_Message_TheRequestRanOut"));
                    case "pending": await _delay(TimeSpan.FromMilliseconds(300), ct); break;
                    default: return new(PortalAiHostAccessOutcome.Failed, Strings.Get("PortalAiHostSession_Message_PortalAIHostCouldn2"));
                }
            }
            return new(PortalAiHostAccessOutcome.Expired, Strings.Get("PortalAiHostSession_Message_TheRequestRanOut"));
        }
        catch (HttpRequestException) { return new(PortalAiHostAccessOutcome.NotRunning, Strings.Get("PortalAiHostEmbedder_Message_PortalAIHostIsn")); }
        finally { PortalAiHostStatus.SetPending(false); }
    }

    /// <summary>Asks the host to start (opens its link), then waits up to 10 seconds for it to answer.</summary>
    public async Task<PortalAiHostState> StartAsync(CancellationToken ct = default)
    {
        try { Process.Start(new ProcessStartInfo(PortalAiHostConstants.StartUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return new PortalAiHostState(PortalAiHostPhase.NotInstalled, Strings.Get("PortalAiHostSession_Message_PortalAIHostIsn"));
        }
        PortalAiHostState state;
        var clock = Stopwatch.StartNew();
        do
        {
            await _delay(TimeSpan.FromMilliseconds(500), ct);
            state = await GetStateAsync(ct);
        } while (state.Phase is PortalAiHostPhase.NotRunning or PortalAiHostPhase.NotInstalled && clock.Elapsed < TimeSpan.FromSeconds(10));
        return state;
    }

    /// <summary>"Forget": tells the host to drop this app's access (best effort), then deletes the saved token.</summary>
    public async Task ForgetAsync(CancellationToken ct = default)
    {
        var token = _secrets.Get(TokenName);
        if (!string.IsNullOrWhiteSpace(token))
        {
            try
            {
                if (await _discovery.DiscoverAsync(ct) is { } endpoint)
                    await PortalAiHostApi.SendAsync(_http, HttpMethod.Delete, endpoint.BaseUrl + "/host/auth/token", token, null, TimeSpan.FromSeconds(5), ct);
            }
            catch (HttpRequestException) { /* the host is gone: the local token goes either way */ }
        }
        _secrets.Remove(TokenName);
        PortalAiHostStatus.SetReady(false);
        PortalAiHostStatus.SetRemoved(null);
    }
}
