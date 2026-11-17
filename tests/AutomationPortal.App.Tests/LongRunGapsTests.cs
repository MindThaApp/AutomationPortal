using AutomationPortal.App.Logic;
using AutomationPortal.App.Services;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.App.Tests;

public class LongRunGapsTests
{
    private static PaletteEntry S(string title) => new(PaletteKind.Shortcut, title, null, "s:" + title);

    [Fact]
    public void Enter_with_nothing_matching_makes_a_shortcut_with_the_typed_name()
    {
        var d = PaletteEnter.Decide(null, "  Weekly report ");
        Assert.Null(d.Entry);
        Assert.Equal("Weekly report", d.NewShortcutName);
    }

    [Fact]
    public void Enter_with_a_highlighted_line_uses_it_and_never_makes_a_shortcut()
    {
        var d = PaletteEnter.Decide(S("Resize"), "res");
        Assert.NotNull(d.Entry);
        Assert.Null(d.NewShortcutName);
    }

    [Fact]
    public void Enter_with_nothing_typed_and_nothing_highlighted_does_nothing()
    {
        var d = PaletteEnter.Decide(null, "   ");
        Assert.Null(d.Entry);
        Assert.Null(d.NewShortcutName);
    }

    [Fact]
    public void Taskbar_stays_clear_until_a_person_started_run_is_long()
    {
        var t = new LongRunTaskbar();
        var id = Guid.NewGuid();
        t.OnState(id, RunSource.Library, TileRunState.Running);
        Assert.Equal(TaskbarProgressKind.None, t.Current.Kind);
        t.OnProgress(id, null);
        Assert.Equal(TaskbarProgressKind.Indeterminate, t.Current.Kind);
    }

    [Fact]
    public void Taskbar_shows_the_fraction_when_the_total_is_known_and_clears_when_done()
    {
        var t = new LongRunTaskbar();
        var id = Guid.NewGuid();
        t.OnState(id, RunSource.QuickRun, TileRunState.Running);
        t.OnProgress(id, (10, 40));
        Assert.Equal(TaskbarProgressKind.Normal, t.Current.Kind);
        Assert.Equal(0.25, t.Current.Value, 3);
        t.OnState(id, RunSource.QuickRun, TileRunState.Failed);
        Assert.Equal(TaskbarProgressKind.None, t.Current.Kind);
    }

    [Fact]
    public void Taskbar_ignores_runs_that_start_by_themselves()
    {
        var t = new LongRunTaskbar();
        var id = Guid.NewGuid();
        t.OnState(id, RunSource.Automation, TileRunState.Running);
        t.OnProgress(id, (1, 5));
        Assert.Equal(TaskbarProgressKind.None, t.Current.Kind);
    }

    [Fact]
    public void Taskbar_with_two_long_runs_just_moves()
    {
        var t = new LongRunTaskbar();
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        t.OnState(a, RunSource.Library, TileRunState.Running); t.OnState(b, RunSource.Library, TileRunState.Running);
        t.OnProgress(a, (1, 5)); t.OnProgress(b, (2, 9));
        Assert.Equal(TaskbarProgressKind.Indeterminate, t.Current.Kind);
    }

    [Fact]
    public void Tracker_reports_the_count_and_its_line()
    {
        var actions = new[] { new ActionInstance { ActionId = "x", Uid = "a" } };
        var tr = new RunProgressTracker(actions, _ => "Step");
        Assert.Null(tr.Count());
        tr.OnStepStarted("a");
        tr.OnCount("a", 12, 40);
        Assert.Equal((12, (int?)40), tr.Count());
        Assert.Equal("12 of 40", RunProgressText.CountLine(tr.Count()));
        Assert.Null(RunProgressText.CountLine(null));
    }
}
