using AutomationPortal.App.Logic;
using Xunit;

namespace AutomationPortal.App.Tests;

public class WhatsNewTests
{
    private static readonly Version V105 = new(1, 0, 5, 0);
    private static WhatsNewPlan Plan(string? seen, Version cur, bool welcomeBefore = true, bool welcomeNow = false, bool bg = false, bool custom = false, bool content = true) =>
        WhatsNew.Plan(seen, cur, welcomeBefore, welcomeNow, bg, custom, _ => content);

    [Fact]
    public void Fresh_install_does_not_show_and_records_the_version()
    {
        var p = Plan(null, V105, welcomeBefore: false, welcomeNow: true);
        Assert.False(p.Show);
        Assert.Equal("1.0.5.0", p.RecordVersion);
    }

    [Fact]
    public void Same_version_does_not_show()
    {
        var p = Plan("1.0.5.0", V105);
        Assert.False(p.Show);
        Assert.Null(p.RecordVersion);
    }

    [Fact]
    public void Upgrade_shows_once()
    {
        var first = Plan("1.0.4.0", V105);
        Assert.True(first.Show);
        Assert.Equal("1.0.5.0", first.RecordVersion);
        Assert.False(Plan(first.RecordVersion, V105).Show);
    }

    [Fact]
    public void Existing_user_with_nothing_recorded_counts_as_an_upgrade()
    {
        Assert.True(Plan(null, V105, welcomeBefore: true).Show);
    }

    [Fact]
    public void Three_part_and_four_part_versions_compare_equal()
    {
        Assert.False(Plan("1.0.5", V105).Show);
    }

    [Fact]
    public void Downgrade_does_not_show()
    {
        Assert.False(Plan("1.0.6.0", V105).Show);
    }

    [Fact]
    public void Missing_content_does_not_show_but_is_recorded_so_it_logs_once()
    {
        var p = Plan("1.0.4.0", V105, content: false);
        Assert.False(p.Show);
        Assert.True(p.ContentMissing);
        Assert.Equal("1.0.5.0", p.RecordVersion);
    }

    [Fact]
    public void Background_start_and_custom_data_root_change_nothing()
    {
        Assert.Equal(new WhatsNewPlan(false, null, false), Plan("1.0.4.0", V105, bg: true));
        Assert.Equal(new WhatsNewPlan(false, null, false), Plan("1.0.4.0", V105, custom: true));
    }

    [Fact]
    public void Parse_reads_thanks_fixes_and_cards()
    {
        var c = WhatsNew.Parse("""{"thanks":"Hi","fixes":["a","b"],"cards":[{"title":"T","body":"B","image":"x.png","showMe":"gallery"},{"body":"no title"}]}""");
        Assert.NotNull(c);
        Assert.Equal("Hi", c!.Thanks);
        Assert.Equal(new[] { "a", "b" }, c.Fixes);
        var card = Assert.Single(c.Cards);
        Assert.Equal("gallery", card.ShowMe);
    }

    [Fact]
    public void Parse_without_fixes_gives_the_general_line()
    {
        var c = WhatsNew.Parse("""{"thanks":"Hi","cards":[]}""");
        Assert.Equal(new[] { WhatsNew.GeneralFixLine }, c!.Fixes);
    }

    [Fact]
    public void Parse_bad_json_gives_null()
    {
        Assert.Null(WhatsNew.Parse("{ nope"));
        Assert.Null(WhatsNew.Parse("[]"));
    }

    [Fact]
    public void The_1_0_5_release_file_is_embedded_and_parses()
    {
        Assert.True(WhatsNew.HasContent(V105));
        var c = WhatsNew.Load(V105);
        Assert.NotNull(c);
        Assert.Equal(2, c!.Fixes.Count);
        Assert.NotEmpty(c.Cards);
    }

    [Fact]
    public void No_file_for_an_unknown_version()
    {
        Assert.False(WhatsNew.HasContent(new Version(9, 9, 9, 0)));
        Assert.Null(WhatsNew.Load(new Version(9, 9, 9, 0)));
    }
}
