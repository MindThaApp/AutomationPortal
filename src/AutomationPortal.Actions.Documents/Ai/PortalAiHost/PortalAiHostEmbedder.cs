using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutomationPortal.Actions.Documents;

/// <summary>Unit-length vectors, one per input text in input order, and the model that made them (vectors of different models must never be compared).</summary>
public sealed record EmbeddingBatch(string Model, IReadOnlyList<float[]> Vectors);

public interface ITextEmbedder
{
    /// <summary>True when embeddings can be had right now.</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct);

    /// <summary>The vectors, or null when embeddings aren't available or the answer was unusable. Never throws for an unreachable host.</summary>
    Task<EmbeddingBatch?> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct);
}

/// <summary>
/// Embeddings from Portal AI Host (<c>POST /v1/embeddings</c>), used only when the host is ready, Automation Portal already has
/// its token, and <c>/host/capabilities</c> says embeddings are available. It only reads: it never asks for access or starts the host.
/// </summary>
public sealed class PortalAiHostEmbedder : ITextEmbedder
{
    public const int MaxInputChars = 1500;
    private const int DefaultBatch = 16;

    private readonly Func<CancellationToken, Task<PortalAiHostState>> _state;
    private readonly Func<string?> _token;
    private readonly HttpClient _http;
    private readonly Func<DateTime> _now;
    private (PortalAiHostState State, DateTime At)? _cached;

    public PortalAiHostEmbedder(Func<CancellationToken, Task<PortalAiHostState>> state, Func<string?> token, HttpClient? http = null, Func<DateTime>? now = null)
    {
        _state = state; _token = token; _http = http ?? PortalAiHostApi.SharedHttp.Value; _now = now ?? (() => DateTime.UtcNow);
    }

    public static PortalAiHostEmbedder For(AiProviderStore store, HttpClient? http = null) =>
        new(ct => store.Host.GetStateAsync(ct), () => store.GetKey(AiProviderIds.PortalAiHost), http);

    /// <summary>One look at the host serves a burst of searches; a failure is remembered for a short while too.</summary>
    private async Task<PortalAiHostState?> GetStateAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_token())) return null;
        if (_cached is { } c && _now() - c.At < (c.State.IsReady ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(10))) return c.State;
        PortalAiHostState state;
        try { state = await _state(ct); }
        catch (Exception ex) when (ex is HttpRequestException or ActionException) { state = new PortalAiHostState(PortalAiHostPhase.NotRunning, Strings.Get("PortalAiHostEmbedder_Message_PortalAIHostIsn")); }
        _cached = (state, _now());
        return state;
    }

    private void Forget() => _cached = null;

    private static bool CanEmbed(PortalAiHostState? s) => s is { IsReady: true, Endpoint: not null, Capabilities.EmbeddingsAvailable: true };

    public async Task<bool> IsAvailableAsync(CancellationToken ct) => CanEmbed(await GetStateAsync(ct));

    public async Task<EmbeddingBatch?> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        if (texts.Count == 0) return new EmbeddingBatch("", Array.Empty<float[]>());
        var state = await GetStateAsync(ct);
        if (!CanEmbed(state)) return null;
        var token = _token();
        if (string.IsNullOrWhiteSpace(token)) return null;
        var batchSize = state!.Capabilities!.EmbeddingsMaxBatch is > 0 and var m ? Math.Min(m, 64) : DefaultBatch;

        var all = new List<float[]>(texts.Count);
        string? model = null;
        for (var i = 0; i < texts.Count; i += batchSize)
        {
            var chunk = texts.Skip(i).Take(batchSize).Select(t => t.Length > MaxInputChars ? t[..MaxInputChars] : t).ToList();
            var body = new JsonObject
            {
                ["model"] = "default-embedding",
                ["input"] = new JsonArray(chunk.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()),
                ["encoding_format"] = "float",
            }.ToJsonString();
            PortalAiHostReply reply;
            try { reply = await PortalAiHostApi.SendAsync(_http, HttpMethod.Post, state.Endpoint!.BaseUrl + "/v1/embeddings", token, body, TimeSpan.FromSeconds(60), ct, priority: "batch"); }
            catch (HttpRequestException) { Forget(); return null; }
            if (!reply.Ok) { Forget(); return null; }
            if (Parse(reply.Body, chunk.Count) is not { } parsed) return null;
            if (model is not null && model != parsed.Model) return null;
            model = parsed.Model;
            all.AddRange(parsed.Vectors);
        }
        if (all.Count != texts.Count || all.Any(v => v.Length != all[0].Length)) return null;
        return new EmbeddingBatch(model ?? "", all);
    }

    internal static EmbeddingBatch? Parse(string json, int expected)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() != expected) return null;
            var vectors = new float[expected][];
            foreach (var item in data.EnumerateArray())
            {
                var index = item.TryGetProperty("index", out var ix) && ix.TryGetInt32(out var n) ? n : -1;
                if (index < 0 || index >= expected || vectors[index] is not null) return null;
                if (!item.TryGetProperty("embedding", out var emb) || emb.ValueKind != JsonValueKind.Array) return null;
                var v = new float[emb.GetArrayLength()];
                var at = 0;
                foreach (var x in emb.EnumerateArray()) v[at++] = x.GetSingle();
                if (v.Length == 0 || !Normalize(v)) return null;
                vectors[index] = v;
            }
            var model = root.TryGetProperty("model", out var mo) && mo.ValueKind == JsonValueKind.String ? mo.GetString() ?? "" : "";
            return new EmbeddingBatch(model, vectors);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { return null; }
    }

    private static bool Normalize(float[] v)
    {
        double sum = 0;
        foreach (var x in v) sum += (double)x * x;
        if (sum <= 0 || double.IsNaN(sum) || double.IsInfinity(sum)) return false;
        var inv = (float)(1 / Math.Sqrt(sum));
        for (var i = 0; i < v.Length; i++) v[i] *= inv;
        return true;
    }
}
