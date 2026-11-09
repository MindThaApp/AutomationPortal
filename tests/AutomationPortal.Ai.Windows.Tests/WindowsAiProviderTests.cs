using AutomationPortal.Actions.Documents;
using AutomationPortal.Ai.Windows;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.Ai.Windows.Tests;

internal sealed class FakeBackend : IWindowsAiBackend
{
    public WindowsAiState State { get; set; } = WindowsAiState.Ready;
    public Exception? StateThrows { get; set; }
    public bool Touched;
    public string? EnsureResult;
    public int EnsureCalls;
    public string? SummaryResult = "SUMMARY";
    public string? RewriteResult = "REWRITTEN";
    public string? ImageResult = "A cat on a sofa.";
    public List<string> Calls { get; } = new();
    public (string System, string Prompt)? Generated;

    public WindowsAiState GetReadyState() { Touched = true; if (StateThrows is not null) throw StateThrows; return State; }
    public Task<string?> EnsureReadyAsync(IProgress<double>? progress, CancellationToken ct) { Touched = true; EnsureCalls++; progress?.Report(0.5); return Task.FromResult(EnsureResult); }
    public Task<string> GenerateAsync(string system, string prompt, CancellationToken ct) { Calls.Add("generate"); Generated = (system, prompt); return Task.FromResult("GENERATED"); }
    public Task<string?> SummarizeAsync(string text, bool bullets, CancellationToken ct) { Calls.Add(bullets ? "summarize-bullets" : "summarize-paragraph"); Calls.Add("text=" + text); return Task.FromResult(SummaryResult); }
    public Task<string?> RewriteAsync(string text, RewriteTone tone, CancellationToken ct) { Calls.Add("rewrite-" + tone); Calls.Add("text=" + text); return Task.FromResult(RewriteResult); }
    public Task<string?> DescribeImageAsync(byte[] image, string detail, CancellationToken ct) { Calls.Add("describe-" + detail); return Task.FromResult(ImageResult); }
}

[Collection("provider-registry")]
public class WindowsAiProviderTests
{
    private static WindowsAiProvider Make(IWindowsAiBackend b, bool packaged = true) => new(() => packaged, () => b);

    [Fact]
    public async Task Unpackaged_says_it_needs_the_Store_version_and_never_touches_Windows_AI()
    {
        var b = new FakeBackend();
        var a = await Make(b, packaged: false).IsAvailableAsync();
        Assert.False(a.IsAvailable);
        Assert.Equal("Needs the Microsoft Store version of Automation Portal", a.Reason);
        Assert.False(b.Touched);
        Assert.Equal("Needs the Microsoft Store version of Automation Portal.", await Make(b, false).EnsureReadyAsync());
        Assert.False(b.Touched);
    }

    [Theory]
    [InlineData(WindowsAiState.Ready, true, null)]
    [InlineData(WindowsAiState.NotSupported, false, "Needs a Copilot+ PC")]
    [InlineData(WindowsAiState.NotReady, false, "Windows is still setting up on-device AI")]
    [InlineData(WindowsAiState.DisabledByUser, false, "On-device AI is turned off in Windows")]
    [InlineData(WindowsAiState.Unavailable, false, "Needs a Copilot+ PC")]
    public async Task Ready_states_become_plain_reasons(WindowsAiState state, bool available, string? reason)
    {
        var a = await Make(new FakeBackend { State = state }).IsAvailableAsync();
        Assert.Equal(available, a.IsAvailable);
        Assert.Equal(reason, a.Reason);
    }

    [Fact]
    public async Task A_backend_that_throws_counts_as_not_supported()
    {
        var a = await Make(new FakeBackend { StateThrows = new System.Runtime.InteropServices.COMException("Class not registered", unchecked((int)0x80040154)) }).IsAvailableAsync();
        Assert.False(a.IsAvailable);
        Assert.Equal("Needs a Copilot+ PC", a.Reason);
    }

    [Fact]
    public async Task Only_the_still_setting_up_state_offers_Get_it_ready()
    {
        var b = new FakeBackend { State = WindowsAiState.NotReady };
        var p = Make(b);
        Assert.True(WindowsAiProvider.CanGetReady(await p.IsAvailableAsync()));
        var seen = new List<double>();
        Assert.Null(await p.EnsureReadyAsync(new Progress<double>(seen.Add)));
        Assert.Equal(1, b.EnsureCalls);
        b.State = WindowsAiState.NotSupported;
        Assert.False(WindowsAiProvider.CanGetReady(await p.IsAvailableAsync()));
        b.EnsureResult = "Windows couldn't finish.";
        Assert.Equal("Windows couldn't finish.", await p.EnsureReadyAsync());
    }

    [Fact]
    public async Task Completing_while_unavailable_explains_why()
    {
        var ex = await Assert.ThrowsAsync<ActionException>(() => Make(new FakeBackend { State = WindowsAiState.NotSupported })
            .CompleteAsync("", new AiRequest("sys", new AiPart[] { new AiPart.Text("hi") }, AiEffort.Low, AiShape.Text), default));
        Assert.Equal("Needs a Copilot+ PC.", ex.Message);
    }

    [Fact]
    public async Task Plain_text_goes_to_the_language_model_with_the_system_prompt()
    {
        var b = new FakeBackend();
        var r = await Make(b).CompleteAsync("", new AiRequest("Be brief.", new AiPart[] { new AiPart.Text("Hello?") }, AiEffort.Low, AiShape.Text), default);
        Assert.Equal("GENERATED", r);
        Assert.Equal(("Be brief.", "Hello?"), b.Generated);
    }

    [Fact]
    public async Task Structured_answers_ask_for_json_and_carry_the_schema()
    {
        var b = new FakeBackend();
        await Make(b).CompleteAsync("", new AiRequest("sys", new AiPart[] { new AiPart.Text("q") }, AiEffort.Medium, AiShape.Fields, "{\"type\":\"object\"}"), default);
        Assert.Contains("only a single JSON object", b.Generated!.Value.System);
        Assert.Contains("{\"type\":\"object\"}", b.Generated.Value.System);
    }

    [Fact]
    public async Task Summaries_use_the_dedicated_summarizer_and_fall_back_when_it_declines()
    {
        var b = new FakeBackend();
        var request = AiRequestBuilder.Summarize(new AiPart[] { new AiPart.Text("A very long article.") }, "bullets");
        Assert.Equal("SUMMARY", await Make(b).CompleteAsync("", request, default));
        Assert.Equal(new[] { "summarize-bullets", "text=A very long article." }, b.Calls);

        var b2 = new FakeBackend { SummaryResult = null };
        Assert.Equal("GENERATED", await Make(b2).CompleteAsync("", request, default));
        Assert.Equal("summarize-bullets", b2.Calls[0]);
        Assert.Equal("generate", b2.Calls[^1]);

        var b3 = new FakeBackend();
        await Make(b3).CompleteAsync("", AiRequestBuilder.Summarize(new AiPart[] { new AiPart.Text("x") }, "short"), default);
        Assert.Equal(new[] { "generate" }, b3.Calls);   // "a sentence or two" isn't something the summarizer can be told
    }

    [Theory]
    [InlineData("professional", "rewrite-Formal")]
    [InlineData("friendlier", "rewrite-Casual")]
    [InlineData("shorter", "rewrite-Concise")]
    public async Task Rewrites_map_the_style_to_a_tone(string style, string call)
    {
        var b = new FakeBackend();
        Assert.Equal("REWRITTEN", await Make(b).CompleteAsync("", AiRequestBuilder.Rewrite("hello", style), default));
        Assert.Equal(call, b.Calls[0]);
        Assert.Equal("text=hello", b.Calls[1]);
    }

    [Fact]
    public async Task Other_rewrite_styles_use_the_language_model_with_their_full_instruction()
    {
        var b = new FakeBackend();
        await Make(b).CompleteAsync("", AiRequestBuilder.Rewrite("hello", "table"), default);
        Assert.Equal(new[] { "generate" }, b.Calls);
        Assert.Contains("Markdown table", b.Generated!.Value.Prompt);
    }

    [Fact]
    public async Task Images_are_described_on_device_and_questions_are_answered_from_the_description()
    {
        var b = new FakeBackend();
        var image = new AiPart.Image(new byte[] { 1, 2, 3 }, "image/png");
        Assert.Equal("A cat on a sofa.", await Make(b).CompleteAsync("", AiRequestBuilder.DescribeImage(image, "alt", null), default));
        Assert.Equal(new[] { "describe-alt" }, b.Calls);

        var b2 = new FakeBackend();
        Assert.Equal("GENERATED", await Make(b2).CompleteAsync("", AiRequestBuilder.DescribeImage(image, "short", "What colour is the sofa?"), default));
        Assert.Equal("describe-detailed", b2.Calls[0]);
        Assert.Contains("A cat on a sofa.", b2.Generated!.Value.Prompt);
        Assert.Contains("What colour is the sofa?", b2.Generated.Value.Prompt);
    }

    [Fact]
    public async Task Reading_text_in_a_picture_and_missing_image_support_give_friendly_errors()
    {
        var image = new AiPart.Image(new byte[] { 1 }, "image/png");
        var ex = await Assert.ThrowsAsync<ActionException>(() => Make(new FakeBackend()).CompleteAsync("", AiRequestBuilder.DescribeImage(image, "text", null), default));
        Assert.Contains("can't read the text", ex.Message);
        var ex2 = await Assert.ThrowsAsync<ActionException>(() => Make(new FakeBackend { ImageResult = null }).CompleteAsync("", AiRequestBuilder.DescribeImage(image, "short", null), default));
        Assert.Contains("couldn't describe that picture", ex2.Message);
    }

    [Fact]
    public async Task Unexpected_failures_become_a_plain_message()
    {
        var b = new ThrowingBackend();
        var ex = await Assert.ThrowsAsync<ActionException>(() => Make(b).CompleteAsync("", new AiRequest("s", new AiPart[] { new AiPart.Text("x") }, AiEffort.Low, AiShape.Text), default));
        Assert.StartsWith("The on-device AI ran into a problem.", ex.Message);
    }

    private sealed class ThrowingBackend : IWindowsAiBackend
    {
        public WindowsAiState GetReadyState() => WindowsAiState.Ready;
        public Task<string?> EnsureReadyAsync(IProgress<double>? progress, CancellationToken ct) => throw new InvalidOperationException();
        public Task<string> GenerateAsync(string system, string prompt, CancellationToken ct) => throw new InvalidOperationException("boom");
        public Task<string?> SummarizeAsync(string text, bool bullets, CancellationToken ct) => throw new InvalidOperationException();
        public Task<string?> RewriteAsync(string text, RewriteTone tone, CancellationToken ct) => throw new InvalidOperationException();
        public Task<string?> DescribeImageAsync(byte[] image, string detail, CancellationToken ct) => throw new InvalidOperationException();
    }

    [Fact]
    public void Registering_adds_it_to_the_provider_list_as_a_local_provider_that_needs_no_key()
    {
        WindowsAiProvider.Register();
        var info = AiProviderRegistry.Get("windows-ai");
        Assert.NotNull(info);
        Assert.Equal("Windows AI (on this PC)", info!.Name);
        Assert.True(info.IsLocal);
        Assert.False(info.NeedsKey);
        Assert.False(info.NeedsEndpoint);
        var created = AiProviderRegistry.Create(new AiProviderContext(info, new AiProviderSettings(), null, null));
        Assert.IsType<WindowsAiProvider>(created);
    }

    // ---- the real thing on a PC without Copilot+ hardware / without package identity -------------------

    [Fact]
    public async Task Real_provider_in_an_unpackaged_process_is_unavailable_with_the_Store_reason()
    {
        PackageIdentity.Override = false;
        try
        {
            var a = await new WindowsAiProvider().IsAvailableAsync();
            Assert.False(a.IsAvailable);
            Assert.Equal(WindowsAiProvider.UnpackagedReason, a.Reason);
        }
        finally { PackageIdentity.Override = null; }
    }

    [Fact]
    public async Task Real_backend_on_a_PC_that_cannot_run_it_fails_gracefully_instead_of_crashing()
    {
        PackageIdentity.Override = true;   // pretend we are packaged so the real Windows App SDK code runs
        try
        {
            var a = await new WindowsAiProvider().IsAvailableAsync();
            // Without an NPU (or without identity) Windows says not supported; with a capable PC it could be ready or still setting up.
            Assert.True(a.IsAvailable || a.Reason is "Needs a Copilot+ PC" or "Windows is still setting up on-device AI" or "On-device AI is turned off in Windows", a.Reason);
        }
        finally { PackageIdentity.Override = null; }
    }
}
