using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using AutomationPortal.App.Dialogs;
using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.App.Tests;

public class WelcomeFlowTests
{
    [Fact]
    public void Walks_three_steps_and_the_last_one_finishes()
    {
        var f = new WelcomeFlow();
        Assert.Equal(0, f.Current);
        Assert.False(f.CanGoBack);
        Assert.False(f.Back());
        Assert.True(f.Next()); Assert.True(f.Next());
        Assert.True(f.IsLast);
        Assert.Equal(WelcomeFlow.TryItStep, f.Current);
        Assert.False(f.Next());
        Assert.True(f.Back());
        Assert.Equal(WelcomeFlow.StartersStep, f.Current);
    }

    [Fact]
    public void Steps_have_ui_automation_ids()
    {
        Assert.Equal(new[] { "WelcomeStep1", "WelcomeStep2", "WelcomeStep3" },
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
    public void Recommended_picks_are_the_three_defaults_when_all_are_shown()
    {
        var shown = new[] { "a/unit-converter.autoportal", "a/weather-now.autoportal", "a/quick-note.autoportal", "a/good-morning.autoportal", "a/end-of-day.autoportal" };
        Assert.Equal(new[] { 1, 2, 3 }, FirstRun.RecommendedIndexes(shown));
    }

    [Fact]
    public void Recommended_picks_top_up_with_the_first_shown_when_a_default_is_missing()
    {
        var shown = new[] { "a/unit-converter.autoportal", "a/weather-now.autoportal", "a/end-of-day.autoportal", "a/other.autoportal" };
        Assert.Equal(new[] { 0, 1, 2 }, FirstRun.RecommendedIndexes(shown));
        Assert.Equal(new[] { 0, 1 }, FirstRun.RecommendedIndexes(new[] { "x.autoportal", "y.autoportal" }));
    }

    [Fact]
    public void Hero_squares_use_six_distinct_category_colours()
    {
        var names = WelcomeFlow.HeroColours.Select(AutomationPortal.Editor.Palette.CategoryName).ToList();
        Assert.Equal(6, names.Distinct().Count());
        Assert.Equal("Violet", AutomationPortal.Editor.Palette.CategoryName("purple"));
    }

    [Fact]
    public void Welcome_strings_exist()
    {
        foreach (var k in new[] { "Welcome2_HelloTitle", "Welcome2_TryTitle", "Welcome2_StartRecommended", "Welcome2_Blurb_quick_note", "Welcome2_StartUsing", "Welcome2_Recommended", "Welcome2_CrashTitle", "Welcome2_CrashDesc", "Welcome2_ChangeLater", "Welcome2_TrySampleName" })
            Assert.NotEqual(k, Strings.Get(k));
    }

    private static string TokenColour(string theme)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "AutomationPortal.Editor", "Resources"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var xaml = File.ReadAllText(Path.Combine(dir!.FullName, "src", "AutomationPortal.Editor", "Resources", "Tokens." + theme + ".xaml"));
        return Regex.Match(xaml, "x:Key=\"DsTextSecondaryBrush\" Color=\"#([0-9A-Fa-f]{8})\"").Groups[1].Value.ToUpperInvariant();
    }

    [Theory]
    [InlineData(false, "Light")]
    [InlineData(true, "Dark")]
    public void Secondary_text_resolves_to_the_secondary_token_at_full_opacity(bool dark, string theme)
    {
        var expected = TokenColour(theme);
        Assert.Equal(dark ? "C9FFFFFF" : "9C000000", expected);
        SharedSta.Run(() =>
        {
            // The step 1 body, the step subtitles and the card descriptions are all built this way.
            foreach (var size in new[] { 13.0, 14.0 })
            {
                var t = WelcomeWindow.SecondaryText("sample", size, dark);
                var brush = Assert.IsType<SolidColorBrush>(t.Foreground);
                Assert.Equal(1.0, t.Opacity);
                Assert.Equal(1.0, brush.Opacity);
                Assert.Equal(expected, brush.Color.ToString().TrimStart(((char)35)).ToUpperInvariant());
            }
        });
    }
}
