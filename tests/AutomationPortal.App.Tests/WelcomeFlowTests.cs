using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.App.Tests;

public class WelcomeFlowTests
{
    [Fact]
    public void Walks_four_steps_and_the_last_one_finishes()
    {
        var f = new WelcomeFlow();
        Assert.Equal(0, f.Current);
        Assert.False(f.CanGoBack);
        Assert.False(f.Back());
        Assert.True(f.Next()); Assert.True(f.Next()); Assert.True(f.Next());
        Assert.True(f.IsLast);
        Assert.Equal(WelcomeFlow.PrivacyStep, f.Current);
        Assert.False(f.Next());
        Assert.True(f.Back());
        Assert.Equal(WelcomeFlow.TryItStep, f.Current);
    }

    [Fact]
    public void Steps_have_ui_automation_ids()
    {
        Assert.Equal(new[] { "WelcomeStep1", "WelcomeStep2", "WelcomeStep3", "WelcomeStep4" },
            Enumerable.Range(0, WelcomeFlow.StepCount).Select(WelcomeFlow.StepAutomationId));
    }

    [Fact]
    public void Crash_reports_start_on_for_a_fresh_install_and_follow_a_saved_choice()
    {
        Assert.True(WelcomeFlow.CrashReportsDefault(new AppSettings()));
        Assert.False(WelcomeFlow.CrashReportsDefault(new AppSettings { CrashReportsChoice = false }));
    }

    [Fact]
    public void Crash_choice_is_saved_and_applied_straight_away()
    {
        var settings = new AppSettings();
        bool? applied = null;
        WelcomeFlow.SaveCrashChoice(false, v => settings.SendCrashReports = v, v => applied = v);
        Assert.False(settings.SendCrashReports);
        Assert.Equal(false, settings.CrashReportsChoice);
        Assert.False(applied);
        WelcomeFlow.SaveCrashChoice(true, v => settings.SendCrashReports = v, null);
        Assert.True(settings.SendCrashReports);
    }

    [Fact]
    public void Try_sample_is_one_text_step_with_only_typed_text_and_a_case_choice()
    {
        var s = TrySample.Create("hello there");
        var step = Assert.Single(s.Actions);
        Assert.Equal("text.case", step.ActionId);
        Assert.All(step.Parameters.Values, v => Assert.True(v is TextValue or ChoiceValue));
        Assert.Equal(SampleShortcut.SampleId, s.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Try_sample_falls_back_to_the_default_text(string? typed)
    {
        var step = TrySample.Create(typed).Actions[0];
        var part = Assert.Single(((TextValue)step.Parameters["text"]).Parts);
        Assert.Equal(TrySample.DefaultText, part.Literal);
    }

    [Fact]
    public void Welcome_strings_exist()
    {
        foreach (var k in new[] { "Welcome2_HelloTitle", "Welcome2_TryTitle", "Welcome2_PrivacyTitle", "Welcome2_GetStarted", "Welcome2_TrySampleName" })
            Assert.NotEqual(k, Strings.Get(k));
    }
}
