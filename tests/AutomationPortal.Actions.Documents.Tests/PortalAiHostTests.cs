using System.Net;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;

namespace AutomationPortal.Actions.Documents.Tests;

[CollectionDefinition("PortalAiHostState", DisableParallelization = true)]
public sealed class PortalAiHostStateCollection { }

[Collection("PortalAiHostState")]
public sealed class PortalAiHostTests : IDisposable
{
    private const string Instance = "6f1c2a9e4b7d4c0f9a51d3e8b2c47a10";
    private const string Caps = """
        {"api_version":"1","capabilities_revision":7,"chat":{"available":true,
         "model":{"id":"qwen3","display_name":"Qwen3 30B"},"context_tokens":32768,"max_output_tokens":8192,
         "features":{"streaming":true,"json_object":true,"json_schema":false,"tools":true}}}
        """;

    public PortalAiHostTests() => PortalAiHostStatus.Reset();
    public void Dispose() => PortalAiHostStatus.Reset();

    private static HttpResponseMessage Ping(string instance = Instance) =>
        StubHandler.Json($"{{\"service\":\"portal-ai-host\",\"api_version\":\"1\",\"host_version\":\"1.0.0\",\"instance_id\":\"{instance}\"}}");

    private static PortalAiHostDiscovery Discovery(HttpClient http, string? env = null, string? localAppData = null) =>
        new(http, name => name == PortalAiHostConstants.UrlEnvironmentVariable ? env : null,
            localAppData ?? Path.Combine(Path.GetTempPath(), "ap-no-such-" + Guid.NewGuid().ToString("N")), () => Task.FromResult(true));

    private static string TempPackages(string json)
    {
        var root = Path.Combine(Path.GetTempPath(), "ap-host-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, "Packages", "Some.Host_abc123", "LocalState", "discovery");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "host.json"), json);
        return root;
    }

    // ---- discovery ---------------------------------------------------------------------------------

    [Fact]
    public async Task EnvironmentOverrideWinsAndMustBeLoopback()
    {
        var h = new StubHandler { Respond = r => r.RequestUri!.Port == 50000 ? Ping() : new HttpResponseMessage(HttpStatusCode.NotFound) };
        var found = await Discovery(new HttpClient(h), env: "http://127.0.0.1:50000").DiscoverAsync();
        Assert.Equal("http://127.0.0.1:50000", found?.BaseUrl);

        var elsewhere = new StubHandler { Respond = _ => Ping() };
        var remote = await Discovery(new HttpClient(elsewhere), env: "http://example.com:50000").DiscoverAsync();
        Assert.DoesNotContain(elsewhere.Calls, c => c.Request.RequestUri!.Host == "example.com");
        Assert.Equal($"http://127.0.0.1:{PortalAiHostConstants.DefaultPort}", remote?.BaseUrl);
    }

    [Fact]
    public async Task DiscoveryFileIsReadAndTheInstanceMustMatch()
    {
        var root = TempPackages($"{{\"schema\":1,\"service\":\"portal-ai-host\",\"base_url\":\"http://127.0.0.1:41875\",\"port\":41875,\"instance_id\":\"{Instance}\"}}");
        try
        {
            var h = new StubHandler { Respond = r => r.RequestUri!.Port == 41875 ? Ping() : new HttpResponseMessage(HttpStatusCode.NotFound) };
            var found = await Discovery(new HttpClient(h), localAppData: root).DiscoverAsync();
            Assert.Equal("http://127.0.0.1:41875", found?.BaseUrl);
            Assert.Equal(Instance, found?.InstanceId);

            var stale = new StubHandler { Respond = r => r.RequestUri!.Port == 41875 ? Ping("another-instance") : new HttpResponseMessage(HttpStatusCode.NotFound) };
            Assert.Null(await Discovery(new HttpClient(stale), localAppData: root).DiscoverAsync());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FileWithOnlyAPortStillWorks()
    {
        var root = TempPackages($"{{\"schema\":1,\"service\":\"portal-ai-host\",\"port\":41877,\"instance_id\":\"{Instance}\"}}");
        try
        {
            var h = new StubHandler { Respond = r => r.RequestUri!.Port == 41877 ? Ping() : new HttpResponseMessage(HttpStatusCode.NotFound) };
            Assert.Equal("http://127.0.0.1:41877", (await Discovery(new HttpClient(h), localAppData: root).DiscoverAsync())?.BaseUrl);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DefaultPortIsTheLastResortAndAWrongServiceIsRefused()
    {
        var h = new StubHandler { Respond = _ => Ping() };
        Assert.Equal("http://127.0.0.1:41870", (await Discovery(new HttpClient(h)).DiscoverAsync())?.BaseUrl);

        var other = new StubHandler { Respond = _ => StubHandler.Json("{\"service\":\"something-else\"}") };
        Assert.Null(await Discovery(new HttpClient(other)).DiscoverAsync());
    }

    // ---- capabilities ------------------------------------------------------------------------------

    [Fact]
    public void CapabilitiesAreReadAndMissingFeaturesAreNotAssumed()
    {
        var c = PortalAiHostCapabilities.Parse(Caps);
        Assert.True(c.ChatAvailable);
        Assert.Equal(32768, c.ContextTokens);
        Assert.Equal("Qwen3 30B", c.ModelName);
        Assert.True(c.Tools);
        Assert.Equal(AiJsonMode.JsonObject, c.JsonMode);

        var bare = PortalAiHostCapabilities.Parse("""{"chat":{"available":true}}""");
        Assert.Equal(AiJsonMode.None, bare.JsonMode);
        Assert.False(bare.Streaming);
        Assert.Equal(0, bare.ContextTokens);

        var none = PortalAiHostCapabilities.Parse("""{"chat":{"available":false,"reason":"no_model"}}""");
        Assert.False(none.ChatAvailable);
        Assert.Equal("no_model", none.UnavailableReason);
    }

    // ---- approval ----------------------------------------------------------------------------------

    private static (PortalAiHostSession Session, InMemorySecretStore Secrets, StubHandler Handler) Session(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var h = new StubHandler { Respond = respond };
        var http = new HttpClient(h);
        var secrets = new InMemorySecretStore();
        return (new PortalAiHostSession(http, secrets, Discovery(http), (_, _) => Task.CompletedTask), secrets, h);
    }

    [Fact]
    public async Task AccessIsRequestedThenPolledUntilApprovedAndTheTokenIsStored()
    {
        var polls = 0;
        var (session, secrets, h) = Session(r =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path == "/host/ping") return Ping();
            if (r.Method == HttpMethod.Post && path == "/host/auth/requests")
                return StubHandler.Json("""{"request_id":"ar_1","status":"pending","poll_wait_ms":25000}""", HttpStatusCode.Accepted);
            if (path == "/host/auth/requests/ar_1")
                return StubHandler.Json(++polls < 3 ? """{"status":"pending"}""" : """{"status":"approved","token":"pah_secret","token_id":"tk_1"}""");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var result = await session.RequestAccessAsync("1.2.3");

        Assert.Equal(PortalAiHostAccessOutcome.Approved, result.Outcome);
        Assert.Equal(3, polls);
        Assert.Equal("pah_secret", secrets.Get(PortalAiHostSession.TokenName));
        Assert.False(PortalAiHostStatus.Pending);
        var post = h.Calls.Single(c => c.Request.Method == HttpMethod.Post).Body;
        Assert.Contains("\"app_id\":\"" + PortalAiHostConstants.AppId + "\"", post);
        Assert.Contains("wait_ms=25000", h.Calls.First(c => c.Request.RequestUri!.AbsolutePath.EndsWith("ar_1")).Request.RequestUri!.Query);
        Assert.DoesNotContain(h.Calls, c => c.Request.Headers.Authorization is not null);
    }

    [Theory]
    [InlineData("denied", PortalAiHostAccessOutcome.Denied)]
    [InlineData("expired", PortalAiHostAccessOutcome.Expired)]
    public async Task DeniedOrExpiredRequestsStoreNothing(string status, PortalAiHostAccessOutcome expected)
    {
        var (session, secrets, _) = Session(r => r.RequestUri!.AbsolutePath switch
        {
            "/host/ping" => Ping(),
            "/host/auth/requests" => StubHandler.Json("""{"request_id":"ar_2","status":"pending"}""", HttpStatusCode.Accepted),
            _ => StubHandler.Json($"{{\"status\":\"{status}\"}}"),
        });
        Assert.Equal(expected, (await session.RequestAccessAsync("1.0.0")).Outcome);
        Assert.Null(secrets.Get(PortalAiHostSession.TokenName));
    }

    [Fact]
    public async Task AccessIsNeverRequestedWhenOnlyLookingAtTheState()
    {
        var (session, _, h) = Session(r => r.RequestUri!.AbsolutePath == "/host/ping" ? Ping() : new HttpResponseMessage(HttpStatusCode.NotFound));
        var state = await session.GetStateAsync();
        Assert.Equal(PortalAiHostPhase.NeedsAccess, state.Phase);
        Assert.DoesNotContain(h.Calls, c => c.Request.Method == HttpMethod.Post);
    }

    // ---- revocation --------------------------------------------------------------------------------

    private static string Err(string code) => $"{{\"error\":{{\"message\":\"x\",\"type\":\"authentication_error\",\"code\":\"{code}\"}}}}";

    [Theory]
    [InlineData("token_revoked")]
    [InlineData("token_invalid")]
    public async Task A401OnTheStateCheckDeletesTheTokenAndSaysSo(string code)
    {
        var (session, secrets, _) = Session(r => r.RequestUri!.AbsolutePath == "/host/ping" ? Ping() : StubHandler.Json(Err(code), HttpStatusCode.Unauthorized));
        secrets.Set(PortalAiHostSession.TokenName, "pah_old");

        var state = await session.GetStateAsync();

        Assert.Equal(PortalAiHostPhase.AccessRemoved, state.Phase);
        Assert.Equal(PortalAiHostStatus.AccessRemovedMessage, state.Message);
        Assert.Null(secrets.Get(PortalAiHostSession.TokenName));
        Assert.Equal(PortalAiHostPhase.AccessRemoved, (await session.GetStateAsync()).Phase);
    }

    [Fact]
    public async Task A401OnARequestDeletesTheTokenThroughTheStoreAndMarksTheProviderNotReady()
    {
        var h = new StubHandler
        {
            Respond = r => r.RequestUri!.AbsolutePath switch
            {
                "/host/ping" => Ping(),
                "/host/capabilities" => StubHandler.Json(Caps),
                _ => StubHandler.Json(Err("token_revoked"), HttpStatusCode.Unauthorized),
            },
        };
        var secrets = new InMemorySecretStore();
        var dir = Path.Combine(Path.GetTempPath(), "ap-store-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AiProviderStore(Path.Combine(dir, "ai-providers.json"), secrets, () => new HttpClient(h));
            store.SetKey(AiProviderIds.PortalAiHost, "pah_old");
            Assert.True(store.HasHostToken);

            var provider = store.Create(AiProviderIds.PortalAiHost);
            Assert.True((await provider.IsAvailableAsync()).IsAvailable);
            var ex = await Assert.ThrowsAsync<ActionException>(() =>
                provider.CompleteAsync("", new AiRequest("sys", new AiPart[] { new AiPart.Text("hi") }, AiEffort.None, AiShape.Text), default));

            Assert.Equal(PortalAiHostStatus.AccessRemovedMessage, ex.Message);
            Assert.False(store.HasHostToken);
            Assert.NotNull(store.SetupProblem(AiProviderIds.PortalAiHost));
            Assert.False(PortalAiHostStatus.ReadyHint);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    // ---- provider ----------------------------------------------------------------------------------

    private static AiProviderContext Context(HttpClient http, string? key) =>
        new(PortalAiHostProvider.Info, new AiProviderSettings { Enabled = true }, key, http);

    [Fact]
    public async Task ProviderExplainsWhyItCannotAnswer()
    {
        var down = new HttpClient(new StubHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound) });
        var gone = new PortalAiHostProvider(Context(down, "tok"), Discovery(down));
        Assert.Equal("Portal AI Host isn't running", (await gone.IsAvailableAsync()).Reason);

        var up = new HttpClient(new StubHandler { Respond = r => r.RequestUri!.AbsolutePath == "/host/ping" ? Ping() : new HttpResponseMessage(HttpStatusCode.NotFound) });
        Assert.Contains("allow", (await new PortalAiHostProvider(Context(up, null), Discovery(up)).IsAvailableAsync()).Reason, StringComparison.OrdinalIgnoreCase);

        PortalAiHostStatus.SetPending(true);
        Assert.Equal("Waiting for you to allow Automation Portal in Portal AI Host", (await new PortalAiHostProvider(Context(up, null), Discovery(up)).IsAvailableAsync()).Reason);
        PortalAiHostStatus.SetPending(false);

        var noModel = new HttpClient(new StubHandler
        {
            Respond = r => r.RequestUri!.AbsolutePath == "/host/ping" ? Ping() : StubHandler.Json("""{"chat":{"available":false,"reason":"no_model"}}"""),
        });
        Assert.Equal("No model is loaded in Portal AI Host", (await new PortalAiHostProvider(Context(noModel, "tok"), Discovery(noModel)).IsAvailableAsync()).Reason);
    }

    [Fact]
    public async Task ProviderSendsTheBearerTokenAndAsksForTheDefaultModel()
    {
        var h = new StubHandler
        {
            Respond = r => r.RequestUri!.AbsolutePath switch
            {
                "/host/ping" => Ping(),
                "/host/capabilities" => StubHandler.Json(Caps),
                _ => StubHandler.Json(StubHandler.Chat("Hello")),
            },
        };
        var http = new HttpClient(h);
        var provider = new PortalAiHostProvider(Context(http, "pah_tok"), Discovery(http));

        var reply = await provider.CompleteAsync("", new AiRequest("sys", new AiPart[] { new AiPart.Text("hi") }, AiEffort.None, AiShape.Text), default);

        Assert.Equal("Hello", reply);
        var chat = h.Calls.Single(c => c.Request.RequestUri!.AbsolutePath == "/v1/chat/completions");
        Assert.Equal("Bearer pah_tok", chat.Request.Headers.Authorization?.ToString());
        Assert.Contains("\"model\":\"default\"", chat.Body);
        Assert.Equal("127.0.0.1", chat.Request.RequestUri!.Host);
        Assert.True(PortalAiHostStatus.ReadyHint);
    }

    [Fact]
    public async Task ForgetAsksTheHostToDropTheTokenAndDeletesIt()
    {
        var (session, secrets, h) = Session(r => r.RequestUri!.AbsolutePath == "/host/ping" ? Ping() : new HttpResponseMessage(HttpStatusCode.NoContent));
        secrets.Set(PortalAiHostSession.TokenName, "pah_tok");
        await session.ForgetAsync();
        Assert.Null(secrets.Get(PortalAiHostSession.TokenName));
        Assert.Contains(h.Calls, c => c.Request.Method == HttpMethod.Delete && c.Request.RequestUri!.AbsolutePath == "/host/auth/token");
    }

    // ---- default choice and privacy ----------------------------------------------------------------

    [Fact]
    public void TheHostIsTheDefaultOnlyWhenReadyAndNothingWasPicked()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ap-store-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AiProviderStore(Path.Combine(dir, "ai-providers.json"), new InMemorySecretStore());
            Assert.Equal("claude", store.DefaultProviderId);

            store.SetKey(AiProviderIds.PortalAiHost, "pah_tok");
            Assert.Equal("claude", store.DefaultProviderId);   // token but not known to be ready
            PortalAiHostStatus.SetReady(true);
            Assert.Equal(AiProviderIds.PortalAiHost, store.DefaultProviderId);

            store.Update(c => { c.DefaultProvider = AiProviderIds.Claude; c.DefaultChosen = true; });
            Assert.Equal("claude", store.DefaultProviderId);   // an explicit pick is kept

            store.Update(c => { c.DefaultProvider = "openai"; c.DefaultChosen = false; });
            Assert.Equal("openai", store.DefaultProviderId);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void PrivacyLabelFollowsTheProvider()
    {
        foreach (var id in new[] { AiProviderIds.PortalAiHost, AiProviderIds.Ollama, AiProviderIds.LmStudio })
            Assert.Equal("Stays on this PC", AiPrivacy.Label(AiProviderRegistry.Get(id)!));
        Assert.Equal("Stays on this PC", AiPrivacy.Label(AiProviderRegistry.Get(AiProviderIds.Ollama)! with { Id = AiProviderIds.WindowsAi, IsLocal = true }));

        Assert.Equal("Sends text to Claude", AiPrivacy.Label(AiProviderRegistry.Get(AiProviderIds.Claude)!));
        Assert.Equal("Sends text to ChatGPT", AiPrivacy.Label(AiProviderRegistry.Get(AiProviderIds.OpenAi)!));
        Assert.Equal("Sends text to the AI service you set up", AiPrivacy.Label(AiProviderRegistry.Get(AiProviderIds.Custom)!));
    }

    [Fact]
    public void OnlyAiActionsGetAPrivacyNote()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ap-store-" + Guid.NewGuid().ToString("N"));
        try
        {
            var ai = new AutomationPortal.Core.ActionDescriptor { Id = "ai.summarize", Title = "Summarize", Category = "AI", NeedsInternet = true };
            var web = new AutomationPortal.Core.ActionDescriptor { Id = "web.getUrl", Title = "Get", Category = "Web", NeedsInternet = true };
            Assert.True(AiPrivacy.IsAiAction(ai));
            Assert.False(AiPrivacy.IsAiAction(web));
            Assert.Null(AiPrivacy.NoteFor(web, null!));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
