using AutomationPortal.App.Integration;
using AutomationPortal.App.Logic;
using Xunit;

namespace AutomationPortal.App.Tests;

public class QuickRunFallbacksTests
{
    private static QuickRunItem Item(string id, Guid? shortcut = null) =>
        new() { Id = id, Title = id, ShortcutId = shortcut, ExecuteAsync = _ => Task.CompletedTask };

    [Fact]
    public void Appear_only_when_nothing_of_the_users_matched()
    {
        Assert.True(QuickRunFallbacks.Applies("zebra", Array.Empty<QuickRunItem>()));
        Assert.True(QuickRunFallbacks.Applies("zebra", new[] { Item("web-search"), Item("calc") }));
        Assert.False(QuickRunFallbacks.Applies("zebra", new[] { Item("shortcut:1", Guid.NewGuid()) }));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    public void Not_for_empty_or_one_letter_text(string typed) => Assert.False(QuickRunFallbacks.Applies(typed, Array.Empty<QuickRunItem>()));

    [Fact]
    public async Task Ready_ai_gives_an_ask_row_that_stays_open_and_a_make_row()
    {
        string? log = null;
        var rows = QuickRunFallbacks.Build("  plan my day ", true, "Stays on this PC", () => { log += "ask;"; return Task.CompletedTask; },
            () => { log += "setup;"; return Task.CompletedTask; }, () => { log += "make;"; return Task.CompletedTask; });

        Assert.Equal(2, rows.Count);
        Assert.Equal("Ask AI: plan my day", rows[0].Title);
        Assert.Equal("Stays on this PC", rows[0].Subtitle);
        Assert.True(rows[0].KeepsWindowOpen);
        Assert.Equal("Make a shortcut: plan my day", rows[1].Title);
        Assert.False(rows[1].KeepsWindowOpen);
        Assert.All(rows, r => Assert.True(QuickRunFallbacks.IsFallback(r)));

        await rows[0].ExecuteAsync(new QuickRunInvocation());
        await rows[1].ExecuteAsync(new QuickRunInvocation());
        Assert.Equal("ask;make;", log);
    }

    [Fact]
    public async Task No_ai_ready_turns_the_ask_row_into_setup()
    {
        string? log = null;
        var rows = QuickRunFallbacks.Build("plan my day", false, "", () => { log += "ask;"; return Task.CompletedTask; },
            () => { log += "setup;"; return Task.CompletedTask; }, () => { log += "make;"; return Task.CompletedTask; });

        Assert.Equal("Set up AI…", rows[0].Title);
        Assert.False(rows[0].KeepsWindowOpen);
        await rows[0].ExecuteAsync(new QuickRunInvocation());
        Assert.Equal("setup;", log);
    }
}
