using System.IO;
using AutomationPortal.App.Logic;
using AutomationPortal.App.Services;
using Xunit;

namespace AutomationPortal.App.Tests;

public class SimulatedPlanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ap-plansim-" + Guid.NewGuid().ToString("N"));

    public SimulatedPlanTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private EntitlementService Make(string value)
    {
        var plan = SimulatedPlan.TryCreate(value, true, _root);
        Assert.NotNull(plan);
        return plan;
    }

    [Fact]
    public void Ignored_without_a_custom_data_root()
    {
        Assert.Null(SimulatedPlan.TryCreate("pro", false, _root));
        Assert.False(File.Exists(Path.Combine(_root, "trial.json")));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("plus")] [InlineData("trial:0")] [InlineData("trial:15")] [InlineData("trial:x")] [InlineData("trial:-1")]
    public void Bad_or_missing_values_are_ignored(string? value) => Assert.Null(SimulatedPlan.TryCreate(value, true, _root));

    [Fact]
    public void Free_offers_the_trial()
    {
        var p = Make("free");
        Assert.True(p.OwnershipKnown);
        Assert.Equal(Tier.Free, p.Tier);
        Assert.Null(p.TrialDaysLeft);
        Assert.Null(p.TrialChipDays);
        Assert.True(p.CanStartTrial);
        Assert.False(File.Exists(Path.Combine(_root, "trial.json")));
    }

    [Fact]
    public void Pro_is_pro_without_a_trial_offer()
    {
        var p = Make("pro");
        Assert.Equal(Tier.Pro, p.Tier);
        Assert.False(p.CanStartTrial);
        Assert.Null(p.TrialDaysLeft);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(5, 5, 5)]
    [InlineData(7, 7, 7)]
    [InlineData(10, 10, null)]
    [InlineData(14, 14, null)]
    public void Trial_reports_the_days_left(int n, int left, int? chip)
    {
        var p = Make($"trial:{n}");
        Assert.Equal(Tier.Pro, p.Tier);
        Assert.Equal(left, p.TrialDaysLeft);
        Assert.Equal(chip, p.TrialChipDays);
        Assert.False(p.CanStartTrial);
    }

    [Fact]
    public void Trial_of_one_day_shows_the_toast_once()
    {
        var p = Make("trial:1");
        Assert.Equal(TrialAction.Toast, TrialMoments.Next(p.Trial!, p.OwnershipKnown, false));
        Assert.Equal(TrialAction.None, TrialMoments.Next(p.Trial!, p.OwnershipKnown, false));
    }

    [Fact]
    public void Trial_ended_shows_the_summary_on_the_first_tick()
    {
        var p = Make("trial-ended");
        Assert.Equal(Tier.Free, p.Tier);
        Assert.Null(p.TrialDaysLeft);
        Assert.False(p.CanStartTrial);
        Assert.Equal(TrialAction.Summary, TrialMoments.Next(p.Trial!, p.OwnershipKnown, false));
        Assert.Equal(TrialAction.None, TrialMoments.Next(p.Trial!, p.OwnershipKnown, false));
    }

    [Fact]
    public void Trial_modes_rewrite_trial_json_fresh_each_launch()
    {
        Make("trial-ended").Trial!.MarkSummaryShown();
        Assert.Equal(TrialAction.Summary, TrialMoments.Next(Make("trial-ended").Trial!, true, false));
    }

    [Fact]
    public void Trial_modes_build_no_registry_marker() =>
        Assert.Null(typeof(ProTrial).GetField("_marker", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(Make("trial:3").Trial));
}
