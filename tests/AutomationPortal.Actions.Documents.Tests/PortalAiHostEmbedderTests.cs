using System.Net;
using System.Text;
using Xunit;

namespace AutomationPortal.Actions.Documents.Tests;

[Collection("ai-statics")]
public class PortalAiHostEmbedderTests
{
    private const string Caps = """
        {"capabilities_revision":7,"chat":{"available":true,"features":{}},
         "embeddings":{"available":true,"model":{"id":"embed-small-q8"},"dimensions":3,"max_input_tokens":512,"max_batch_inputs":2,"normalized":true}}
        """;

    private const string CapsNoEmbeddings = """{"capabilities_revision":7,"chat":{"available":true,"features":{}}}""";

    private sealed class Handler : HttpMessageHandler
    {
        public List<(string Url, string Body, string? Priority)> Requests { get; } = new();
        public Func<string, HttpResponseMessage>? Respond { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.RequestUri!.ToString(), body, request.Headers.TryGetValues("X-Portal-Priority", out var p) ? p.First() : null));
            return Respond!(body);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string Vectors(int count, string model = "embed-small-q8") =>
        $$"""{"model":"{{model}}","data":[{{string.Join(",", Enumerable.Range(0, count).Select(i => $$"""{"index":{{i}},"embedding":[3,4,0]}"""))}}]}""";

    private static PortalAiHostEmbedder Make(string caps, Handler handler, string? token = "tok", bool ready = true)
    {
        var parsed = PortalAiHostCapabilities.Parse(caps);
        var state = new PortalAiHostState(ready ? PortalAiHostPhase.Ready : PortalAiHostPhase.NotRunning, "x", parsed, new PortalAiHostEndpoint("http://127.0.0.1:1", null, null));
        return new PortalAiHostEmbedder(_ => Task.FromResult(state), () => token, new HttpClient(handler));
    }

    [Fact]
    public void Capabilities_report_embeddings_only_when_the_host_says_so()
    {
        var yes = PortalAiHostCapabilities.Parse(Caps);
        Assert.True(yes.EmbeddingsAvailable);
        Assert.Equal(2, yes.EmbeddingsMaxBatch);
        Assert.False(PortalAiHostCapabilities.Parse(CapsNoEmbeddings).EmbeddingsAvailable);
    }

    [Fact]
    public async Task Texts_are_sent_in_host_sized_batches_as_background_work_and_come_back_normalised()
    {
        var handler = new Handler { Respond = body => Json(Vectors(body.Contains("\"c\"") ? 1 : 2)) };
        var embedder = Make(Caps, handler);

        var batch = await embedder.EmbedAsync(new[] { "a", "b", "c" }, default);

        Assert.NotNull(batch);
        Assert.Equal(3, batch!.Vectors.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => { Assert.EndsWith("/v1/embeddings", r.Url); Assert.Equal("batch", r.Priority); });
        Assert.Equal(0.6f, batch.Vectors[0][0], 3);
        Assert.Equal(0.8f, batch.Vectors[0][1], 3);
        Assert.Equal("embed-small-q8", batch.Model);
    }

    [Fact]
    public async Task No_token_or_no_embeddings_or_host_not_ready_means_unavailable_and_no_request()
    {
        var handler = new Handler { Respond = _ => Json(Vectors(1)) };
        Assert.False(await Make(Caps, handler, token: null).IsAvailableAsync(default));
        Assert.False(await Make(CapsNoEmbeddings, handler).IsAvailableAsync(default));
        Assert.False(await Make(Caps, handler, ready: false).IsAvailableAsync(default));
        Assert.Null(await Make(CapsNoEmbeddings, handler).EmbedAsync(new[] { "a" }, default));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_host_error_or_a_wrong_count_gives_null_not_an_exception()
    {
        var failing = new Handler { Respond = _ => Json("""{"error":{"code":"queue_full"}}""", HttpStatusCode.ServiceUnavailable) };
        Assert.Null(await Make(Caps, failing).EmbedAsync(new[] { "a" }, default));
        var wrong = new Handler { Respond = _ => Json(Vectors(1)) };
        Assert.Null(await Make(Caps, wrong).EmbedAsync(new[] { "a", "b" }, default));
    }

    [Fact]
    public async Task Very_long_texts_are_cut_before_they_are_sent()
    {
        var handler = new Handler { Respond = _ => Json(Vectors(1)) };
        await Make(Caps, handler).EmbedAsync(new[] { new string('x', 5000) }, default);
        Assert.True(handler.Requests[0].Body.Length < PortalAiHostEmbedder.MaxInputChars + 200);
    }
}
