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
            if (File.Exists(path)) return ResolveStrings(File.ReadAllText(path), Path.Combine(dir.FullName, "src", "AutomationPortal.App", "Strings.resx"));
        }
        return null;
    }

    // Swaps each {local:Str Key} for the resx text, so the checks below see what the page shows.
    private static string ResolveStrings(string xaml, string resxPath)
    {
        var resx = File.ReadAllText(resxPath);
        return Regex.Replace(xaml, @"\{local:Str (\w+)\}", m =>
        {
            var hit = Regex.Match(resx, $@"data name=""{m.Groups[1].Value}""[^>]*><value>(.*?)</value>", RegexOptions.Singleline);
            return hit.Success ? hit.Groups[1].Value : m.Value;
        });
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
    public void Every_appearance_setting_is_listed_and_the_colour_picker_shows_ten_distinct_colours()
    {
        var names = SettingsLayout.In(SettingsSection.Appearance).Select(i => i.Name).ToList();
        foreach (var key in new[] { "SettingsPage_AccentColour", "SettingsPage_WindowBackground", "SettingsPage_Corners", "SettingsPage_Density", "SettingsPage_TextSize", "SettingsPage_Motion", "SettingsPage_TileLook", "SettingsPage_ResetAppearance" })
            Assert.Contains(Strings.Get(key), names);
        Assert.Contains("Theme", names);
        Assert.Equal(SettingsSection.Appearance, SettingsLayout.Sections[0].Section);

        Assert.Equal(10, AutomationPortal.App.Infrastructure.Palette.PickerKeys.Count);
        Assert.Equal(10, AutomationPortal.App.Infrastructure.Palette.PickerKeys.Select(AutomationPortal.App.Infrastructure.Palette.CategoryResourceKey).Distinct().Count());
    }

    private static List<string> Tags(string xaml, string box)
    {
        var m = Regex.Match(xaml, $@"<ComboBox x:Name=""{box}""[^>]*>(.*?)</ComboBox>", RegexOptions.Singleline);
        Assert.True(m.Success, box);
        return Regex.Matches(m.Groups[1].Value, @"Tag=""([^""]+)""").Select(x => x.Groups[1].Value).ToList();
    }

    [Fact]
    public void Each_appearance_box_has_a_choice_for_every_setting_value_and_shows_it_after_load()
    {
        var xaml = Xaml();
        if (xaml is null) return;
        var accent = Tags(xaml, "AccentBox"); var material = Tags(xaml, "MaterialBox"); var corners = Tags(xaml, "CornerBox"); var density = Tags(xaml, "DensityBox");
        var motion = Tags(xaml, "MotionBox"); var tiles = Tags(xaml, "TileLookBox");
        Assert.Contains("Windows", accent);
        foreach (var c in AutomationPortal.Core.AppearanceMath.CategoryColors) Assert.Contains(c, accent);
        foreach (var v in Enum.GetValues<AutomationPortal.Core.MaterialSetting>()) Assert.Contains(v.ToString(), material);
        foreach (var v in Enum.GetValues<AutomationPortal.Core.CornerStyle>()) Assert.Contains(v.ToString(), corners);
        foreach (var v in Enum.GetValues<AutomationPortal.Core.Density>()) Assert.Contains(v.ToString(), density);
        foreach (var v in Enum.GetValues<AutomationPortal.Core.MotionSetting>()) Assert.Contains(v.ToString(), motion);
        foreach (var v in Enum.GetValues<AutomationPortal.Core.TileLook>()) Assert.Contains(v.ToString(), tiles);

        // Selecting by tag lands on the matching item, and an unknown tag falls back to the first.
        SharedSta.Run(() =>
        {
            var box = new System.Windows.Controls.ComboBox();
            foreach (var tag in accent) box.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = tag, Tag = tag });
            Pages.SettingsPage.Select(box, "Teal");
            Assert.Equal(accent.IndexOf("Teal"), box.SelectedIndex);
            Assert.Equal("Teal", (box.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag);
            Pages.SettingsPage.Select(box, "nonsense");
            Assert.Equal(0, box.SelectedIndex);
        });

        var s = new AutomationPortal.Core.AppSettings { AccentMode = AutomationPortal.Core.AccentMode.Preset, AccentPreset = "yellow", Material = AutomationPortal.Core.MaterialSetting.Acrylic, TextScale = 125 };
        var tags = Pages.SettingsPage.AppearanceTags(s);
        Assert.Equal("Amber", tags.Accent);
        Assert.Equal("Acrylic", tags.Material);
        Assert.Equal("125", tags.TextSize);
        Assert.Equal("Windows", Pages.SettingsPage.AppearanceTags(new AutomationPortal.Core.AppSettings()).Accent);
        Assert.Equal("Windows", Pages.SettingsPage.AppearanceTags(new AutomationPortal.Core.AppSettings { AccentMode = AutomationPortal.Core.AccentMode.Preset, AccentPreset = "chartreuse" }).Accent);
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
