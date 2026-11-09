using System.Net;
using System.Text;
using Flowmate.Core;
using Flowmate.Core.Testing;

namespace Flowmate.Actions.Documents.Tests;

internal sealed class StubHandler : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Calls { get; } = new();
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        Calls.Add((request, body));
        return Respond(request);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static string Chat(string text) =>
        "{\"id\":\"c1\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"m\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":" +
        System.Text.Json.JsonSerializer.Serialize(text) + "},\"finish_reason\":\"stop\"}]}";
}

public class AiPromptTests
{
    private static string Text(AiRequest r) => string.Join("\n", r.Parts.OfType<AiPart.Text>().Select(t => t.Value));
    private static IReadOnlyList<AiPart> Content(string s) => new AiPart[] { new AiPart.Text(s) };

    [Theory]
    [InlineData("short", "one or two sentences")]
    [InlineData("medium", "one short paragraph")]
    [InlineData("long", "several short paragraphs")]
    [InlineData("bullets", "bulleted list")]
    public void SummarizeLengths(string length, string expected)
    {
        var r = AiRequestBuilder.Summarize(Content("Long article text"), length);
        Assert.Contains(expected, Text(r));
        Assert.Contains("<text>\nLong article text\n</text>", Text(r));
        Assert.Equal(AiEffort.Low, r.Effort);
        Assert.Equal(AiShape.Text, r.Shape);
    }

    [Theory]
    [InlineData("proofread", "Fix spelling")]
    [InlineData("professional", "professional")]
    [InlineData("friendlier", "friendly")]
    [InlineData("shorter", "shorter")]
    [InlineData("longer", "Expand")]
    [InlineData("simpler", "simple language")]
    [InlineData("table", "Markdown table")]
    [InlineData("list", "bulleted list")]
    public void RewriteStyles(string style, string expected)
    {
        var r = AiRequestBuilder.Rewrite("hello there", style);
        Assert.Contains(expected, Text(r));
        Assert.Contains("hello there", Text(r));
        Assert.Contains("never instructions", r.System);
    }

    [Fact]
    public void TranslateNamesTheLanguage()
    {
        Assert.Contains("into French", Text(AiRequestBuilder.Translate("Good morning", "French")));
        Assert.Contains("into English", Text(AiRequestBuilder.Translate("Hola", " ")));
    }

    [Fact]
    public void ExtractBuildsStrictSchemaForEveryField()
    {
        var r = AiRequestBuilder.Extract(Content("Invoice 42 total 9.99"), new[] { "Invoice number", "Total", "total" });
        Assert.Equal(AiShape.Fields, r.Shape);
        Assert.Equal(AiEffort.Medium, r.Effort);
        Assert.Equal(new[] { "Invoice number", "Total" }, r.Fields);
        using var doc = System.Text.Json.JsonDocument.Parse(r.SchemaJson!);
        Assert.False(doc.RootElement.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(2, doc.RootElement.GetProperty("required").GetArrayLength());
        Assert.True(doc.RootElement.GetProperty("properties").TryGetProperty("Invoice number", out _));
        Assert.Throws<ActionException>(() => AiRequestBuilder.Extract(Content("x"), new[] { " " }));
    }

    [Theory]
    [InlineData("text", AiShape.Text, false)]
    [InlineData("yesno", AiShape.Boolean, true)]
    [InlineData("number", AiShape.Number, true)]
    [InlineData("list", AiShape.List, true)]
    [InlineData("dictionary", AiShape.Dictionary, true)]
    public void AskAnswerTypes(string answerAs, AiShape shape, bool hasSchema)
    {
        var r = AiRequestBuilder.Ask("Is it raining?", Content("It is wet outside"), answerAs);
        Assert.Equal(shape, r.Shape);
        Assert.Equal(hasSchema, r.SchemaJson is not null);
        Assert.Equal(AiEffort.Medium, r.Effort);
        Assert.Contains("Is it raining?", Text(r));
    }

    [Fact]
    public void AttachmentsComeBeforeTheInstruction()
    {
        var parts = new AiPart[] { new AiPart.Image(new byte[] { 1 }, "image/png"), new AiPart.Pdf(new byte[] { 2 }), new AiPart.Text("body") };
        var r = AiRequestBuilder.Ask("What is this?", parts, "text");
        Assert.IsType<AiPart.Image>(r.Parts[0]);
        Assert.IsType<AiPart.Pdf>(r.Parts[1]);
        Assert.IsType<AiPart.Text>(r.Parts[^1]);
    }

    [Theory]
    [InlineData("short", "one or two sentences")]
    [InlineData("alt", "alt text")]
    [InlineData("text", "Transcribe")]
    public void DescribeImageDetails(string detail, string expected)
    {
        var r = AiRequestBuilder.DescribeImage(new AiPart.Image(new byte[] { 1 }, "image/png"), detail, null);
        Assert.Contains(expected, Text(r));
        Assert.IsType<AiPart.Image>(r.Parts[0]);
        Assert.Contains("colour of the car", Text(AiRequestBuilder.DescribeImage(new AiPart.Image(new byte[] { 1 }, "image/png"), "short", "What is the colour of the car?")));
    }
}

public class AiAnswerParserTests
{
    [Fact]
    public void Booleans()
    {
        Assert.True(AiAnswerParser.Parse(AiShape.Boolean, "{\"answer\": true}").AsBool());
        Assert.False(AiAnswerParser.Parse(AiShape.Boolean, "```json\n{\"answer\": false}\n```").AsBool(true));
        Assert.True(AiAnswerParser.Parse(AiShape.Boolean, "Yes, it is.").AsBool());
        Assert.False(AiAnswerParser.Parse(AiShape.Boolean, "No.").AsBool(true));
    }

    [Fact]
    public void Numbers()
    {
        Assert.Equal(42, AiAnswerParser.Parse(AiShape.Number, "{\"answer\": 42}").AsNumber());
        Assert.Equal(12.5, AiAnswerParser.Parse(AiShape.Number, "The answer is 12.5 kg").AsNumber());
        Assert.Throws<ActionException>(() => AiAnswerParser.Parse(AiShape.Number, "no idea"));
    }

    [Fact]
    public void Lists()
    {
        Assert.Equal(new[] { "a", "b" }, AiAnswerParser.Parse(AiShape.List, "{\"items\":[\"a\",\"b\"]}").AsTexts());
        Assert.Equal(new[] { "one", "two", "three" }, AiAnswerParser.Parse(AiShape.List, "- one\n* two\n3. three\n").AsTexts());
    }

    [Fact]
    public void Dictionaries()
    {
        var d = (DictionaryItem)AiAnswerParser.Parse(AiShape.Dictionary, "{\"entries\":[{\"key\":\"city\",\"value\":\"Oslo\"},{\"key\":\"pop\",\"value\":\"700k\"}]}")[0];
        Assert.Equal("Oslo", d.Get("city").AsText());
        var plain = (DictionaryItem)AiAnswerParser.Parse(AiShape.Dictionary, "Here you go: {\"a\": 1}")[0];
        Assert.Equal(1, plain.Get("a").AsNumber());
        Assert.Throws<ActionException>(() => AiAnswerParser.Parse(AiShape.Dictionary, "just words"));
    }

    [Fact]
    public void FieldsAreOrderedAndMissingOnesAreEmpty()
    {
        var d = (DictionaryItem)AiAnswerParser.Parse(AiShape.Fields, "{\"total\":\"9.99\",\"Name\":null}", new[] { "Name", "Total", "Date" })[0];
        Assert.Equal(new[] { "Name", "Total", "Date" }, d.Keys.ToArray());
        Assert.Equal("", d.Get("Name").AsText());
        Assert.Equal("9.99", d.Get("Total").AsText());
        Assert.Equal("", d.Get("Date").AsText());
        Assert.Throws<ActionException>(() => AiAnswerParser.Parse(AiShape.Fields, "nothing", new[] { "x" }));
    }

    [Fact]
    public void TextIsTrimmed() => Assert.Equal("hi", AiAnswerParser.Parse(AiShape.Text, "  hi \n").AsText());
}

public class AiModelTests
{
    [Theory]
    [InlineData(AiModelTier.Best, "", AiModels.Best)]
    [InlineData(AiModelTier.Smart, "", AiModels.Smart)]
    [InlineData(AiModelTier.Fast, "", AiModels.Fast)]
    [InlineData(AiModelTier.Smart, "best", AiModels.Best)]
    [InlineData(AiModelTier.Best, "Fast", AiModels.Fast)]
    [InlineData(AiModelTier.Best, "claude-sonnet-5-5", "claude-sonnet-5-5")]
    public void ClaudeModelResolution(AiModelTier tier, string action, string expected)
    {
        var info = AiProviderRegistry.Get(AiProviderIds.Claude)!;
        Assert.Equal(expected, AiModels.ResolveModel(info, new AiProviderSettings(), tier, action));
    }

    [Fact]
    public void ModelIdsAreExact()
    {
        Assert.Equal("claude-opus-5-5", AiModels.Best);
        Assert.Equal("claude-sonnet-5-5", AiModels.Smart);
        Assert.Equal("claude-haiku-4-5", AiModels.Fast);
    }

    [Fact]
    public void OtherProvidersUseTheirDefaultModelAndAzureUsesTheDeployment()
    {
        var openai = AiProviderRegistry.Get(AiProviderIds.OpenAi)!;
        Assert.Equal("my-model", AiModels.ResolveModel(openai, new AiProviderSettings { Model = "my-model" }, AiModelTier.Best, ""));
        Assert.Equal("other", AiModels.ResolveModel(openai, new AiProviderSettings { Model = "my-model" }, AiModelTier.Best, "other"));
        var azure = AiProviderRegistry.Get(AiProviderIds.Azure)!;
        Assert.Equal("gpt-dep", AiModels.ResolveModel(azure, new AiProviderSettings { Deployment = "gpt-dep", Model = "ignored" }, AiModelTier.Best, ""));
    }

    [Fact]
    public void ClaudeRequestSkipsEffortOnHaikuAndSendsSchemaForStructuredAnswers()
    {
        var yesNo = AiRequestBuilder.Ask("q", new AiPart[] { new AiPart.Text("t") }, "yesno");
        var opus = ClaudeProvider.BuildParams(AiModels.Best, yesNo);
        var haiku = ClaudeProvider.BuildParams(AiModels.Fast, yesNo);
        Assert.NotNull(opus.OutputConfig!.Effort);
        Assert.Null(haiku.OutputConfig!.Effort);
        Assert.NotNull(haiku.OutputConfig!.Format);
        Assert.Equal(16000, opus.MaxTokens);
        Assert.Null(opus.Thinking);
        var plain = ClaudeProvider.BuildParams(AiModels.Fast, AiRequestBuilder.Summarize(new AiPart[] { new AiPart.Text("t") }, "short"));
        Assert.Null(plain.OutputConfig);
    }

    [Fact]
    public void ClaudeRequestCarriesImagesAndPdfs()
    {
        var req = AiRequestBuilder.Ask("What is this?", new AiPart[] { new AiPart.Image(new byte[] { 1, 2 }, "image/png"), new AiPart.Pdf(new byte[] { 3 }) }, "text");
        var p = ClaudeProvider.BuildParams(AiModels.Best, req);
        Assert.Single(p.Messages);
        Assert.Equal(3, ((System.Collections.IEnumerable)p.Messages[0].Content.Value!).Cast<object>().Count());
    }
}

public class OpenAiRequestTests
{
    [Fact]
    public void ImagesAreRejectedForProvidersWithoutVision()
    {
        var req = AiRequestBuilder.DescribeImage(new AiPart.Image(new byte[] { 1 }, "image/png"), "short", null);
        var ex = Assert.Throws<ActionException>(() => OpenAiProvider.Build(AiProviderRegistry.Get(AiProviderIds.DeepSeek)!, req));
        Assert.Contains("can't look at pictures", ex.Message);
        var (messages, _) = OpenAiProvider.Build(AiProviderRegistry.Get(AiProviderIds.OpenAi)!, req);
        Assert.Equal(2, messages.Count);
    }

    [Theory]
    [InlineData(AiProviderIds.OpenAi, true)]
    [InlineData(AiProviderIds.Gemini, true)]
    [InlineData(AiProviderIds.Ollama, true)]
    [InlineData(AiProviderIds.DeepSeek, true)]
    [InlineData(AiProviderIds.Kimi, true)]
    public void StructuredAnswersUseAJsonFormat(string provider, bool expected)
    {
        var req = AiRequestBuilder.Ask("q", new AiPart[] { new AiPart.Text("t") }, "number");
        var (_, options) = OpenAiProvider.Build(AiProviderRegistry.Get(provider)!, req);
        Assert.Equal(expected, options.ResponseFormat is not null);
        var (_, plain) = OpenAiProvider.Build(AiProviderRegistry.Get(provider)!, AiRequestBuilder.Ask("q", new AiPart[] { new AiPart.Text("t") }, "text"));
        Assert.Null(plain.ResponseFormat);
    }

    [Fact]
    public void PdfsAreSentAsText()
    {
        using var d = new TempDir();
        var pdf = Path.Combine(d.Path, "a.pdf");
        PdfEngine.Make(new PdfSource[] { new PdfSource.Text("Contract clause seven applies.") }, pdf, new PdfMakeOptions());
        var req = AiRequestBuilder.Summarize(new AiPart[] { new AiPart.Pdf(File.ReadAllBytes(pdf)), new AiPart.Text("extra") }, "short");
        var (messages, _) = OpenAiProvider.Build(AiProviderRegistry.Get(AiProviderIds.DeepSeek)!, req);
        var user = Assert.IsType<OpenAI.Chat.UserChatMessage>(messages[1]);
        Assert.Contains("Contract clause seven applies", string.Join(" ", user.Content.Select(c => c.Text)));
    }

    private static OpenAiProvider Make(string id, StubHandler handler, string? key = "sk-test", string? endpoint = null, string? deployment = null)
    {
        var info = AiProviderRegistry.Get(id)!;
        return new OpenAiProvider(new AiProviderContext(info, new AiProviderSettings { Endpoint = endpoint ?? info.DefaultEndpoint, Deployment = deployment, Enabled = true }, key, new HttpClient(handler)));
    }

    [Fact]
    public async Task CompletesAgainstTheConfiguredEndpoint()
    {
        var handler = new StubHandler { Respond = _ => StubHandler.Json(StubHandler.Chat("Hello!")) };
        var p = Make(AiProviderIds.Custom, handler, "sk-abc", "https://llm.example.com/v1");
        var reply = await p.CompleteAsync("my-model", AiRequestBuilder.Summarize(new AiPart[] { new AiPart.Text("t") }, "short"), default);
        Assert.Equal("Hello!", reply);
        var (request, body) = Assert.Single(handler.Calls);
        Assert.Equal("https://llm.example.com/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Equal("Bearer sk-abc", request.Headers.Authorization!.ToString());
        Assert.Contains("\"model\":\"my-model\"", body);
        Assert.DoesNotContain("response_format", body);
    }

    [Fact]
    public async Task StructuredRequestsSendAJsonSchemaAndRetryPlainOn400()
    {
        var calls = 0;
        var handler = new StubHandler
        {
            Respond = _ => ++calls == 1 ? StubHandler.Json("{\"error\":{\"message\":\"unsupported\"}}", HttpStatusCode.BadRequest) : StubHandler.Json(StubHandler.Chat("{\"answer\": true}")),
        };
        var p = Make(AiProviderIds.OpenAi, handler);
        var reply = await p.CompleteAsync("m", AiRequestBuilder.Ask("q", new AiPart[] { new AiPart.Text("t") }, "yesno"), default);
        Assert.Equal("{\"answer\": true}", reply);
        Assert.Equal(2, handler.Calls.Count);
        Assert.Contains("json_schema", handler.Calls[0].Body);
        Assert.DoesNotContain("response_format", handler.Calls[1].Body);
    }

    [Theory]
    [InlineData(401, "Your AI key isn't valid")]
    [InlineData(403, "Your AI key isn't valid")]
    [InlineData(429, "busy")]
    [InlineData(404, "isn't available")]
    public async Task HttpErrorsBecomePlainWords(int status, string expected)
    {
        var handler = new StubHandler { Respond = _ => StubHandler.Json("{\"error\":{\"message\":\"nope sk-secret\"}}", (HttpStatusCode)status) };
        var p = Make(AiProviderIds.OpenAi, handler, "sk-secret");
        var ex = await Assert.ThrowsAsync<ActionException>(() => p.CompleteAsync("m", AiRequestBuilder.Summarize(new AiPart[] { new AiPart.Text("t") }, "short"), default));
        Assert.Contains(expected, ex.Message);
        Assert.DoesNotContain("sk-secret", ex.Message);
    }

    [Fact]
    public async Task LocalServerNotRunningSaysToStartIt()
    {
        var handler = new StubHandler { Respond = _ => throw new HttpRequestException("connection refused") };
        var p = Make(AiProviderIds.Ollama, handler, null);
        var ex = await Assert.ThrowsAsync<ActionException>(() => p.CompleteAsync("llama", AiRequestBuilder.Summarize(new AiPart[] { new AiPart.Text("t") }, "short"), default));
        Assert.Contains("Start Ollama, then try again", ex.Message);
        var cloud = Make(AiProviderIds.OpenAi, handler);
        var ex2 = await Assert.ThrowsAsync<ActionException>(() => cloud.CompleteAsync("m", AiRequestBuilder.Summarize(new AiPart[] { new AiPart.Text("t") }, "short"), default));
        Assert.Contains("internet connection", ex2.Message);
    }

    [Fact]
    public async Task ModelListsComeFromTheModelsEndpoint()
    {
        var handler = new StubHandler { Respond = _ => StubHandler.Json("{\"object\":\"list\",\"data\":[{\"id\":\"models/gemini-b\",\"object\":\"model\",\"created\":1,\"owned_by\":\"g\"},{\"id\":\"gemini-a\",\"object\":\"model\",\"created\":1,\"owned_by\":\"g\"}]}") };
        var p = Make(AiProviderIds.Gemini, handler);
        Assert.Equal(new[] { "gemini-a", "gemini-b" }, await p.ListModelsAsync(default));
        Assert.EndsWith("/v1beta/openai/models", handler.Calls[0].Request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task EmptyModelAsksUserToChoose()
    {
        var p = Make(AiProviderIds.OpenAi, new StubHandler());
        var ex = await Assert.ThrowsAsync<ActionException>(() => p.CompleteAsync("", AiRequestBuilder.Summarize(new AiPart[] { new AiPart.Text("t") }, "short"), default));
        Assert.Contains("Choose a model", ex.Message);
    }

    [Fact]
    public async Task AzureUsesTheDeploymentAndApiKeyHeader()
    {
        var handler = new StubHandler { Respond = _ => StubHandler.Json(StubHandler.Chat("hi")) };
        var p = Make(AiProviderIds.Azure, handler, "azkey", "https://res.openai.azure.com", "my-deployment");
        Assert.Equal(new[] { "my-deployment" }, await p.ListModelsAsync(default));
        await p.CompleteAsync("my-deployment", AiRequestBuilder.Summarize(new AiPart[] { new AiPart.Text("t") }, "short"), default);
        var (req, _) = Assert.Single(handler.Calls);
        Assert.Contains("/openai/deployments/my-deployment/chat/completions", req.RequestUri!.AbsolutePath);
        Assert.True(req.Headers.Contains("api-key"));
    }

    [Fact]
    public async Task ClaudeErrorsAreMappedToo()
    {
        var handler = new StubHandler { Respond = _ => StubHandler.Json("{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}", HttpStatusCode.Unauthorized) };
        var info = AiProviderRegistry.Get(AiProviderIds.Claude)!;
        var claude = new ClaudeProvider(new AiProviderContext(info, new AiProviderSettings(), "sk-ant-secret", new HttpClient(handler)));
        var ex = await Assert.ThrowsAsync<ActionException>(() => claude.CompleteAsync(AiModels.Best, AiRequestBuilder.Summarize(new AiPart[] { new AiPart.Text("t") }, "short"), default));
        Assert.Contains("Your AI key isn't valid", ex.Message);
        Assert.NotEmpty(handler.Calls);
    }
}

[Collection("ai-statics")]
public class AiProviderStoreTests
{
    private static (AiProviderStore Store, InMemorySecretStore Secrets, string Path, TempDir Dir) Make()
    {
        var dir = new TempDir();
        var secrets = new InMemorySecretStore();
        var path = Path.Combine(dir.Path, "ai-providers.json");
        return (new AiProviderStore(path, secrets), secrets, path, dir);
    }

    [Fact]
    public void ListsEveryBuiltInProvider()
    {
        var ids = AiProviderRegistry.All.Select(p => p.Id).ToArray();
        foreach (var id in new[] { "claude", "openai", "gemini", "deepseek", "kimi", "azure", "ollama", "lmstudio", "custom" }) Assert.Contains(id, ids);
        Assert.Equal("https://api.deepseek.com/v1", AiProviderRegistry.Get("deepseek")!.DefaultEndpoint);
        Assert.Equal("https://api.moonshot.ai/v1", AiProviderRegistry.Get("kimi")!.DefaultEndpoint);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai/", AiProviderRegistry.Get("gemini")!.DefaultEndpoint);
        Assert.Equal("http://localhost:11434/v1", AiProviderRegistry.Get("ollama")!.DefaultEndpoint);
        Assert.Equal("http://localhost:1234/v1", AiProviderRegistry.Get("lmstudio")!.DefaultEndpoint);
        Assert.Contains("Copilot", AiProviderRegistry.Get("azure")!.Name);
    }

    [Fact]
    public void ClaudeIsConfiguredByTheOldKeyAndNewKeyWins()
    {
        var (store, secrets, _, dir) = Make(); using (dir)
        {
            Assert.False(store.IsConfigured("claude"));
            Assert.Equal("Add your AI key in Settings › AI to use AI actions.", store.SetupProblem("claude"));
            secrets.Set("ai.apiKey", "old");
            Assert.True(store.IsConfigured("claude"));
            Assert.Equal("old", store.GetKey("claude"));
            store.SetKey("claude", " new ");
            Assert.Equal("new", store.GetKey("claude"));
            Assert.Equal("new", secrets.Get("ai.claude.apiKey"));
            Assert.Equal("claude", store.DefaultProviderId);
        }
    }

    [Fact]
    public void SettingsRoundTripAndKeysNeverLandInTheFile()
    {
        var (store, secrets, path, dir) = Make(); using (dir)
        {
            store.SetKey("openai", "sk-very-secret");
            store.Update(c =>
            {
                c.DefaultProvider = "openai";
                c.Providers["openai"] = new AiProviderSettings { Enabled = true, Model = "some-model" };
                c.Providers["azure"] = new AiProviderSettings { Enabled = true, Endpoint = "https://r.openai.azure.com", Deployment = "dep" };
            });
            Assert.DoesNotContain("sk-very-secret", File.ReadAllText(path));
            var reopened = new AiProviderStore(path, secrets);
            Assert.Equal("openai", reopened.DefaultProviderId);
            Assert.Equal("some-model", reopened.Get("openai").Model);
            Assert.Equal("dep", reopened.Get("azure").Deployment);
            Assert.True(reopened.IsConfigured("openai"));
            Assert.False(reopened.IsConfigured("azure"));   // no key yet
            reopened.SetKey("azure", "k");
            Assert.True(reopened.IsConfigured("azure"));
        }
    }

    [Fact]
    public void RequirementsPerProviderAreExplainedInPlainWords()
    {
        var (store, _, _, dir) = Make(); using (dir)
        {
            Assert.Contains("Turn on ChatGPT", store.SetupProblem("openai"));
            store.Update(c => c.Providers["openai"] = new AiProviderSettings { Enabled = true });
            Assert.Contains("Add your ChatGPT", store.SetupProblem("openai"));
            store.Update(c => c.Providers["ollama"] = new AiProviderSettings { Enabled = true });
            Assert.Null(store.SetupProblem("ollama"));
            Assert.Equal("http://localhost:11434/v1", store.EffectiveEndpoint("ollama"));
            store.Update(c => c.Providers["custom"] = new AiProviderSettings { Enabled = true });
            Assert.Contains("address", store.SetupProblem("custom"));
            store.Update(c => c.Providers["azure"] = new AiProviderSettings { Enabled = true, Endpoint = "https://r.openai.azure.com" });
            store.SetKey("azure", "k");
            Assert.Contains("deployment", store.SetupProblem("azure"));
            Assert.Contains(store.ConfiguredProviders(), p => p.Id == "ollama");
            Assert.DoesNotContain(store.ConfiguredProviders(), p => p.Id == "azure");
        }
    }

    [Fact]
    public void CorruptFileFallsBackToDefaults()
    {
        var (store, _, path, dir) = Make(); using (dir)
        {
            File.WriteAllText(path, "{ not json");
            Assert.Equal("claude", store.DefaultProviderId);
        }
    }

    [Fact]
    public async Task TestConnectionReportsProblemsAndSuccessAndListsModels()
    {
        var (store, _, _, dir) = Make(); using (dir)
        {
            var missing = await store.TestConnectionAsync("openai");
            Assert.False(missing.Ok);
            store.SetKey("openai", "k");
            store.Update(c => c.Providers["openai"] = new AiProviderSettings { Enabled = true });
            var handler = new StubHandler { Respond = _ => StubHandler.Json("{\"object\":\"list\",\"data\":[{\"id\":\"m1\",\"object\":\"model\",\"created\":1,\"owned_by\":\"o\"}]}") };
            var withHttp = new AiProviderStore(Path.Combine(dir.Path, "ai-providers.json"), new InMemorySecretStore(), () => new HttpClient(handler));
            withHttp.SetKey("openai", "k");
            var ok = await withHttp.TestConnectionAsync("openai");
            Assert.True(ok.Ok, ok.Message);
            Assert.Contains("1 model", ok.Message);
            Assert.Equal(new[] { "m1" }, await withHttp.ListModelsAsync("openai", default, refresh: true));
            var bad = new StubHandler { Respond = _ => StubHandler.Json("{}", HttpStatusCode.Unauthorized) };
            var failing = new AiProviderStore(Path.Combine(dir.Path, "ai-providers.json"), new InMemorySecretStore(), () => new HttpClient(bad));
            failing.SetKey("openai", "wrong");
            var r = await failing.TestConnectionAsync("openai");
            Assert.False(r.Ok);
            Assert.Contains("key isn't valid", r.Message);
            Assert.False((await store.TestConnectionAsync("nope")).Ok);
        }
    }

    [Fact]
    public void SuggestModelSkipsNonChatModels() =>
        Assert.Equal("gpt-x", AiProviderStore.SuggestModel(new[] { "text-embedding-3", "whisper-1", "gpt-x", "gpt-y" }));
}

internal sealed class FakeProvider : IAiProvider
{
    public string Id { get; init; } = "fake";
    public AiAvailability Availability { get; init; } = AiAvailability.Yes;
    public string Reply { get; set; } = "";
    public string? LastModel; public AiRequest? LastRequest; public AiProviderContext? Context;
    public Task<AiAvailability> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(Availability);
    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(new[] { "fake-model" });
    public Task<string> CompleteAsync(string model, AiRequest request, CancellationToken ct) { LastModel = model; LastRequest = request; return Task.FromResult(Reply); }
}

/// <summary>These tests change static provider settings, so they must not run in parallel.</summary>
[Collection("ai-statics")]
public class AiActionTests : IDisposable
{
    private readonly TestHost _host = T.Host();
    private readonly FakeProvider _fake = new();

    public AiActionTests()
    {
        // keep the store's file inside the test host's own folder
        AiProviderRegistry.FactoryOverride = c => { _fake.Context = c; return _fake; };
    }

    public void Dispose() { AiProviderRegistry.FactoryOverride = null; _host.Dispose(); }

    private void SetKey(string id = "claude", string key = "k") => _host.Secrets.Set(AiProviderStore.KeyName(id), key);

    [Fact]
    public async Task NoKeyGivesFriendlyMessage()
    {
        var ex = await Assert.ThrowsAsync<ActionException>(() => T.Run(_host, new SummarizeAction(), ContentValue.Text("text")));
        Assert.Equal("Add your AI key in Settings › AI to use AI actions.", ex.Message);
    }

    [Fact]
    public async Task SummarizeUsesClaudeBestByDefault()
    {
        SetKey(); _fake.Reply = "A summary.";
        _host.Settings.Current.AiModel = AiModelTier.Best;
        var r = await T.Run(_host, new SummarizeAction(), ContentValue.Text("A long article."), ("length", T.Choice("short")));
        Assert.Equal("A summary.", r.AsText());
        Assert.Equal("claude-opus-5-5", _fake.LastModel);
        Assert.Contains("A long article.", string.Join(" ", _fake.LastRequest!.Parts.OfType<AiPart.Text>().Select(t => t.Value)));
    }

    [Fact]
    public async Task ModelOverrideAndTierFromSettings()
    {
        SetKey(); _fake.Reply = "x";
        _host.Settings.Current.AiModel = AiModelTier.Fast;
        await T.Run(_host, new RewriteTextAction(), ContentValue.Text("hello"));
        Assert.Equal("claude-haiku-4-5", _fake.LastModel);
        await T.Run(_host, new RewriteTextAction(), ContentValue.Text("hello"), ("model", "smart"));
        Assert.Equal("claude-sonnet-5-5", _fake.LastModel);
    }

    [Fact]
    public async Task UseParameterPicksAnotherProviderAndItsDefaultModel()
    {
        SetKey("openai", "sk");
        var store = AiProviderStore.For(_host.Services);
        store.Update(c => c.Providers["openai"] = new AiProviderSettings { Enabled = true, Model = "chosen-model" });
        _fake.Reply = "Bonjour";
        var r = await T.Run(_host, new TranslateTextAction(), ContentValue.Text("Hello"), ("language", T.Choice("French")), ("provider", T.Choice("openai")));
        Assert.Equal("Bonjour", r.AsText());
        Assert.Equal("chosen-model", _fake.LastModel);
        Assert.Equal("openai", _fake.Context!.Info.Id);
        Assert.Equal("sk", _fake.Context.Key);
        store.Update(c => c.DefaultProvider = "openai");
        await T.Run(_host, new TranslateTextAction(), ContentValue.Text("Hello"));
        Assert.Equal("openai", _fake.Context.Info.Id);
        _host.Dispose();
    }

    [Fact]
    public async Task ProviderThatIsNotSetUpExplainsWhat()
    {
        var ex = await Assert.ThrowsAsync<ActionException>(() => T.Run(_host, new TranslateTextAction(), ContentValue.Text("Hello"), ("provider", T.Choice("gemini"))));
        Assert.Contains("Turn on Google Gemini", ex.Message);
    }

    [Fact]
    public async Task ProviderChoicesListConfiguredProviders()
    {
        SetKey();
        var choices = await new AskAiAction().Descriptor.FindParameter("provider")!.ChoicesProvider!(_host.Services, default);
        Assert.Contains(choices, c => c.Id == "default");
        Assert.Contains(choices, c => c.Id == "claude");
        Assert.DoesNotContain(choices, c => c.Id == "openai");
    }

    [Fact]
    public async Task ExtraProvidersCanBeRegisteredAndGreyedOut()
    {
        var info = new AiProviderInfo("winai-test", "Windows AI (on this PC)", null, false, false, false, false, true, AiJsonMode.None, "test");
        AiProviderRegistry.Register(info, c => new FakeProvider { Id = "winai-test", Availability = new AiAvailability(false, "Needs a Copilot+ PC") });
        AiProviderRegistry.FactoryOverride = null;
        Assert.Contains(AiProviderRegistry.All, p => p.Id == "winai-test");
        var store = AiProviderStore.For(_host.Services);
        store.Update(c => c.Providers["winai-test"] = new AiProviderSettings { Enabled = true });
        var test = await store.TestConnectionAsync("winai-test");
        Assert.False(test.Ok);
        Assert.Equal("Needs a Copilot+ PC", test.Message);
        var ex = await Assert.ThrowsAsync<ActionException>(() => T.Run(_host, new SummarizeAction(), ContentValue.Text("x"), ("provider", T.Choice("winai-test"))));
        Assert.Equal("Needs a Copilot+ PC", ex.Message);
    }

    [Theory]
    [InlineData("yesno", "{\"answer\": true}", true)]
    [InlineData("yesno", "{\"answer\": false}", false)]
    public async Task AskYesNoGivesBoolean(string answerAs, string reply, bool expected)
    {
        SetKey(); _fake.Reply = reply;
        var r = await T.Run(_host, new AskAiAction(), null, ("prompt", "Is the sky blue?"), ("answerAs", T.Choice(answerAs)));
        Assert.IsType<BooleanItem>(r[0]);
        Assert.Equal(expected, r.AsBool());
        Assert.Equal(AiShape.Boolean, _fake.LastRequest!.Shape);
    }

    [Fact]
    public async Task AskNumberListAndDictionary()
    {
        SetKey();
        _fake.Reply = "{\"answer\": 7}";
        Assert.Equal(7, (await T.Run(_host, new AskAiAction(), null, ("prompt", "How many?"), ("answerAs", T.Choice("number")))).AsNumber());
        _fake.Reply = "{\"items\":[\"a\",\"b\",\"c\"]}";
        Assert.Equal(3, (await T.Run(_host, new AskAiAction(), null, ("prompt", "List"), ("answerAs", T.Choice("list")))).Count);
        _fake.Reply = "{\"entries\":[{\"key\":\"k\",\"value\":\"v\"}]}";
        var d = (DictionaryItem)(await T.Run(_host, new AskAiAction(), null, ("prompt", "Dict"), ("answerAs", T.Choice("dictionary"))))[0];
        Assert.Equal("v", d.Get("k").AsText());
    }

    [Fact]
    public async Task ExtractGivesDictionary()
    {
        SetKey(); _fake.Reply = "{\"Name\":\"Sam\",\"Total\":\"12.50\"}";
        var r = await T.Run(_host, new ExtractInformationAction(), ContentValue.Text("Receipt for Sam, total 12.50"), ("fields", ParamValue.List("Name", "Total")));
        var d = (DictionaryItem)r[0];
        Assert.Equal("Sam", d.Get("Name").AsText());
        Assert.Equal("12.50", d.Get("Total").AsText());
    }

    [Fact]
    public async Task FilesAreReadAsTextAndImagesAndPdfsAreAttached()
    {
        SetKey(); _fake.Reply = "ok";
        using var d = new TempDir();
        var txt = d.File("note.txt", "File contents here");
        await T.Run(_host, new SummarizeAction(), ContentValue.Files(new[] { txt }));
        Assert.Contains("File contents here", ((AiPart.Text)_fake.LastRequest!.Parts[^1]).Value);

        var png = await T.MakeImage(d.File("p.png"), 20, 20);
        await T.Run(_host, new DescribeImageAction(), ContentValue.Images(new[] { png }), ("detail", T.Choice("alt")));
        Assert.IsType<AiPart.Image>(_fake.LastRequest!.Parts[0]);

        var pdf = Path.Combine(d.Path, "a.pdf");
        PdfEngine.Make(new PdfSource[] { new PdfSource.Text("hello") }, pdf, new PdfMakeOptions());
        await T.Run(_host, new SummarizeAction(), ContentValue.Files(new[] { pdf }));
        Assert.IsType<AiPart.Pdf>(_fake.LastRequest!.Parts[0]);
    }

    [Fact]
    public async Task DescribeImageReturnsOneTextPerImage()
    {
        SetKey(); _fake.Reply = "A red and blue picture.";
        using var d = new TempDir();
        var a = await T.MakeImage(d.File("a.png"), 20, 20); var b = await T.MakeImage(d.File("b.png"), 20, 20);
        var r = await T.Run(_host, new DescribeImageAction(), ContentValue.Images(new[] { a, b }));
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public async Task EmptyInputGivesFriendlyError()
    {
        SetKey();
        var ex = await Assert.ThrowsAsync<ActionException>(() => T.Run(_host, new SummarizeAction(), null));
        Assert.Contains("nothing to work with", ex.Message);
    }

    [Fact]
    public void AllAiActionsOfferProviderAndModelAndNeedInternet()
    {
        foreach (ShortcutAction a in new ShortcutAction[] { new AskAiAction(), new SummarizeAction(), new RewriteTextAction(), new TranslateTextAction(), new ExtractInformationAction(), new DescribeImageAction() })
        {
            Assert.NotNull(a.Descriptor.FindParameter("provider"));
            Assert.NotNull(a.Descriptor.FindParameter("model"));
            Assert.True(a.Descriptor.NeedsInternet);
            Assert.Equal(ActionCategories.Ai, a.Descriptor.Category);
        }
    }
}
