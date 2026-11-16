using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using Xunit;

namespace AutomationPortal.App.Tests;

public class TileAppearanceTests
{
    [Fact]
    public void Neutral_is_the_default_tile_style() => Assert.Equal(TileStyleSetting.Neutral, new AppSettings().TileStyle);

    [Fact]
    public void Tile_style_survives_a_json_round_trip()
    {
        var json = AutomationPortalJson.Serialize(new AppSettings { TileStyle = TileStyleSetting.Gradients });
        Assert.Equal(TileStyleSetting.Gradients, AutomationPortalJson.Deserialize<AppSettings>(json)!.TileStyle);
    }

    [Theory]
    [InlineData("#FFD43B", true)]
    [InlineData("#F08C00", true)]
    [InlineData("#8CE99A", true)]
    [InlineData("#2B5DD8", false)]
    [InlineData("#343A40", false)]
    public void Dark_text_is_chosen_on_light_colours(string hex, bool dark) => Assert.Equal(dark, TileAppearance.UseDarkText(hex));

    [Fact]
    public void Every_palette_colour_gets_text_that_meets_WCAG_AA_on_the_name_area()
    {
        foreach (var key in Palette.Keys)
        {
            var (_, bottom) = Palette.Hex(key);
            var text = TileAppearance.TextFor(bottom);
            var ratio = TileAppearance.ContrastRatio(Lum(bottom), Lum(text));
            Assert.True(ratio >= 4.5, $"{key}: {ratio:F2}");   // AA: the better of white and near-black is used
        }
    }

    private static double Lum(string hex)
    {
        var h = hex.TrimStart('#');
        return TileAppearance.Luminance(Convert.ToByte(h[..2], 16), Convert.ToByte(h.Substring(2, 2), 16), Convert.ToByte(h.Substring(4, 2), 16));
    }
}
