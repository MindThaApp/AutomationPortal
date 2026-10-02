using System.Windows.Input;
using Flowmate.App.Logic;
using Flowmate.Core;
using Xunit;

namespace Flowmate.App.Tests;

public class FuzzyMatcherTests
{
    [Fact] public void ExactBeatsPrefixBeatsContains()
    {
        var exact = FuzzyMatcher.Score("focus", "Focus");
        var prefix = FuzzyMatcher.Score("foc", "Focus Mode");
        var word = FuzzyMatcher.Score("mode", "Focus Mode");
        var contains = FuzzyMatcher.Score("cus", "Focus Mode");
        Assert.True(exact > prefix && prefix > word && word > contains);
    }

    [Fact] public void SubsequenceMatchesAndNonMatchIsNull()
    {
        Assert.NotNull(FuzzyMatcher.Score("gmr", "Good Morning"));
        Assert.Null(FuzzyMatcher.Score("xyz", "Good Morning"));
    }

    [Fact] public void ExactAliasWins()
    {
        var alias = FuzzyMatcher.ScoreNameAndAlias("gm", "Good Morning", "gm");
        var name = FuzzyMatcher.ScoreNameAndAlias("good", "Good Morning", "gm");
        Assert.True(alias > name);
    }

    [Fact] public void EmptyQueryMatchesEverything() => Assert.Equal(0, FuzzyMatcher.Score("  ", "Anything"));
}

public class HotkeyFormatTests
{
    [Fact] public void FormatsModifiersInStableOrder() =>
        Assert.Equal("Ctrl+Alt+K", HotkeyFormat.Format(ModifierKeys.Alt | ModifierKeys.Control, Key.K));

    [Fact] public void BareModifierIsNotAHotkey() => Assert.Null(HotkeyFormat.Format(ModifierKeys.Control, Key.LeftCtrl));

    [Theory]
    [InlineData("Ctrl+Alt+Space", ModifierKeys.Control | ModifierKeys.Alt, Key.Space)]
    [InlineData("Shift+Win+5", ModifierKeys.Windows | ModifierKeys.Shift, Key.D5)]
    [InlineData("F12", ModifierKeys.None, Key.F12)]
    public void RoundTrips(string text, ModifierKeys mods, Key key)
    {
        Assert.True(HotkeyFormat.TryParse(text, out var m, out var k));
        Assert.Equal(mods, m);
        Assert.Equal(key, k);
        Assert.Equal(text, HotkeyFormat.Format(m, k));
    }

    [Fact] public void RejectsGarbage() => Assert.False(HotkeyFormat.TryParse("Banana+K", out _, out _));
}

public class GalleryIndexTests
{
    [Fact] public void ParsesSections()
    {
        var plan = GalleryIndex.Parse("""{"sections":[{"title":"Get Started","files":["a.flowmate","b"]},{"title":"","files":[]}]}""");
        Assert.Single(plan);
        Assert.Equal("Get Started", plan[0].Title);
        Assert.Equal(2, plan[0].Files.Count);
    }

    [Theory] [InlineData(null)] [InlineData("")] [InlineData("not json")] [InlineData("[1,2]")] [InlineData("""{"sections":5}""")]
    public void BadIndexGivesEmptyPlan(string? json) => Assert.Empty(GalleryIndex.Parse(json));

    [Fact] public void ArrangeUsesIndexOrderAndAppendsLeftovers()
    {
        var files = new[] { @"c:\g\a.flowmate", @"c:\g\b.flowmate", @"c:\g\c.flowmate" };
        var plan = GalleryIndex.Parse("""{"sections":[{"title":"One","files":["b"]},{"title":"Empty","files":["missing.flowmate"]}]}""");
        var arranged = GalleryIndex.Arrange(files, plan);
        Assert.Equal(2, arranged.Count);
        Assert.Equal("One", arranged[0].Title);
        Assert.Equal(new[] { @"c:\g\b.flowmate" }, arranged[0].Files);
        Assert.Equal(GalleryIndex.FallbackSectionTitle, arranged[1].Title);
        Assert.Equal(2, arranged[1].Files.Count);
    }

    [Fact] public void NoIndexPutsEverythingInOneSection()
    {
        var arranged = GalleryIndex.Arrange(new[] { @"c:\g\a.flowmate" }, GalleryIndex.Parse(null));
        Assert.Single(arranged);
        Assert.Equal("All shortcuts", arranged[0].Title);
    }
}

public class OrderingTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid();

    [Fact] public void MovesBeforeTarget() => Assert.Equal(new[] { C, A, B }, ShortcutOrdering.MoveBefore(new[] { A, B, C }, C, A));
    [Fact] public void NullTargetMovesToEnd() => Assert.Equal(new[] { B, C, A }, ShortcutOrdering.MoveBefore(new[] { A, B, C }, A, null));
    [Fact] public void ApplyToGlobalKeepsHiddenItemsInPlace() =>
        Assert.Equal(new[] { C, D, A }, ShortcutOrdering.ApplyToGlobal(new[] { A, D, C }, new[] { C, A }));
}

public class WizardTests
{
    private static (TriggerRegistry T, ConditionRegistry C) Registries()
    {
        var t = new TriggerRegistry(); var c = new ConditionRegistry();
        Flowmate.Triggers.TriggerHost.RegisterProviders(t, c);
        return (t, c);
    }

    [Fact] public void RealProvidersAreRegistered()
    {
        var (t, c) = Registries();
        Assert.NotNull(t.Get("app.opened"));
        Assert.NotNull(t.Get("time.interval"));
        Assert.NotNull(c.Get("cond.power"));
        Assert.True(t.All.Count >= 25);
        Assert.Contains(t.All, p => p.Descriptor.Parameters.Any(x => x.Type == ParamType.List && x.Choices is { Count: > 0 }));
    }
    [Fact] public void StepsBlockUntilRequiredThingsAreFilled()
    {
        var (t, c) = Registries();
        var s = new AutomationWizardState();
        Assert.NotNull(s.BlockingMessage(t, c));
        s.SelectTrigger("app.opened");
        Assert.True(s.TryNext(t, c));
        Assert.NotNull(s.BlockingMessage(t, c));                 // app is required
        Assert.False(s.TryNext(t, c));
        s.TriggerValues["app"] = ParamValue.App("Spotify", "Spotify");
        Assert.Null(s.BlockingMessage(t, c));
        Assert.True(s.TryNext(t, c));
        Assert.NotNull(s.BlockingMessage(t, c));                 // no shortcut yet
        s.ExistingShortcutId = Guid.NewGuid();
        Assert.True(s.TryNext(t, c));
        Assert.True(s.TryBack());
    }

    [Fact] public void BuildProducesTheAutomation()
    {
        var (t, _) = Registries();
        var s = new AutomationWizardState { RunMode = RunMode.AskFirst, NotifyWhenRun = true, CooldownMinutes = 5 };
        s.SelectTrigger("time.daily");
        s.AddCondition("cond.days").Values["days"] = ParamValue.List("mon");
        var id = Guid.NewGuid();
        var a = s.Build(id);
        Assert.Equal("time.daily", a.Trigger.TriggerId);
        Assert.Equal(300, a.CooldownSeconds);
        Assert.Equal(RunMode.AskFirst, a.RunMode);
        Assert.Equal(id, a.ShortcutId);
        Assert.Single(a.Conditions);
    }

    [Fact] public void NewShortcutNameMustBeFreeAndNotEmpty()
    {
        var (t, c) = Registries();
        var lib = new Flowmate.Core.Testing.InMemoryShortcutLibrary();
        lib.Save(new Shortcut { Name = "Taken" });
        var s = new AutomationWizardState { CreateNewShortcut = true };
        s.SelectTrigger("system.signIn");
        Assert.True(s.TryNext(t, c, lib));
        Assert.True(s.TryNext(t, c, lib));
        Assert.NotNull(s.BlockingMessage(t, c, lib));
        s.NewShortcutName = "taken";
        Assert.NotNull(s.BlockingMessage(t, c, lib));
        s.NewShortcutName = "Fresh";
        Assert.Null(s.BlockingMessage(t, c, lib));
    }

    [Fact] public void SentenceReadsNaturally()
    {
        var (t, c) = Registries();
        var lib = new Flowmate.Core.Testing.InMemoryShortcutLibrary();
        var sc = new Shortcut { Name = "Focus Mode" }; lib.Save(sc);
        var a = new Automation { ShortcutId = sc.Id, Trigger = new TriggerInstance { TriggerId = "app.opened", Parameters = { ["app"] = ParamValue.App("Spotify", "Spotify") } } };
        a.Conditions.Add(new ConditionInstance { ConditionId = "cond.power", Parameters = { ["state"] = ParamValue.Choice("battery") } });
        var m = AutomationSentence.Build(a, t, c, lib);
        Assert.Equal("When Spotify opens  →  Run Focus Mode", string.Concat(m.Parts.Select(p => p.Text)));
        Assert.Equal("Only if I'm on battery", m.ConditionsText);
    }

    [Theory]
    [InlineData(90, "1 minute 30 seconds")] [InlineData(3600, "1 hour")] [InlineData(172800, "2 days")]
    public void DurationsAreFriendly(double seconds, string text) => Assert.Equal(text, LiteralFormatter.Duration(seconds));
}

