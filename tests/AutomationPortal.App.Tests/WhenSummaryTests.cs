using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.App.Tests;

public class WhenSummaryTests
{
    private static (TriggerRegistry T, ConditionRegistry C) Registries()
    {
        var t = new TriggerRegistry(); var c = new ConditionRegistry();
        AutomationPortal.Triggers.TriggerHost.RegisterProviders(t, c);
        return (t, c);
    }

    [Fact] public void PopularListIsTheSevenCommonTriggersAndAllExist()
    {
        var (t, _) = Registries();
        Assert.Equal(new[] { "time.daily", "key.shortcut", "system.signIn", "file.changed", "app.opened", "net.wifiJoined", "device.usb" },
            WhenSummary.Popular.Select(p => p.TriggerId));
        Assert.Equal(WhenSummary.Popular.Count, WhenSummary.AvailablePopular(t).Count);
        Assert.Equal("time.daily", WhenSummary.Popular[0].TriggerId);
    }

    [Fact] public void DefaultTimeIsEight()
    {
        Assert.Equal(new TimeOnly(8, 0), WhenSummary.DefaultTime);
        var (t, c) = Registries();
        var s = new AutomationWizardState();
        s.ChooseTrigger("time.daily");
        Assert.Equal("Every day at 08:00", WhenSummary.Text("time.daily", s.TriggerValues, t));
    }

    [Fact] public void DailyTextCoversDaySets()
    {
        var t = new TimeOnly(7, 30);
        Assert.Equal("Every day at 07:30", WhenSummary.DailyText(t, new string[0]));
        Assert.Equal("Every day at 07:30", WhenSummary.DailyText(t, new[] { "mon", "tue", "wed", "thu", "fri", "sat", "sun" }));
        Assert.Equal("Every weekday at 07:30", WhenSummary.DailyText(t, new[] { "fri", "mon", "tue", "wed", "thu" }));
        Assert.Equal("Every weekend day at 07:30", WhenSummary.DailyText(t, new[] { "sat", "sun" }));
        Assert.Equal("Every Mon, Wed at 07:30", WhenSummary.DailyText(t, new[] { "wed", "mon" }));
    }

    [Fact] public void ChoosingTheDailyTriggerNeedsOnlyAShortcut()
    {
        var (t, c) = Registries();
        var s = new AutomationWizardState();
        Assert.NotNull(s.ReadyMessage(t, c));
        s.ChooseTrigger("time.daily");
        Assert.Equal("Choose a shortcut to run.", s.ReadyMessage(t, c));
        s.ExistingShortcutId = Guid.NewGuid();
        Assert.Null(s.ReadyMessage(t, c));
        var a = s.Build(s.ExistingShortcutId.Value);
        Assert.True(a.Enabled);
        Assert.Equal(RunMode.Immediately, a.RunMode);
        Assert.Equal(0, a.CooldownSeconds);
    }

    [Fact] public void RequiredTriggerOptionsBlockUntilFilled()
    {
        var (t, c) = Registries();
        var s = new AutomationWizardState { ExistingShortcutId = Guid.NewGuid() };
        s.ChooseTrigger("app.opened");
        Assert.StartsWith("Fill in", s.ReadyMessage(t, c));
        s.TriggerValues["app"] = ParamValue.App("Spotify", "Spotify");
        Assert.Null(s.ReadyMessage(t, c));
        s.ClearTrigger();
        Assert.Null(s.TriggerId);
    }

    [Fact] public void OtherTriggersUseTheirOwnSummary()
    {
        var (t, _) = Registries();
        Assert.Equal("When I sign in or Automation Portal starts", WhenSummary.Text("system.signIn", new Dictionary<string, ParamValue>(), t));
    }
}
