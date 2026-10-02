using Flowmate.Actions.Documents;
using Flowmate.Core;
using Flowmate.Core.Testing;
using Xunit;

namespace Flowmate.Actions.Documents.Tests;

internal sealed class QueueProvider : IAiProvider
{
    private readonly Queue<string> _replies;
    public QueueProvider(params string[] replies) { _replies = new Queue<string>(replies); }
    public List<AiRequest> Requests { get; } = new();
    public string Id => "queue";
    public Task<AiAvailability> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(AiAvailability.Yes);
    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    public Task<string> CompleteAsync(string model, AiRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(_replies.Count > 0 ? _replies.Dequeue() : "");
    }
    public string UserText(int i) => string.Join("\n", Requests[i].Parts.OfType<AiPart.Text>().Select(t => t.Value));
}

[Collection("ai-statics")]
public class ShortcutAiTests : IDisposable
{
    private readonly TestHost _host = T.Host();
    private readonly TriggerRegistry _triggers = new();

    public ShortcutAiTests()
    {
        _host.With(typeof(Flowmate.Actions.Content.Utilities.ExpressionEvaluator).Assembly)
            .With(typeof(Flowmate.Actions.Windows.Apps.InstalledApps).Assembly);
        _triggers.Register(new FakeDaily());
    }

    public void Dispose() => _host.Dispose();

    private sealed class FakeDaily : TriggerProvider
    {
        public override TriggerDescriptor Descriptor { get; } = new()
        {
            Id = "time.daily", Title = "At a time of day", Category = "Time",
            Parameters = new[]
            {
                new ParameterDescriptor { Key = "time", Label = "Time", Type = ParamType.Time, Default = ParamValue.Time("08:00") },
                new ParameterDescriptor { Key = "days", Label = "Days", Type = ParamType.List, Choices = new Choice[] { "mon", "tue" } },
            },
        };
        public override IDisposable Start(TriggerConfig config, ITriggerSink sink) => throw new NotSupportedException();
    }

    private ShortcutAiBuilder Builder(QueueProvider p, bool advanced = false) => new(_host.Registry, _triggers, p, "m", advanced);

    private const string Good = """
        {"name":"Morning weather","icon":"WeatherSunny24","color":"orange","summary":"Tells you the weather and opens Outlook.",
         "steps":[
          {"actionId":"weather.current","uid":"s1","parameters":{"location":"London"}},
          {"actionId":"ui.notify","uid":"s2","parameters":{"body":"Today: {{s1.key:Summary}}"}},
          {"actionId":"app.open","uid":"s3","parameters":{"app":"Outlook"}}],
         "automations":[{"triggerId":"time.daily","label":"Every day at 8:00","options":{"time":"08:00","days":["mon","tue"]}},{"triggerId":"nope.nothing","label":"x"}]}
        """;

    [Fact]
    public async Task ValidReplyBecomesADraftWithAutomations()
    {
        var p = new QueueProvider(Good);
        var r = await Builder(p).CreateAsync("Every morning, tell me the weather and open Outlook", default);
        Assert.True(r.Success, r.Error);
        Assert.Equal(1, p.Requests.Count);
        var s = r.Shortcut!;
        Assert.Equal("Morning weather", s.Name);
        Assert.Equal("WeatherSunny24", s.Icon);
        Assert.Equal(new[] { "weather.current", "ui.notify", "app.open" }, s.Actions.Select(a => a.ActionId));
        var body = Assert.IsType<TextValue>(s.Actions[1].Parameters["body"]);
        Assert.Equal(VarRef.Output("s1").WithProperty("key:Summary"), body.Parts[1].Var);
        Assert.IsType<AppValue>(s.Actions[2].Parameters["app"]);
        Assert.Empty(ShortcutValidator.Validate(s, _host.Registry));
        var auto = Assert.Single(r.Automations);
        Assert.Equal("time.daily", auto.TriggerId);
        Assert.IsType<TimeValue>(auto.Options["time"]);
        Assert.Equal(2, ((ListValue)auto.Options["days"]).Items.Count);
        Assert.Equal("Tells you the weather and opens Outlook.", r.Summary);
        Assert.Equal(AiShape.Fields, p.Requests[0].Shape);
        Assert.Contains("weather.current", p.Requests[0].System);
        Assert.Contains("Every morning", p.UserText(0));
    }

    [Fact]
    public async Task ProblemsAreSentBackAndFixed()
    {
        var bad = """{"name":"x","steps":[{"actionId":"does.notExist","uid":"a"}]}""";
        var p = new QueueProvider(bad, Good);
        var r = await Builder(p).CreateAsync("weather", default);
        Assert.True(r.Success, r.Error);
        Assert.Equal(2, p.Requests.Count);
        Assert.Contains("does.notExist", p.UserText(1));
        Assert.Contains("problems", p.UserText(1));
    }

    [Fact]
    public async Task GarbageGivesAFriendlyErrorAfterTwoRepairs()
    {
        var p = new QueueProvider("I'm sorry, I can't do that.", "still no", "nope");
        var r = await Builder(p).CreateAsync("weather", default);
        Assert.False(r.Success);
        Assert.Equal(1 + ShortcutAiBuilder.MaxRepairRounds, p.Requests.Count);
        Assert.Contains("wasn't usable", r.Error);
        Assert.DoesNotContain("Exception", r.Error);
    }

    [Fact]
    public async Task FixableProblemsLeftOverBecomeWarnings()
    {
        // a later variable is a validator error but the editor shows it inline, so the draft is still delivered
        var later = """{"name":"x","steps":[{"actionId":"ui.notify","uid":"a","parameters":{"body":"{{b}}"}},{"actionId":"clip.get","uid":"b"}]}""";
        var p = new QueueProvider(later, later, later);
        var r = await Builder(p).CreateAsync("x", default);
        Assert.True(r.Success);
        Assert.NotEmpty(r.Warnings);
    }

    [Fact]
    public async Task AdvancedActionsAreRefusedUnlessEnabled()
    {
        var advanced = _host.Registry.All.Select(a => a.Descriptor).FirstOrDefault(d => d.IsAdvanced);
        if (advanced is null) return;
        Assert.DoesNotContain(advanced.Id + " |", ShortcutAiBuilder.BuildCatalog(_host.Registry, false));
        Assert.Contains(advanced.Id + " |", ShortcutAiBuilder.BuildCatalog(_host.Registry, true));
        var doc = $$"""{"name":"x","steps":[{"actionId":"{{advanced.Id}}","uid":"a"}]}""";
        var p = new QueueProvider(doc, doc, doc);
        var r = await Builder(p, advanced: false).CreateAsync("x", default);
        Assert.False(r.Success);
    }

    [Fact]
    public async Task ChangeKeepsTheShortcutIdAndStepsItWasNotAskedToTouch()
    {
        var current = new ShortcutBuilder("Hello", "Star24", "green");
        current.Add("clip.get", ("as", ParamValue.Choice("text")));
        var original = current.Build();
        var stepsJson = FlowmateJson.Serialize(original.Actions);
        var revised = "{\"name\":\"Hello\",\"summary\":\"Added a notification at the end.\",\"steps\":" + stepsJson[..^1] +
                      ",{\"actionId\":\"ui.notify\",\"uid\":\"n1\",\"parameters\":{\"body\":\"Done\"}}]}";        var p = new QueueProvider(revised);
        var r = await Builder(p).ChangeAsync(original, "add a notification", default);
        Assert.True(r.Success, r.Error);
        Assert.Equal(original.Id, r.Shortcut!.Id);
        Assert.Equal(2, r.Shortcut.Actions.Count);
        Assert.Equal(original.Actions[0].Uid, r.Shortcut.Actions[0].Uid);
        Assert.Contains("Added a notification", r.Summary);
        Assert.Empty(r.Automations);
        Assert.Contains("add a notification", p.UserText(0));
        Assert.Contains(original.Actions[0].Uid, p.UserText(0));
    }

    [Fact]
    public async Task ReplyInsideCodeFencesAndFullTaggedValuesWork()
    {
        var reply = "Sure!\n```json\n" + """{"name":"Fenced","steps":[{"actionId":"ui.notify","uid":"s1","parameters":{"title":{"t":"text","parts":["Hi"]},"body":{"t":"text","v":"plain v"},"timeout":{"t":"num","v":10}}}]}""" + "\n```";
        var p = new QueueProvider(reply);
        var r = await Builder(p).CreateAsync("x", default);
        Assert.True(r.Success, r.Error);
        Assert.Equal("plain v", r.Shortcut!.Actions[0].Parameters["body"].ToLiteralText());
    }

    [Fact]
    public async Task ControlFlowGroupsSurvive()
    {
        var gold = ShortcutAiBuilder.GoldExamples()[1].Shortcut;
        var reply = ShortcutAiBuilder.ExampleJson(gold, null);
        var p = new QueueProvider(reply);
        var r = await Builder(p).CreateAsync("x", default);
        Assert.True(r.Success, r.Error);
        Assert.Equal(gold.Actions.Count, r.Shortcut!.Actions.Count);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void GoldExamplesAreValidAndRoundTrip()
    {
        foreach (var (request, shortcut, automations) in ShortcutAiBuilder.GoldExamples())
        {
            var issues = ShortcutValidator.Validate(shortcut, _host.Registry);
            Assert.True(issues.Count == 0, request + ": " + string.Join("; ", issues.Select(i => i.Message)));
            Assert.Contains("\"actionId\"", ShortcutAiBuilder.ExampleJson(shortcut, automations));
        }
    }

    [Fact]
    public void CatalogListsActionsWithParametersAndChoices()
    {
        var catalog = ShortcutAiBuilder.BuildCatalog(_host.Registry, false);
        var line = catalog.Split('\n').First(l => l.StartsWith("weather.current |"));
        Assert.Contains("location(", line);
        Assert.Contains("units(choice:", line);
        Assert.Contains("Dictionary", line);
        Assert.DoesNotContain("provider(", catalog);
    }

    [Fact]
    public async Task ProviderErrorsBecomeFriendlyResults()
    {
        var r = await new ShortcutAiBuilder(_host.Registry, null, new ThrowingProvider(), "m", false).CreateAsync("x", default);
        Assert.False(r.Success);
        Assert.Equal("The AI is asleep.", r.Error);
    }

    private sealed class ThrowingProvider : IAiProvider
    {
        public string Id => "t";
        public Task<AiAvailability> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(AiAvailability.Yes);
        public Task<string> CompleteAsync(string model, AiRequest request, CancellationToken ct) => throw new ActionException("The AI is asleep.");
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    [Fact]
    public async Task ServiceReportsMissingSetupAndUsesTheDefaultProvider()
    {
        var host = _host;
        Assert.False(ShortcutAiService.IsReady(host.Services));
        var none = await ShortcutAiService.RunAsync(host.Services, host.Registry, _triggers, "x", null);
        Assert.Contains("Settings", none.Error);

        host.Secrets.Set(AiProviderStore.KeyName("claude"), "k");
        Assert.True(ShortcutAiService.IsReady(host.Services));
        var fake = new QueueProvider(Good);
        AiProviderRegistry.FactoryOverride = _ => fake;
        try
        {
            var r = await ShortcutAiService.RunAsync(host.Services, host.Registry, _triggers, "weather", null);
            Assert.True(r.Success, r.Error);
        }
        finally { AiProviderRegistry.FactoryOverride = null; }
    }
}
