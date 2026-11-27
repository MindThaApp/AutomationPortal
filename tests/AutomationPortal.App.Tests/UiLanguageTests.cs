using System.Globalization;
using System.IO;
using System.Xml.Linq;
using AutomationPortal.App.Controls;
using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.App.Tests;

public class UiLanguageTests
{
    [Fact]
    public void Lists_the_twenty_shipped_languages_once_each()
    {
        Assert.Equal(20, UiLanguage.All.Count);
        Assert.Equal(UiLanguage.All.Count, UiLanguage.All.Select(l => l.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var l in UiLanguage.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(l.NativeName));
            Assert.False(string.IsNullOrWhiteSpace(l.EnglishName));
            Assert.False(string.IsNullOrWhiteSpace(l.Welcome));
            _ = CultureInfo.GetCultureInfo(l.Code);   // a real culture
        }
    }

    [Fact]
    public void Every_language_has_a_flag_or_a_badge()
    {
        foreach (var l in UiLanguage.All)
        {
            if (l.FlagRegion is not null)
            {
                var image = FlagArt.For(l.FlagRegion);
                Assert.NotNull(image);
                Assert.True(image!.IsFrozen);
            }
            else Assert.False(string.IsNullOrWhiteSpace(l.Badge), l.Code + " has neither a flag nor a badge");
        }
        Assert.Equal("繁", UiLanguage.Find("zh-Hant")!.Badge);
    }

    [Fact]
    public void Every_shipped_satellite_is_listed()
    {
        var dir = SourceDir();
        if (dir is null) return;
        var satellites = Directory.GetFiles(dir, "Strings.*.resx").Select(f => Path.GetFileName(f)[8..^5]).ToList();
        Assert.NotEmpty(satellites);
        foreach (var code in satellites) Assert.NotNull(UiLanguage.Find(code));
    }

    [Theory]
    [InlineData("de-AT", "de")]
    [InlineData("fr-CA", "fr")]
    [InlineData("pt-PT", "pt-BR")]
    [InlineData("zh-TW", "zh-Hant")]
    [InlineData("zh-HK", "zh-Hant")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("en-GB", "en-US")]
    [InlineData("fi-FI", "en-US")]
    [InlineData("ar-EG", "ar")]
    public void Empty_follows_windows_when_the_app_has_it_else_english(string windows, string expected)
    {
        Assert.Equal(expected, UiLanguage.Resolve("", CultureInfo.GetCultureInfo(windows)).Code);
    }

    [Fact]
    public void A_listed_choice_wins_and_an_unknown_one_follows_windows()
    {
        Assert.Equal("ja", UiLanguage.Resolve("ja", CultureInfo.GetCultureInfo("de-DE")).Code);
        Assert.Equal("de", UiLanguage.Resolve("xx-nope", CultureInfo.GetCultureInfo("de-DE")).Code);
        Assert.Equal("en-US", UiLanguage.Resolve(null, CultureInfo.InvariantCulture).Code);
    }

    [Fact]
    public void Reads_only_the_language_from_settings_and_shrugs_at_bad_files()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ap-lang-" + Guid.NewGuid().ToString("N")));
        try
        {
            var file = Path.Combine(dir.FullName, "settings.json");
            Assert.Equal("", UiLanguage.ReadSetting(file));
            File.WriteAllText(file, AutomationPortalJson.Serialize(new AppSettings { Language = "ko" }, indented: true));
            Assert.Equal("ko", UiLanguage.ReadSetting(file));
            File.WriteAllText(file, "{ not json");
            Assert.Equal("", UiLanguage.ReadSetting(file));
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Settings_file_path_matches_the_store_for_a_data_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "ap-root");
        Assert.Equal(AutomationPortalPaths.ForRoot(root).SettingsFile, AutomationPortalPaths.SettingsFileFor(root));
    }

    internal static string? SourceDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "AutomationPortal.App");
            if (File.Exists(Path.Combine(candidate, "Strings.resx"))) return candidate;
        }
        return null;
    }
}

[CollectionDefinition("Ui culture", DisableParallelization = true)]
public sealed class UiCultureCollection { }

/// <summary>These change the process-wide UI culture, so they run on their own and put it back.</summary>
[Collection("Ui culture")]
public class UiLanguageApplyTests
{
    private static T WithCulture<T>(Func<T> body)
    {
        var ui = CultureInfo.CurrentUICulture;
        var format = CultureInfo.CurrentCulture;
        var defaultUi = CultureInfo.DefaultThreadCurrentUICulture;
        try { return body(); }
        finally
        {
            CultureInfo.DefaultThreadCurrentUICulture = defaultUi;
            CultureInfo.CurrentUICulture = ui;
            CultureInfo.CurrentCulture = format;
        }
    }

    [Fact]
    public void German_choice_makes_app_strings_german_and_leaves_formatting_alone()
    {
        var dir = UiLanguageTests.SourceDir();
        if (dir is null) return;
        const string key = "Welcome2_Next";
        var german = XDocument.Load(Path.Combine(dir, "Strings.de.resx")).Root!.Elements("data").First(d => (string?)d.Attribute("name") == key).Element("value")!.Value;
        var english = XDocument.Load(Path.Combine(dir, "Strings.resx")).Root!.Elements("data").First(d => (string?)d.Attribute("name") == key).Element("value")!.Value;
        Assert.NotEqual(english, german);

        WithCulture(() =>
        {
            var format = CultureInfo.CurrentCulture.Name;
            var applied = UiLanguage.Apply(new AppSettings { Language = "de" });
            Assert.Equal("de", applied.Code);
            Assert.Equal("de", CultureInfo.CurrentUICulture.Name);
            Assert.Equal("de", CultureInfo.DefaultThreadCurrentUICulture!.Name);
            Assert.Equal(german, Strings.Get(key));
            Assert.Equal(format, CultureInfo.CurrentCulture.Name);
            Assert.False(UiLanguage.NeedsRestart("de"));
            Assert.True(UiLanguage.NeedsRestart("ja"));
            return 0;
        });
    }

    [Fact]
    public void Arabic_sets_a_right_to_left_culture()
    {
        WithCulture(() =>
        {
            var applied = UiLanguage.Apply("ar");
            Assert.True(applied.IsRightToLeft);
            Assert.True(CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft);
            return 0;
        });
    }

    [Fact]
    public void Empty_choice_applies_a_supported_language()
    {
        WithCulture(() =>
        {
            var applied = UiLanguage.Apply("");
            Assert.NotNull(UiLanguage.Find(applied.Code));
            Assert.Equal(applied.Code, CultureInfo.CurrentUICulture.Name);
            return 0;
        });
    }
}
