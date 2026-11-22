using AutomationPortal.Core.Testing;
using Xunit;

namespace AutomationPortal.Actions.Documents.Tests;

[Collection("ai-statics")]
public class QuickAskServiceTests : IDisposable
{
    private readonly TestHost _host = T.Host();

    public void Dispose() { AiProviderRegistry.FactoryOverride = null; _host.Dispose(); }

    [Fact]
    public async Task Asks_the_default_provider_and_returns_the_answer_with_the_privacy_line()
    {
        _host.Secrets.Set(AiProviderStore.KeyName("claude"), "k");
        var fake = new FakeProvider { Reply = "  Paris.  " };
        AiProviderRegistry.FactoryOverride = _ => fake;

        var r = await QuickAskService.AskAsync(_host.Services, "capital of France?", null, default);

        Assert.True(r.Ok);
        Assert.Equal("Paris.", r.Answer);
        Assert.Equal("Sends text to Claude", r.Privacy);
        Assert.Contains("capital of France?", string.Join("\n", fake.LastRequest!.Parts.OfType<AiPart.Text>().Select(t => t.Value)));
    }

    [Fact]
    public async Task Selected_text_goes_along_as_material()
    {
        _host.Secrets.Set(AiProviderStore.KeyName("claude"), "k");
        var fake = new FakeProvider { Reply = "ok" };
        AiProviderRegistry.FactoryOverride = _ => fake;

        await QuickAskService.AskAsync(_host.Services, "summarize", "the quick brown fox", default);

        Assert.Contains("the quick brown fox", string.Join("\n", fake.LastRequest!.Parts.OfType<AiPart.Text>().Select(t => t.Value)));
    }

    [Fact]
    public async Task No_provider_ready_asks_for_setup_and_sends_nothing()
    {
        var fake = new FakeProvider { Reply = "never" };
        AiProviderRegistry.FactoryOverride = _ => fake;

        var r = await QuickAskService.AskAsync(_host.Services, "hello there", null, default);

        Assert.False(r.Ok);
        Assert.True(r.NeedsSetup);
        Assert.Null(fake.LastRequest);
        Assert.False(QuickAskService.Status(_host.Services).Ready);
    }

    [Fact]
    public async Task An_unavailable_provider_is_a_problem_not_a_setup_request()
    {
        _host.Secrets.Set(AiProviderStore.KeyName("claude"), "k");
        AiProviderRegistry.FactoryOverride = _ => new FakeProvider { Availability = new AiAvailability(false, "Offline.") };

        var r = await QuickAskService.AskAsync(_host.Services, "hello there", null, default);

        Assert.False(r.NeedsSetup);
        Assert.Equal("Offline.", r.Problem);
    }

    [Fact]
    public async Task An_empty_reply_is_reported()
    {
        _host.Secrets.Set(AiProviderStore.KeyName("claude"), "k");
        AiProviderRegistry.FactoryOverride = _ => new FakeProvider { Reply = "   " };
        var r = await QuickAskService.AskAsync(_host.Services, "hello there", null, default);
        Assert.False(r.Ok);
        Assert.Contains("empty", r.Problem);
    }
}
