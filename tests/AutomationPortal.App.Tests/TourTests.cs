using System.Reflection;
using System.Resources;
using AutomationPortal.App.Dialogs;
using AutomationPortal.App.Logic;
using Xunit;

namespace AutomationPortal.App.Tests;

public class TourTests
{
    private static IReadOnlyList<TourStop> Stops() => Tour.Load() ?? throw new InvalidOperationException("tour.json is not embedded or not valid");

    [Fact]
    public void Tour_json_parses_and_has_10_to_14_stops()
    {
        var stops = Stops();
        Assert.InRange(stops.Count, Tour.MinStops, Tour.MaxStops);
        Assert.Equal(stops.Count, stops.Select(s => s.Id).Distinct().Count());
        foreach (var s in stops)
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Title));
            Assert.False(string.IsNullOrWhiteSpace(s.Body));
            Assert.Equal("Assets/Tour/" + s.Id + ".png", s.Image);
        }
    }

    [Fact]
    public void Every_stop_image_is_a_compiled_resource()
    {
        var asm = typeof(Tour).Assembly;
        var name = asm.GetName().Name + ".g.resources";
        using var stream = asm.GetManifestResourceStream(name) ?? throw new InvalidOperationException(name + " missing");
        using var reader = new ResourceReader(stream);
        var keys = reader.Cast<System.Collections.DictionaryEntry>().Select(e => ((string)e.Key).ToLowerInvariant()).ToHashSet();
        foreach (var s in Stops()) Assert.Contains(s.Image.ToLowerInvariant(), keys);
    }

    [Fact]
    public void Every_show_me_target_is_known()
    {
        foreach (var s in Stops().Where(s => s.ShowMe is not null)) Assert.NotNull(WhatsNewWindow.NavFor(s.ShowMe!));
    }

    [Fact]
    public void Bad_json_gives_null_and_incomplete_stops_are_left_out()
    {
        Assert.Null(Tour.Parse("not json"));
        Assert.Null(Tour.Parse("[]"));
        var stops = Tour.Parse("{\"stops\":[{\"id\":\"a\",\"title\":\"A\",\"body\":\"b\",\"image\":\"Assets/Tour/a.png\"},{\"id\":\"x\"}]}");
        Assert.NotNull(stops);
        Assert.Single(stops!);
        Assert.Null(stops![0].ShowMe);
    }

    [Fact]
    public void First_stop_has_no_back_and_last_stop_ends_on_next()
    {
        var nav = new TourNav(3);
        Assert.Equal(0, nav.Current);
        Assert.False(nav.CanGoBack);
        Assert.False(nav.IsLast);
        Assert.False(nav.Back());
        Assert.True(nav.Next());
        Assert.True(nav.CanGoBack);
        Assert.True(nav.Next());
        Assert.True(nav.IsLast);
        Assert.False(nav.Ended);
        Assert.False(nav.Next());
        Assert.True(nav.Ended);
        Assert.Equal(2, nav.Current);
    }

    [Fact]
    public void Back_steps_down_and_skip_ends_from_any_stop()
    {
        var nav = new TourNav(4);
        nav.Next(); nav.Next();
        Assert.True(nav.Back());
        Assert.Equal(1, nav.Current);
        nav.Skip();
        Assert.True(nav.Ended);
        Assert.Equal(1, nav.Current);
    }

    [Fact]
    public void A_one_stop_tour_is_first_and_last()
    {
        var nav = new TourNav(1);
        Assert.True(nav.IsLast);
        Assert.False(nav.CanGoBack);
    }
}
