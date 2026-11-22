using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.Core.Tests;

public sealed class AppearanceSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ap-appearance-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
    private AutomationPortalPaths Paths => AutomationPortalPaths.ForRoot(_root);

    private void WriteSettings(string json)
    {
        Directory.CreateDirectory(Paths.RoamingRoot);
        File.WriteAllText(Paths.SettingsFile, json);
    }

    [Fact]
    public void Defaults_equal_the_look_before_the_settings_existed()
    {
        var s = new AppSettings();
        Assert.Equal(ThemeSetting.System, s.Theme);
        Assert.Equal(AccentMode.Windows, s.AccentMode);
        Assert.Equal(MaterialSetting.Mica, s.Material);
        Assert.Equal(CornerStyle.Rounded, s.CornerStyle);
        Assert.Equal(Density.Standard, s.Density);
        Assert.Equal(100, s.TextScale);
        Assert.Equal(MotionSetting.FollowWindows, s.MotionMode);
        Assert.Equal(TileLook.Cards, s.TileLook);
    }

    [Fact]
    public void Appearance_round_trips_through_the_settings_file()
    {
        var store = new JsonSettingsStore(Paths);
        store.Update(s =>
        {
            s.Theme = ThemeSetting.Dark; s.AccentMode = AccentMode.Preset; s.AccentPreset = "Teal"; s.Material = MaterialSetting.Solid;
            s.CornerStyle = CornerStyle.ExtraRound; s.Density = Density.Comfortable; s.TextScale = 125; s.MotionMode = MotionSetting.Off; s.TileLook = TileLook.Compact;
        });
        var s2 = new JsonSettingsStore(Paths).Current;
        Assert.Equal(ThemeSetting.Dark, s2.Theme);
        Assert.Equal(AccentMode.Preset, s2.AccentMode);
        Assert.Equal("Teal", s2.AccentPreset);
        Assert.Equal(MaterialSetting.Solid, s2.Material);
        Assert.Equal(CornerStyle.ExtraRound, s2.CornerStyle);
        Assert.Equal(Density.Comfortable, s2.Density);
        Assert.Equal(125, s2.TextScale);
        Assert.Equal(MotionSetting.Off, s2.MotionMode);
        Assert.Equal(TileLook.Compact, s2.TileLook);
    }

    [Fact]
    public void An_old_file_without_appearance_keys_loads_with_defaults_and_keeps_its_other_settings()
    {
        WriteSettings("{ \"theme\": \"Light\", \"quickRunHotkey\": \"Ctrl+Alt+Q\", \"ntfyTopic\": \"mine\", \"tileStyle\": \"Neutral\" }");
        var s = new JsonSettingsStore(Paths).Current;
        Assert.Equal(ThemeSetting.Light, s.Theme);
        Assert.Equal("Ctrl+Alt+Q", s.QuickRunHotkey);
        Assert.Equal("mine", s.NtfyTopic);
        Assert.Equal(Density.Standard, s.Density);
        Assert.Equal(100, s.TextScale);
        Assert.Equal(MaterialSetting.Mica, s.Material);
    }

    [Fact]
    public void An_unknown_appearance_value_falls_back_to_the_default_without_losing_the_file()
    {
        WriteSettings("{ \"material\": \"Glass\", \"density\": 7, \"cornerStyle\": \"square\", \"tileLook\": 3.5, \"ntfyTopic\": \"mine\" }");
        var store = new JsonSettingsStore(Paths);
        Assert.Null(store.LoadError);
        Assert.Equal(MaterialSetting.Mica, store.Current.Material);
        Assert.Equal(Density.Standard, store.Current.Density);
        Assert.Equal(CornerStyle.Square, store.Current.CornerStyle);
        Assert.Equal(TileLook.Cards, store.Current.TileLook);
        Assert.Equal("mine", store.Current.NtfyTopic);
    }

    [Fact]
    public void Reset_appearance_restores_the_defaults_and_nothing_else()
    {
        var s = new AppSettings
        {
            Theme = ThemeSetting.Dark, AccentMode = AccentMode.Preset, AccentPreset = "Red", Material = MaterialSetting.Acrylic, CornerStyle = CornerStyle.Square,
            Density = Density.Compact, TextScale = 150, MotionMode = MotionSetting.Reduced, TileLook = TileLook.Compact, NtfyTopic = "mine", QuickRunHotkey = "Ctrl+Alt+Q",
        };
        s.ResetAppearance();
        var d = new AppSettings();
        Assert.Equal(d.Theme, s.Theme);
        Assert.Equal(d.AccentMode, s.AccentMode);
        Assert.Equal(d.AccentPreset, s.AccentPreset);
        Assert.Equal(d.Material, s.Material);
        Assert.Equal(d.CornerStyle, s.CornerStyle);
        Assert.Equal(d.Density, s.Density);
        Assert.Equal(d.TextScale, s.TextScale);
        Assert.Equal(d.MotionMode, s.MotionMode);
        Assert.Equal(d.TileLook, s.TileLook);
        Assert.Equal("mine", s.NtfyTopic);
        Assert.Equal("Ctrl+Alt+Q", s.QuickRunHotkey);
    }

    [Fact]
    public void Corner_density_and_text_scale_math()
    {
        Assert.Equal((4.0, 8.0), AppearanceMath.Radii(CornerStyle.Rounded));
        Assert.Equal((0.0, 2.0), AppearanceMath.Radii(CornerStyle.Square));
        Assert.Equal((8.0, 16.0), AppearanceMath.Radii(CornerStyle.ExtraRound));
        Assert.Equal(1.0, AppearanceMath.DensityFactor(Density.Standard));
        Assert.Equal(0.85, AppearanceMath.DensityFactor(Density.Compact));
        Assert.Equal(1.15, AppearanceMath.DensityFactor(Density.Comfortable));
        Assert.Equal(13.6, AppearanceMath.Scaled(16, Density.Compact));
        Assert.Equal(18.4, AppearanceMath.Scaled(16, Density.Comfortable));
        Assert.Equal(14.0, AppearanceMath.FontSize(14, 100));
        Assert.Equal(21.0, AppearanceMath.FontSize(14, 150));
        Assert.Equal(10.8, AppearanceMath.FontSize(12, 90));   // the 12 px floor scales with the step
        Assert.Equal(AppearanceMath.MaxTextScale, AppearanceMath.ClampTextScale(400));
        Assert.Equal(AppearanceMath.MinTextScale, AppearanceMath.ClampTextScale(10));
    }

    [Fact]
    public void Contrast_ratio_matches_known_values()
    {
        Assert.Equal(21.0, AppearanceMath.ContrastRatio((0, 0, 0), (255, 255, 255)), 1);
        Assert.Equal(1.0, AppearanceMath.ContrastRatio((120, 40, 200), (120, 40, 200)), 3);
    }

    [Fact]
    public void Ensure_contrast_darkens_a_light_accent_on_a_light_card_until_it_reaches_four_point_five()
    {
        var white = ((byte)255, (byte)255, (byte)255);
        var amber = ((byte)0xF0, (byte)0xA0, (byte)0x20);
        Assert.True(AppearanceMath.ContrastRatio(amber, white) < 4.5);
        var fixedColor = AppearanceMath.EnsureContrast(amber, white);
        Assert.True(AppearanceMath.ContrastRatio(fixedColor, white) >= 4.5);
        Assert.True(fixedColor.R < amber.Item1);
    }

    [Fact]
    public void Ensure_contrast_lightens_a_dark_accent_on_a_dark_card_and_leaves_passing_colours_alone()
    {
        var dark = ((byte)0x2B, (byte)0x2B, (byte)0x2B);
        var navy = ((byte)0x10, (byte)0x30, (byte)0x90);
        var lighter = AppearanceMath.EnsureContrast(navy, dark);
        Assert.True(AppearanceMath.ContrastRatio(lighter, dark) >= 4.5);
        var already = ((byte)0xFF, (byte)0xFF, (byte)0xFF);
        Assert.Equal(already, AppearanceMath.EnsureContrast(already, dark));
    }

    [Fact]
    public void Every_preset_accent_reaches_four_point_five_on_a_light_and_a_dark_card()
    {
        var tokens = new Dictionary<string, (byte, byte, byte)>
        {
            ["Blue"] = (0x0F, 0x6C, 0xBD), ["Indigo"] = (0x4F, 0x52, 0xD9), ["Violet"] = (0x7A, 0x3F, 0xD1), ["Magenta"] = (0xB8, 0x2D, 0x74), ["Red"] = (0xC9, 0x34, 0x34),
            ["Orange"] = (0xC2, 0x57, 0x0C), ["Amber"] = (0x8F, 0x62, 0x00), ["Green"] = (0x1A, 0x7F, 0x37), ["Teal"] = (0x0B, 0x7A, 0x75), ["Slate"] = (0x52, 0x60, 0x6D),
        };
        Assert.Equal(AppearanceMath.CategoryColors.OrderBy(x => x), tokens.Keys.OrderBy(x => x));
        foreach (var card in new[] { ((byte)255, (byte)255, (byte)255), ((byte)0x2B, (byte)0x2B, (byte)0x2B) })
            foreach (var (name, c) in tokens)
                Assert.True(AppearanceMath.ContrastRatio(AppearanceMath.EnsureContrast(c, card), card) >= 4.5, name);
    }
}
