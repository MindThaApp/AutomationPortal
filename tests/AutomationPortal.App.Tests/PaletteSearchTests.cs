using AutomationPortal.App.Logic;
using Xunit;

namespace AutomationPortal.App.Tests;

public class PaletteSearchTests
{
    private static PaletteEntry S(string title, string? alias = null) => new(PaletteKind.Shortcut, title, null, "s:" + title, alias);
    private static PaletteEntry C(string title, params string[] words) => new(PaletteKind.Command, title, null, "c:" + title, null, words);
    private static PaletteEntry G(string title) => new(PaletteKind.Gallery, title, null, "g:" + title);

    private static readonly PaletteEntry[] All =
    [
        S("Good Morning", "gm"), S("Resize Images"), S("Settings Backup Notes"),
        C("New shortcut", "create", "add"), C("Settings", "options", "preferences"), C("Run History", "log", "past"), C("Recently deleted", "trash", "undo"),
        G("Resize photos for email"), G("Morning briefing"),
    ];

    [Fact]
    public void ExactAndPrefixBeatContains()
    {
        var r = PaletteSearch.Rank(All, "settings");
        Assert.Equal("Settings", r[0].Title);              // exact beats the longer prefix match
        Assert.Equal("Settings Backup Notes", r[1].Title);
    }

    [Fact]
    public void AnExactAliasWinsOverEverything()
    {
        var r = PaletteSearch.Rank(All, "gm");
        Assert.Equal("Good Morning", r[0].Title);
        Assert.Equal(PaletteKind.Shortcut, r[0].Kind);
    }

    [Fact]
    public void AShortcutOutranksAGalleryItemOnATie()
    {
        var r = PaletteSearch.Rank([G("Resize"), C("Resize"), S("Resize")], "resize");
        Assert.Equal(new[] { PaletteKind.Shortcut, PaletteKind.Command, PaletteKind.Gallery }, r.Select(e => e.Kind).ToArray());
    }

    [Fact]
    public void KeywordsFindACommandButRankBelowATitleHit()
    {
        var r = PaletteSearch.Rank(All, "trash");
        Assert.Equal("Recently deleted", Assert.Single(r).Title);
        var both = PaletteSearch.Rank([C("Other", "history"), S("History of Rome")], "history");
        Assert.Equal("History of Rome", both[0].Title);
    }

    [Fact]
    public void NoMatchGivesNothing_AndEmptyQueryListsCommandsFirstWithinTheLimit()
    {
        Assert.Empty(PaletteSearch.Rank(All, "zzzqqq"));
        var r = PaletteSearch.Rank(All, "  ", max: 5);
        Assert.Equal(5, r.Count);
        Assert.All(r.Take(4), e => Assert.Equal(PaletteKind.Command, e.Kind));
    }

    [Fact]
    public void MatchingIsCaseInsensitiveAndRespectsTheLimit()
    {
        Assert.Equal("Run History", PaletteSearch.Rank(All, "RUN HIST")[0].Title);
        Assert.Equal(2, PaletteSearch.Rank(All, "re", max: 2).Count);
    }
}
