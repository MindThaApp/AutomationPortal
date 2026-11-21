using System.Windows;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.Editor.Tests;

public class AppearanceTokensTests
{
    [Fact]
    public void Default_settings_give_the_standard_token_values()
    {
        var t = AppearanceTokens.Tokens(new AppSettings());
        Assert.Equal(new CornerRadius(4), t["DsRadiusControl"]);
        Assert.Equal(new CornerRadius(8), t["DsRadiusCard"]);
        Assert.Equal(new Thickness(16, 12, 16, 12), t["DsCardPadding"]);
        Assert.Equal(new Thickness(0, 0, 0, 6), t["DsCardGap"]);
        Assert.Equal(14.0, t["DsFontSizeBody"]);
        Assert.Equal(12.0, t["DsFontSizeCaption"]);
        Assert.Equal(182.0, t["DsTileWidth"]);
        Assert.Equal(156.0, t["DsTileHeight"]);
    }

    [Fact]
    public void Square_and_extra_round_rewrite_the_radius_tokens()
    {
        var sq = AppearanceTokens.Tokens(new AppSettings { CornerStyle = CornerStyle.Square });
        Assert.Equal(new CornerRadius(0), sq["DsRadiusControl"]);
        Assert.Equal(new CornerRadius(2), sq["DsRadiusCard"]);
        Assert.Equal(2.0, sq["DsRadiusCardValue"]);
        var xr = AppearanceTokens.Tokens(new AppSettings { CornerStyle = CornerStyle.ExtraRound });
        Assert.Equal(new CornerRadius(6), xr["DsRadiusControl"]);
        Assert.Equal(new CornerRadius(12), xr["DsRadiusCard"]);
    }

    [Fact]
    public void Density_scales_paddings_and_gaps_only()
    {
        var compact = AppearanceTokens.Tokens(new AppSettings { Density = Density.Compact });
        Assert.Equal(new Thickness(13.6, 10.2, 13.6, 10.2), compact["DsCardPadding"]);
        Assert.Equal(new Thickness(0, 0, 0, 5.1), compact["DsCardGap"]);
        Assert.Equal(14.0, compact["DsFontSizeBody"]);
        Assert.Equal(182.0, compact["DsTileWidth"]);
        var comfy = AppearanceTokens.Tokens(new AppSettings { Density = Density.Comfortable });
        Assert.Equal(new Thickness(18.4, 13.8, 18.4, 13.8), comfy["DsCardPadding"]);
        Assert.Equal(9.2, comfy["DsSpace8"]);
    }

    [Fact]
    public void Text_scale_multiplies_the_type_ramp_and_never_goes_below_the_scaled_floor()
    {
        var big = AppearanceTokens.Tokens(new AppSettings { TextScale = 150 });
        Assert.Equal(18.0, big["DsFontSizeCaption"]);
        Assert.Equal(42.0, big["DsFontSizeTitle"]);
        var small = AppearanceTokens.Tokens(new AppSettings { TextScale = 90 });
        foreach (var k in small.Keys.Where(k => k.StartsWith("DsFontSize")))
            Assert.True((double)small[k] >= 12 * 0.9 - 0.001, k);
        var clamped = AppearanceTokens.Tokens(new AppSettings { TextScale = 999 });
        Assert.Equal(18.0, clamped["DsFontSizeCaption"]);
    }

    [Fact]
    public void Compact_tile_look_is_shorter_and_puts_the_title_beside_the_icon()
    {
        var t = AppearanceTokens.Tokens(new AppSettings { TileLook = TileLook.Compact });
        Assert.True((double)t["DsTileHeight"] < 156);
        Assert.Equal(0, t["DsTileNameRow"]);
        Assert.Equal(1, t["DsTileNameColumn"]);
        var cards = AppearanceTokens.Tokens(new AppSettings());
        Assert.Equal(1, cards["DsTileNameRow"]);
        Assert.Equal(0, cards["DsTileNameColumn"]);
    }

    [Fact]
    public void Tile_size_and_title_height_grow_with_the_text_scale()
    {
        foreach (var look in new[] { TileLook.Cards, TileLook.Compact })
        {
            var one = AppearanceTokens.Tokens(new AppSettings { TileLook = look });
            var big = AppearanceTokens.Tokens(new AppSettings { TileLook = look, TextScale = 150 });
            Assert.Equal((double)one["DsTileWidth"] * 1.5, (double)big["DsTileWidth"], 0);
            Assert.Equal((double)one["DsTileHeight"] * 1.5, (double)big["DsTileHeight"], 0);
            Assert.True((double)big["DsTileTitleMaxHeight"] > (double)one["DsTileTitleMaxHeight"]);
            Assert.Equal((double)big["DsTileTitleLineHeight"] * 2, (double)big["DsTileTitleMaxHeight"], 3);   // two lines, then an ellipsis
        }
    }

    [Fact]
    public void Square_corners_also_reach_the_WPF_UI_radius_keys()
    {
        var t = AppearanceTokens.Tokens(new AppSettings { CornerStyle = CornerStyle.Square });
        Assert.Equal(new CornerRadius(0), t["ControlCornerRadius"]);
        Assert.Equal(new CornerRadius(2), t["OverlayCornerRadius"]);
    }

    [Fact]
    public void Old_palette_keys_and_category_names_map_to_a_category_and_anything_else_to_none()
    {
        Assert.Equal("Amber", Palette.CategoryNameOrNull("yellow"));
        Assert.Equal("Violet", Palette.CategoryNameOrNull("purple"));
        Assert.Equal("Magenta", Palette.CategoryNameOrNull("pink"));
        Assert.Equal("Teal", Palette.CategoryNameOrNull("mint"));
        Assert.Equal("Violet", Palette.CategoryNameOrNull("Violet"));
        Assert.Equal("Amber", Palette.CategoryNameOrNull("amber"));
        Assert.Null(Palette.CategoryNameOrNull("chartreuse"));
        Assert.Null(Palette.CategoryNameOrNull(null));
    }

    [Fact]
    public void Motion_setting_maps_onto_the_motion_mode()
    {
        Assert.Equal(MotionMode.FollowWindows, AppearanceTokens.ToMotion(MotionSetting.FollowWindows));
        Assert.Equal(MotionMode.Reduced, AppearanceTokens.ToMotion(MotionSetting.Reduced));
        Assert.Equal(MotionMode.Off, AppearanceTokens.ToMotion(MotionSetting.Off));
    }

    [Fact]
    public void The_picker_offers_ten_distinct_category_colours_and_old_keys_map_onto_them()
    {
        Assert.Equal(10, Palette.PickerKeys.Count);
        Assert.Equal(10, Palette.PickerKeys.Select(Palette.CategoryResourceKey).Distinct().Count());
        Assert.All(Palette.PickerKeys, k => Assert.True(Palette.IsValid(k)));
        foreach (var old in Palette.Keys) Assert.Contains(Palette.PickerKey(old), Palette.PickerKeys);
        Assert.Equal("teal", Palette.PickerKey("mint"));
        Assert.Equal("slate", Palette.PickerKey("graphite"));
        Assert.Equal("blue", Palette.PickerKey(null));
    }
}
