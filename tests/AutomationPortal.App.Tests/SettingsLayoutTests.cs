using System.IO;
using System.Text.RegularExpressions;
using AutomationPortal.App.Logic;
using Xunit;

namespace AutomationPortal.App.Tests;

public class SettingsLayoutTests
{
    private static string? Xaml()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "src", "AutomationPortal.App", "Pages", "SettingsPage.xaml");
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        return null;
    }

    [Fact]
    public void Every_setting_is_listed_once_and_every_section_has_something()
    {
        Assert.Equal(SettingsLayout.Items.Count, SettingsLayout.Items.Select(i => i.Name).Distinct().Count());
        foreach (var section in Enum.GetValues<SettingsSection>())
            Assert.NotEmpty(SettingsLayout.In(section));
        Assert.Equal(Enum.GetValues<SettingsSection>().Length, SettingsLayout.Sections.Count);
        Assert.Equal(SettingsSection.Models, SettingsLayout.SectionOf("Model level: Fast, Smart or Best"));
        Assert.Equal(SettingsSection.Advanced, SettingsLayout.SectionOf("Show advanced actions"));
        Assert.Null(SettingsLayout.SectionOf("Nothing like this"));
    }

    [Fact]
    public void The_page_shows_each_setting_exactly_once_inside_its_section()
    {
        var xaml = Xaml();
        if (xaml is null) return;   // the page source isn't beside the test run

        // Every card on the page is in the list, and every listed card is on the page.
        var cards = Regex.Matches(xaml, @"Text=""([^""]+)"" Style=""\{StaticResource CardTitle\}""").Select(m => m.Groups[1].Value).ToList();
        var listedCards = SettingsLayout.Items.Where(i => !i.Name.Contains("Host") && i.Name != "VersionText").Select(i => i.Name).ToList();
        Assert.Equal(listedCards.OrderBy(x => x), cards.OrderBy(x => x));

        var starts = SettingsLayout.Sections.Select(s => xaml.IndexOf($"x:Name=\"{s.Anchor}\"", StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, starts);
        Assert.Equal(starts.OrderBy(x => x), starts);   // the sections appear in the listed order

        foreach (var item in SettingsLayout.Items)
        {
            var token = cards.Contains(item.Name) ? $"Text=\"{item.Name}\" Style=" : $"x:Name=\"{item.Name}\"";
            Assert.Single(Regex.Matches(xaml, Regex.Escape(token)));
            var at = xaml.IndexOf(token, StringComparison.Ordinal);
            var index = SettingsLayout.Sections.ToList().FindIndex(s => s.Section == item.Section);
            var end = index + 1 < starts.Count ? starts[index + 1] : xaml.Length;
            Assert.True(at > starts[index] && at < end, $"{item.Name} is not inside {item.Section}");
        }
    }
}
