using AutomationPortal.Core;
using AutomationPortal.Core.Testing;

namespace AutomationPortal.Actions.Documents.Tests;

public class AnswerTypeParsingTests
{
    [Theory]
    [InlineData("Yes", true)] [InlineData("yes.", true)] [InlineData("YES!", true)] [InlineData("Yes, it is urgent.", true)]
    [InlineData("**Yes**", true)] [InlineData("Yeah", true)] [InlineData("Yep", true)] [InlineData("true", true)] [InlineData("Y", true)]
    [InlineData("Sure thing", true)] [InlineData("\"yes\"", true)] [InlineData("  yes \n", true)]
    [InlineData("No", false)] [InlineData("no.", false)] [InlineData("NO", false)] [InlineData("No, it is not.", false)]
    [InlineData("Nope", false)] [InlineData("false", false)] [InlineData("N", false)] [InlineData("Nah", false)]
    [InlineData("{\"answer\": true}", true)] [InlineData("{\"answer\": false}", false)]
    [InlineData("```json\n{\"answer\": true}\n```", true)] [InlineData("{\"answer\": \"yes\"}", true)] [InlineData("{\"answer\": \"No\"}", false)]
    public void YesNo(string reply, bool expected) =>
        Assert.Equal(expected, AiAnswerParser.Parse(AiShape.Boolean, reply).AsBool(!expected));

    [Theory]
    [InlineData("Maybe")] [InlineData("It depends.")] [InlineData("")] [InlineData("Not sure")] [InlineData("{\"answer\": \"perhaps\"}")] [InlineData("42")]
    public void YesNoThatCantBeReadFails(string reply)
    {
        var ex = Assert.Throws<ActionException>(() => AiAnswerParser.Parse(AiShape.Boolean, reply));
        Assert.Contains("isn't a yes or no", ex.Message);
        Assert.StartsWith("The AI answered \"", ex.Message);
    }

    [Fact]
    public void FailureMessageQuotesTheAnswer() =>
        Assert.Equal("The AI answered \"Maybe\", which isn't a yes or no.",
            Assert.Throws<ActionException>(() => AiAnswerParser.Parse(AiShape.Boolean, "Maybe")).Message);

    [Fact]
    public void LongAnswersAreShortenedInTheMessage()
    {
        var ex = Assert.Throws<ActionException>(() => AiAnswerParser.Parse(AiShape.Boolean, new string('x', 500)));
        Assert.True(ex.Message.Length < 160);
    }

    [Theory]
    [InlineData("42", 42)] [InlineData("-7", -7)] [InlineData("3.14", 3.14)] [InlineData("-0.5", -0.5)] [InlineData(".5", 0.5)]
    [InlineData("The total is 12.5 kg", 12.5)] [InlineData("About 1,234 people", 1234)] [InlineData("1,234,567.89", 1234567.89)]
    [InlineData("{\"answer\": 42}", 42)] [InlineData("{\"answer\": -1.25}", -1.25)] [InlineData("{\"answer\": \"17\"}", 17)]
    [InlineData("```json\n{\"answer\": 8}\n```", 8)] [InlineData("Between 3 and 5", 3)] [InlineData("$19.99", 19.99)]
    public void Numbers(string reply, double expected) => Assert.Equal(expected, AiAnswerParser.Parse(AiShape.Number, reply).AsNumber());

    [Theory]
    [InlineData("de-DE")] [InlineData("fr-FR")] [InlineData("tr-TR")] [InlineData("ar-SA")]
    public void NumbersIgnoreTheCurrentCulture(string culture)
    {
        var old = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(culture);
            Assert.Equal(12.5, AiAnswerParser.Parse(AiShape.Number, "12.5").AsNumber());
            Assert.Equal(-3.25, AiAnswerParser.Parse(AiShape.Number, "{\"answer\": -3.25}").AsNumber());
            Assert.True(AiAnswerParser.Parse(AiShape.Boolean, "Yes").AsBool());
            Assert.Equal("Idle", AiAnswerParser.Parse(AiShape.Choice, "idle", new[] { "Idle", "Busy" }).AsText());
        }
        finally { Thread.CurrentThread.CurrentCulture = old; }
    }

    [Theory]
    [InlineData("no idea")] [InlineData("")] [InlineData("many")]
    public void NumbersThatCantBeReadFail(string reply)
    {
        var ex = Assert.Throws<ActionException>(() => AiAnswerParser.Parse(AiShape.Number, reply));
        Assert.Contains("isn't a number", ex.Message);
    }

    [Fact]
    public void ListsFromJsonObjectArrayBulletsAndNumbers()
    {
        Assert.Equal(new[] { "a", "b" }, AiAnswerParser.Parse(AiShape.List, "{\"items\":[\"a\",\"b\"]}").AsTexts());
        Assert.Equal(new[] { "a", "b" }, AiAnswerParser.Parse(AiShape.List, "[\"a\", \"b\"]").AsTexts());
        Assert.Equal(new[] { "a", "b" }, AiAnswerParser.Parse(AiShape.List, "```json\n[\"a\",\"b\"]\n```").AsTexts());
        Assert.Equal(new[] { "1", "2" }, AiAnswerParser.Parse(AiShape.List, "{\"items\":[1,2]}").AsTexts());
        Assert.Equal(new[] { "one", "two" }, AiAnswerParser.Parse(AiShape.List, "- one\n- two").AsTexts());
        Assert.Equal(new[] { "one", "two" }, AiAnswerParser.Parse(AiShape.List, "• one\r\n• two\r\n").AsTexts());
        Assert.Equal(new[] { "one", "two" }, AiAnswerParser.Parse(AiShape.List, "1. one\n2) two").AsTexts());
        Assert.Empty(AiAnswerParser.Parse(AiShape.List, "{\"items\":[]}").AsTexts());
    }

    [Theory]
    [InlineData("Billing", "Billing")] [InlineData("billing", "Billing")] [InlineData("BILLING.", "Billing")] [InlineData("\"Billing\"", "Billing")]
    [InlineData("{\"answer\": \"Billing\"}", "Billing")] [InlineData("{\"answer\": \"sales\"}", "Sales")] [InlineData("**Sales**", "Sales")]
    public void Choices(string reply, string expected) =>
        Assert.Equal(expected, AiAnswerParser.Parse(AiShape.Choice, reply, new[] { "Billing", "Sales", "Support" }).AsText());

    [Fact]
    public void ChoiceExactMatchWinsOverCaseInsensitive() =>
        Assert.Equal("ok", AiAnswerParser.Parse(AiShape.Choice, "ok", new[] { "OK", "ok" }).AsText());

    [Theory]
    [InlineData("Marketing")] [InlineData("")] [InlineData("Billing or Sales")]
    public void ChoiceOutsideTheOptionsFails(string reply)
    {
        var ex = Assert.Throws<ActionException>(() => AiAnswerParser.Parse(AiShape.Choice, reply, new[] { "Billing", "Sales" }));
        Assert.Contains("isn't one of the options (Billing, Sales)", ex.Message);
    }
}

public class AnswerRequestTests
{
    private static readonly IReadOnlyList<AiPart> In = new AiPart[] { new AiPart.Text("body") };

    [Fact]
    public void ChoiceRequestCarriesTheOptionsAndAnEnumSchema()
    {
        var r = AiRequestBuilder.Ask("Which team?", In, "choice", new[] { "Billing", " Sales ", "billing", "" });
        Assert.Equal(AiShape.Choice, r.Shape);
        Assert.Equal(new[] { "Billing", "Sales" }, r.Fields);
        Assert.Contains("\"enum\":[\"Billing\",\"Sales\"]", r.SchemaJson);
        Assert.Contains("\"Billing\", \"Sales\"", r.System);
    }

    [Theory]
    [InlineData(null)] [InlineData("")]
    public void ChoiceNeedsTwoOptions(string? only)
    {
        var ex = Assert.Throws<ActionException>(() => AiRequestBuilder.Ask("q", In, "choice", only is null ? null : new[] { only, "x" }));
        Assert.Contains("at least two options", ex.Message);
    }

    [Fact]
    public void UnknownAnswerTypeIsText() => Assert.Equal(AiShape.Text, AiRequestBuilder.Ask("q", In, "something-new").Shape);
}

/// <summary>These tests change static provider settings, so they must not run in parallel.</summary>
[Collection("ai-statics")]
public class AnswerTypeActionTests : IDisposable
{
    private readonly TestHost _host = T.Host();
    private readonly FakeProvider _fake = new();

    public AnswerTypeActionTests()
    {
        AiProviderRegistry.FactoryOverride = c => { _fake.Context = c; return _fake; };
        _host.Secrets.Set(AiProviderStore.KeyName("claude"), "k");
    }

    public void Dispose() { AiProviderRegistry.FactoryOverride = null; _host.Dispose(); }

    private Task<ContentValue> Ask(string reply, string answerAs, params (string, ParamValue)[] extra)
    {
        _fake.Reply = reply;
        return T.Run(_host, new AskAiAction(), null, new[] { ("prompt", ParamValue.Text("Is it urgent?")), ("answerAs", T.Choice(answerAs)) }.Concat(extra).ToArray());
    }

    [Fact]
    public async Task OldShortcutsWithoutAnswerAsStayText()
    {
        _fake.Reply = "Because.";
        var r = await T.Run(_host, new AskAiAction(), null, ("prompt", ParamValue.Text("Why?")));
        Assert.Equal("Because.", r.AsText());
        Assert.Equal(AiShape.Text, _fake.LastRequest!.Shape);
        Assert.Null(_fake.LastRequest.SchemaJson);
    }

    [Fact]
    public async Task YesNoSendsASchemaAndReturnsABoolean()
    {
        var r = await Ask("{\"answer\": true}", "yesno");
        Assert.True(r.AsBool());
        Assert.IsType<BooleanItem>(r[0]);
        Assert.NotNull(_fake.LastRequest!.SchemaJson);
    }

    [Fact]
    public async Task ReplyWithoutJsonStillWorks()
    {
        Assert.False((await Ask("No, it can wait.", "yesno")).AsBool(true));
        Assert.Equal(42, (await Ask("It is 42.", "number")).AsNumber());
        Assert.Equal(new[] { "a", "b" }, (await Ask("- a\n- b", "list")).AsTexts());
    }

    [Fact]
    public async Task UnreadableYesNoStopsTheStep()
    {
        var ex = await Assert.ThrowsAsync<ActionException>(() => Ask("Hard to say.", "yesno"));
        Assert.Equal("The AI answered \"Hard to say.\", which isn't a yes or no.", ex.Message);
    }

    [Fact]
    public async Task ChoiceUsesTheOptionsList()
    {
        var r = await Ask("{\"answer\":\"sales\"}", "choice", ("options", ParamValue.List("Billing", "Sales", "Support")));
        Assert.Equal("Sales", r.AsText());
        Assert.Equal(new[] { "Billing", "Sales", "Support" }, _fake.LastRequest!.Fields);
    }

    [Fact]
    public async Task ChoiceWithoutOptionsFailsBeforeAskingTheAi()
    {
        _fake.LastRequest = null;
        await Assert.ThrowsAsync<ActionException>(() => Ask("Sales", "choice"));
        Assert.Null(_fake.LastRequest);
    }

    [Fact]
    public async Task ChoiceOutsideTheOptionsStopsTheStep()
    {
        var ex = await Assert.ThrowsAsync<ActionException>(() => Ask("Marketing", "choice", ("options", ParamValue.List("Billing", "Sales"))));
        Assert.Contains("isn't one of the options", ex.Message);
    }

    [Theory]
    [InlineData(null, ContentKind.Text)] [InlineData("text", ContentKind.Text)] [InlineData("yesno", ContentKind.Boolean)]
    [InlineData("number", ContentKind.Number)] [InlineData("list", ContentKind.Any)] [InlineData("choice", ContentKind.Text)]
    [InlineData("dictionary", ContentKind.Dictionary)]
    public void OutputKindFollowsTheAnswerType(string? answerAs, ContentKind expected)
    {
        var a = answerAs is null ? ActionInstance.Create("ai.ask") : ActionInstance.Create("ai.ask", ("answerAs", T.Choice(answerAs)));
        Assert.Equal(expected, new AskAiAction().Descriptor.OutputKindOf(a));
    }

    [Fact]
    public void AnswerAsIsADropdownWithAHintAndOptionsShowOnlyForChoice()
    {
        var d = new AskAiAction().Descriptor;
        var answer = d.FindParameter("answerAs")!;
        Assert.Equal(ParamType.Choice, answer.Type);
        Assert.Contains(answer.Choices!, c => c.Id == "choice");
        Assert.False(string.IsNullOrWhiteSpace(answer.Hint));
        var options = d.FindParameter("options")!;
        Assert.True(options.IsVisible(k => k == "answerAs" ? "choice" : null));
        Assert.False(options.IsVisible(k => k == "answerAs" ? "text" : null));
    }

    private async Task<RunResult> RunFlow(Action<ShortcutBuilder> build)
    {
        var b = new ShortcutBuilder("Ai flow");
        build(b);
        return await _host.Runner.RunAsync(b.Build(), ContentValue.Empty, new RunOptions { Source = RunSource.Editor });
    }

    [Theory]
    [InlineData("yes", "urgent")] [InlineData("no", "later")]
    public async Task YesNoDrivesIf(string reply, string expected)
    {
        _fake.Reply = reply;
        var r = await RunFlow(b =>
        {
            var ask = b.Add("ai.ask", ("prompt", "Is it urgent?"), ("answerAs", T.Choice("yesno")));
            b.If(ParamValue.AllOf(ConditionRow.Of(ParamValue.Output(ask), ConditionOperators.IsTrue)),
                s => s.Add("content.text", ("text", "urgent")),
                s => s.Add("content.text", ("text", "later")));
        });
        Assert.Equal(RunStatus.Succeeded, r.Status);
        Assert.Equal(expected, r.Output.AsText());
    }

    [Theory]
    [InlineData("12", "big")] [InlineData("3", "small")]
    public async Task NumberDrivesComparison(string reply, string expected)
    {
        _fake.Reply = reply;
        var r = await RunFlow(b =>
        {
            var ask = b.Add("ai.ask", ("prompt", "How many?"), ("answerAs", T.Choice("number")));
            b.If(ParamValue.AllOf(ConditionRow.Of(ParamValue.Output(ask), ConditionOperators.GreaterThan, 10)),
                s => s.Add("content.text", ("text", "big")),
                s => s.Add("content.text", ("text", "small")));
        });
        Assert.Equal(expected, r.Output.AsText());
    }

    [Fact]
    public async Task ListDrivesRepeatWithEach()
    {
        _fake.Reply = "{\"items\":[\"red\",\"green\",\"blue\"]}";
        var r = await RunFlow(b =>
        {
            var ask = b.Add("ai.ask", ("prompt", "Name colors"), ("answerAs", T.Choice("list")));
            b.RepeatEach(ParamValue.Output(ask), s => s.Add("content.text", ("text", ParamValue.Text("<", VarRef.RepeatItem, ">"))));
        });
        Assert.Equal(RunStatus.Succeeded, r.Status);
        Assert.Equal("<red>\n<green>\n<blue>", r.Output.AsText());
    }

    [Fact]
    public async Task UnreadableAnswerFailsTheShortcutWithTheMessage()
    {
        _fake.Reply = "Maybe";
        var r = await RunFlow(b => b.Add("ai.ask", ("prompt", "Is it urgent?"), ("answerAs", T.Choice("yesno"))));
        Assert.Equal(RunStatus.Failed, r.Status);
        Assert.Contains("isn't a yes or no", r.ErrorMessage ?? "");
    }
}
