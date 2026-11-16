using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.App.Tests;

public class StarterSnippetsTests
{
    [Fact]
    public void An_empty_page_gets_both_starters() =>
        Assert.Equal(new[] { ";date", ";addr" }, StarterSnippets.Missing(Array.Empty<Snippet>()).Select(s => s.Abbreviation));

    [Fact]
    public void A_starter_whose_code_is_taken_is_left_out()
    {
        var left = StarterSnippets.Missing(new[] { new Snippet { Abbreviation = ";DATE", Text = "mine" } });
        Assert.Equal(new[] { ";addr" }, left.Select(s => s.Abbreviation));
    }

    [Fact]
    public void The_text_expansion_gallery_items_are_recognised()
    {
        Assert.True(StarterSnippets.IsGalleryDuplicate("Text Expansion: Today's Date"));
        Assert.False(StarterSnippets.IsGalleryDuplicate("Tidy Downloads"));
    }
}
