using AutomationPortal.Actions.Documents;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using Xunit;

namespace AutomationPortal.Actions.Documents.Tests;

[Collection("ai-statics")]
public class ShortcutAiClarifyTests : IDisposable
{
    private readonly TestHost _host = T.Host();

    public ShortcutAiClarifyTests()
    {
        _host.With(typeof(AutomationPortal.Actions.Content.Utilities.ExpressionEvaluator).Assembly)
            .With(typeof(AutomationPortal.Actions.Windows.Apps.InstalledApps).Assembly);
    }

    public void Dispose() => _host.Dispose();

    private ShortcutAiBuilder Builder(QueueProvider p, AiJsonMode mode = AiJsonMode.None) =>
        new(_host.Registry, new TriggerRegistry(), p, "m", false, jsonMode: mode);

    private const string Good = """
        {"name":"Copy photos","icon":"Image24","color":"blue","summary":"Copies photos.",
         "steps":[{"actionId":"ui.notify","uid":"s1","parameters":{"body":"Hello"}}]}
        """;

    private const string AllKinds = """
        {"questions":[
         {"text":"Which folder?","kind":"folder","options":[],"default":"C:\\invented"},
         {"text":"Which file?","kind":"file","options":[],"default":""},
         {"text":"Name?","kind":"text","options":[],"default":"Photos"},
         {"text":"Ignored fourth","kind":"yesno","options":[],"default":"yes"}]}
        """;

    [Fact]
    public void EachKindIsParsedAndCappedAtThree()
    {
        var q = ShortcutAiClarifier.Parse(AllKinds);
        Assert.Equal(3, q.Count);
        Assert.Equal(new[] { AiQuestionKind.Folder, AiQuestionKind.File, AiQuestionKind.Text }, q.Select(x => x.Kind));
        Assert.Equal("", q[0].Default);
        Assert.Equal("Photos", q[2].Default);
    }

    [Fact]
    public void ChoiceNeedsTwoToFiveOptionsAndDefaultsToAnOption()
    {
        var q = ShortcutAiClarifier.Parse("""
            ```json
            {"questions":[{"text":"How?","kind":"choice","options":["a","b","c","d","e","f"],"default":"c"},
                          {"text":"Too few","kind":"choice","options":["only"],"default":"only"},
                          {"text":"Sure?","kind":"yes-no","options":[],"default":"Yes"}]}
            ```
            """);
        Assert.Equal(2, q.Count);
        Assert.Equal(5, q[0].Options.Count);
        Assert.Equal("c", q[0].Default);
        Assert.Equal(AiQuestionKind.YesNo, q[1].Kind);
        Assert.Equal("yes", q[1].Default);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sure, what folder?")]
    [InlineData("{\"questions\": [ {")]
    [InlineData("{\"other\":1}")]
    public void UnusableRepliesGiveNoQuestions(string reply) => Assert.Empty(ShortcutAiClarifier.Parse(reply));

    [Fact]
    public async Task NoQuestionsMeansTheBuildRunsAsBefore()
    {
        var p = new QueueProvider("""{"questions":[]}""", Good);
        var b = Builder(p);
        Assert.Empty(await b.ClarifyAsync("show a notification", default));
        var r = await b.CreateAsync("show a notification", default);
        Assert.True(r.Success, r.Error);
        Assert.Equal(2, p.Requests.Count);
        Assert.Contains("<request>\nshow a notification\n</request>", p.UserText(1));
    }

    [Fact]
    public async Task AnswersFlowIntoTheBuildPrompt()
    {
        var p = new QueueProvider("""{"questions":[{"text":"Which folder?","kind":"folder","options":[],"default":""},{"text":"Keep originals?","kind":"yesno","options":[],"default":"yes"}]}""", Good);
        var b = Builder(p);
        var questions = await b.ClarifyAsync("copy my photos", default);
        var request = AiQuestions.AppendAnswers("copy my photos", questions, new[] { @"D:\Pictures", "Yes" });
        Assert.True((await b.CreateAsync(request, default)).Success);
        var build = p.UserText(1);
        Assert.Contains("copy my photos", build);
        Assert.Contains(@"Which folder? D:\Pictures", build);
        Assert.Contains("Keep originals? Yes", build);
    }

    [Fact]
    public void UnansweredQuestionsAreLeftOut()
    {
        var q = ShortcutAiClarifier.Parse("""{"questions":[{"text":"Which folder?","kind":"folder","options":[],"default":""}]}""");
        Assert.Equal("copy photos", AiQuestions.AppendAnswers("copy photos", q, new[] { "" }));
    }

    [Fact]
    public async Task MalformedClarifyReplyFallsBackToBuild()
    {
        var p = new QueueProvider("I have no questions!", Good);
        var b = Builder(p);
        Assert.Empty(await b.ClarifyAsync("show a notification", default));
        Assert.True((await b.CreateAsync("show a notification", default)).Success);
    }

    [Fact]
    public async Task StructuredOutputIsOnlyRequestedWhenTheProviderSupportsSchemas()
    {
        var plain = new QueueProvider("""{"questions":[]}""");
        await Builder(plain, AiJsonMode.JsonObject).ClarifyAsync("x", default);
        Assert.Null(plain.Requests[0].SchemaJson);
        var schema = new QueueProvider("""{"questions":[]}""");
        await Builder(schema, AiJsonMode.JsonSchema).ClarifyAsync("x", default);
        Assert.NotNull(schema.Requests[0].SchemaJson);
    }

    [Fact]
    public async Task TotalCallsStayWithinTheCap()
    {
        var p = new QueueProvider("""{"questions":[{"text":"Name?","kind":"text","options":[],"default":"x"}]}""", "nope", "nope", "nope", "nope", "nope");
        var b = Builder(p);
        await b.ClarifyAsync("make something", default);
        var r = await b.CreateAsync("make something", default);
        Assert.False(r.Success);
        Assert.Equal(ShortcutAiClarifier.MaxTotalCalls, p.Requests.Count);
        Assert.Equal(4, ShortcutAiClarifier.MaxTotalCalls);
    }

    [Fact]
    public void DraftSummaryLabelsFileChangingStepsAndSavesNothing()
    {
        var s = new Shortcut { Name = "Tidy" };
        s.Actions.Add(new ActionInstance { ActionId = "file.get", Uid = "a" });
        s.Actions.Add(new ActionInstance { ActionId = "file.copy", Uid = "b" });
        s.Actions.Add(new ActionInstance { ActionId = "file.move", Uid = "c" });
        s.Actions.Add(new ActionInstance { ActionId = "ui.notify", Uid = "d" });
        var lines = AiDraftSummary.Lines(s, _host.Registry);
        Assert.Equal(new[] { AiStepEffect.None, AiStepEffect.MakesCopies, AiStepEffect.ChangesFiles, AiStepEffect.None }, lines.Select(l => l.Effect));
        Assert.Empty(_host.Library.All);
    }
}
