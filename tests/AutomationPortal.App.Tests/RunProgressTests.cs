using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.App.Tests;

public class RunProgressTests
{
    private static ActionInstance Step(string uid, string id = "x") => new() { ActionId = id, Uid = uid };

    private static string? Title(string id) => id switch { "resize" => "Resize pictures", "each" => "Repeat with each", "end" => "End repeat", _ => id };

    [Fact]
    public void Names_the_step_and_its_place_in_the_shortcut()
    {
        var t = new RunProgressTracker(new[] { Step("a"), Step("b"), Step("c", "resize") }, Title);
        t.OnStepStarted("c");
        Assert.Equal("Step 3 of 3: Resize pictures", t.Text());
    }

    [Fact]
    public void Shows_nothing_before_a_step_has_started()
    {
        var t = new RunProgressTracker(new[] { Step("a") }, Title);
        Assert.Null(t.Text());
    }

    [Fact]
    public void A_single_step_has_no_step_number()
    {
        var t = new RunProgressTracker(new[] { Step("a", "resize") }, Title);
        t.OnStepStarted("a");
        Assert.Equal("Resize pictures", t.Text());
    }

    [Fact]
    public void A_real_count_is_added_on_a_second_line()
    {
        var t = new RunProgressTracker(new[] { Step("a", "resize"), Step("b") }, Title);
        t.OnStepStarted("a");
        t.OnCount("a", 12, 40);
        Assert.Equal("Step 1 of 2: Resize pictures\n12 of 40", t.Text());
    }

    [Fact]
    public void A_count_without_a_total_shows_only_the_count()
    {
        var t = new RunProgressTracker(new[] { Step("a", "resize") }, Title);
        t.OnStepStarted("a");
        t.OnCount("a", 7, null);
        Assert.Equal("Resize pictures\n7 so far", t.Text());
    }

    [Fact]
    public void The_count_goes_when_the_next_step_starts()
    {
        var t = new RunProgressTracker(new[] { Step("a", "resize"), Step("b") }, Title);
        t.OnStepStarted("a");
        t.OnCount("a", 40, 40);
        t.OnStepStarted("b");
        Assert.DoesNotContain("40", t.Text());
    }

    [Fact]
    public void A_loop_count_stays_while_its_inner_steps_run_and_goes_after_the_loop()
    {
        var start = Step("s", "each").WithFlow("g", FlowRole.Start);
        var end = Step("e", "end").WithFlow("g", FlowRole.End);
        var t = new RunProgressTracker(new[] { start, Step("inner", "resize"), end, Step("after") }, Title);
        t.OnStepStarted("s");
        t.OnCount("s", 3, 10);
        t.OnStepStarted("inner");
        Assert.EndsWith("3 of 10", t.Text());
        t.OnStepStarted("after");
        Assert.DoesNotContain("3 of 10", t.Text());
    }
}
