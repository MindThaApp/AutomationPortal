using AutomationPortal.Actions.Documents;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using Xunit;

namespace AutomationPortal.Actions.Documents.Tests;

[Collection("ai-statics")]
public class ShortcutExplainTests : IDisposable
{
    private readonly TestHost _host = T.Host();

    public ShortcutExplainTests()
    {
        _host.With(typeof(AutomationPortal.Actions.Content.Utilities.ExpressionEvaluator).Assembly)
            .With(typeof(AutomationPortal.Actions.Windows.Apps.InstalledApps).Assembly);
    }

    public void Dispose() => _host.Dispose();

    private const string SecretText = "TOP-SECRET-LITERAL";
    private const string FileContents = "CONTENTS-OF-A-PRIVATE-FILE";
    private const string ClipboardText = "CLIPBOARD-PASSWORD";

    private static Shortcut Original()
    {
        var b = new ShortcutBuilder("Morning weather", "WeatherSunny24", "orange");
        b.Add("weather.current", ("location", ParamValue.Text("London")));
        b.Add("ui.notify", ("title", ParamValue.Text("Hi")), ("body", ParamValue.Text(SecretText)));
        var s = b.Build();
        s.Actions[0].Uid = "s1";
        s.Actions[1].Uid = "s2";
        return s;
    }

    private static RunRecord Failed(Shortcut s) => new()
    {
        RunId = "r1", ShortcutId = s.Id, ShortcutName = s.Name, Status = RunStatus.Failed,
        ErrorMessage = "Couldn't reach the weather service.", FailedStepUid = "s1",
        OutputPreview = FileContents + ClipboardText,
        Steps = new List<StepLog>
        {
            new() { Uid = "s1", ActionId = "weather.current", Title = "Get Weather", OutputPreview = FileContents, Error = "Couldn't reach the weather service." },
        },
    };

    private ShortcutAiExplainer Explainer(QueueProvider p) => new(_host.Registry, null, p, "m", false);

    private const string GoodFix = """
        {"name":"Morning weather","icon":"WeatherSunny24","color":"orange","summary":"Asks for the place instead of guessing.",
         "steps":[
          {"actionId":"weather.current","uid":"s1","parameters":{"location":"London"}},
          {"actionId":"ui.notify","uid":"s2","parameters":{"body":"Today: {{s1.key:Summary}}"}}]}
        """;

    [Fact]
    public async Task ErrorPromptHasNamesAndErrorButNoContents()
    {
        var p = new QueueProvider("It couldn't get the weather. Nothing was changed. Check your internet connection.");
        var s = Original();
        var r = await Explainer(p).ExplainErrorAsync(s, Failed(s), default);
        Assert.True(r.Success, r.Error);
        var sent = p.UserText(0) + p.Requests[0].System;
        Assert.Contains("weather.current", sent);
        Assert.Contains("Couldn't reach the weather service.", sent);
        Assert.DoesNotContain(FileContents, sent);
        Assert.DoesNotContain(ClipboardText, sent);
        Assert.DoesNotContain(SecretText, sent);
        Assert.Single(p.Requests);
    }

    [Fact]
    public async Task ShortcutPromptListsStepsAndFlagsFileChangesWithoutValues()
    {
        var p = new QueueProvider("1. Gets the weather. 2. Shows it.");
        var r = await Explainer(p).ExplainShortcutAsync(Original(), default);
        Assert.True(r.Success, r.Error);
        var sent = p.UserText(0);
        Assert.Contains("1. ", sent);
        Assert.Contains("ui.notify", sent);
        Assert.DoesNotContain(SecretText, sent);
        Assert.Contains("(changes files)", p.Requests[0].System);
    }

    [Fact]
    public async Task FixReturnsAValidatedDraftAndLeavesTheOriginalAlone()
    {
        var p = new QueueProvider(GoodFix);
        var s = Original();
        var before = AutomationPortalJson.Serialize(s);
        var librarySize = _host.Services.Library.All.Count;
        var r = await Explainer(p).FixAsync(s, Failed(s), default);
        Assert.True(r.Success, r.Error);
        Assert.Empty(ShortcutValidator.Validate(r.Shortcut!, _host.Registry));
        Assert.NotSame(s, r.Shortcut);
        Assert.Equal(before, AutomationPortal.Core.AutomationPortalJson.Serialize(s));
        Assert.Equal(librarySize, _host.Services.Library.All.Count);
        Assert.Contains("Couldn't reach the weather service.", p.UserText(0));
        Assert.DoesNotContain(FileContents, p.UserText(0));
    }

    [Fact]
    public async Task FixWithAnUnusableAnswerFailsInsteadOfGuessing()
    {
        var p = new QueueProvider("nope", "still nope", "no");
        var s = Original();
        var r = await Explainer(p).FixAsync(s, Failed(s), default);
        Assert.False(r.Success);
    }

    [Fact]
    public async Task WithoutAReadyProviderTheAnswerPointsToSettings()
    {
        var s = Original();
        var explain = await ShortcutAiService.ExplainErrorAsync(_host.Services, _host.Registry, null, s, Failed(s));
        Assert.False(explain.Success);
        Assert.True(explain.NeedsSetup);
        Assert.Contains("Settings", explain.Error);
        var shortcut = await ShortcutAiService.ExplainShortcutAsync(_host.Services, _host.Registry, null, s);
        Assert.True(shortcut.NeedsSetup);
        var fix = await ShortcutAiService.FixAsync(_host.Services, _host.Registry, null, s, Failed(s));
        Assert.False(fix.Success);
        Assert.Contains("Settings", fix.Error);
    }
}
