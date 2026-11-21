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
    public void Compact_tile_look_makes_the_tile_smaller()
    {
        var t = AppearanceTokens.Tokens(new AppSettings { TileLook = TileLook.Compact });
        Assert.True((double)t["DsTileWidth"] < 182 && (double)t["DsTileHeight"] < 156);
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
