using System.Diagnostics;

namespace AutomationPortal.Actions.Windows.Location;

public sealed class GetCurrentLocationAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "loc.current", Title = Strings.Get("GetCurrentLocationAction_Title"), Category = ActionCategories.WeatherAndLocation, Icon = "LocationRipple24",
        Keywords = new[] { "gps", "where am i", "position", "coordinates", "city", "place" },
        Summary = Strings.Get("GetCurrentLocationAction_Summary"),
        Description = Strings.Get("GetCurrentLocationAction_Description"),
        OutputKind = ContentKind.Location, OutputName = Strings.Get("GetCurrentLocationAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Duration("timeout", Strings.Get("GetCurrentLocationAction_Param_Timeout_Label"), 15),
            ParameterDescriptor.Bool("getName", Strings.Get("GetCurrentLocationAction_Param_GetName_Label"), true),
            ParameterDescriptor.Bool("approximate", Strings.Get("GetCurrentLocationAction_Param_Approximate_Label"), true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var timeout = await ctx.GetDurationAsync("timeout", TimeSpan.FromSeconds(15));
        if (timeout < TimeSpan.FromSeconds(2)) timeout = TimeSpan.FromSeconds(2);
        var location = await LocationService.GetCurrentAsync(ctx.Services, timeout,
            await ctx.GetBoolAsync("approximate", true), await ctx.GetBoolAsync("getName", true), ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.From(location);
    }
}

public sealed class GetDistanceAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "loc.distance", Title = Strings.Get("GetDistanceAction_Title"), Category = ActionCategories.WeatherAndLocation, Icon = "Ruler24",
        Keywords = new[] { "how far", "kilometres", "miles", "between places", "distance" },
        Summary = Strings.Get("GetDistanceAction_Summary"),
        Description = Strings.Get("GetDistanceAction_Description"),
        OutputKind = ContentKind.Number, OutputName = Strings.Get("GetDistanceAction_OutputName"), NeedsInternet = false,
        Parameters = new[]
        {
            ParameterDescriptor.Content("from", Strings.Get("GetDistanceAction_Param_From_Label")),
            ParameterDescriptor.Content("to", Strings.Get("GetDistanceAction_Param_To_Label"), isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("unit", Strings.Get("GetDistanceAction_Param_Unit_Label"), "km", new Choice("km", Strings.Get("GetDistanceAction_Param_Unit_Choice_Km")), new Choice("miles", Strings.Get("GetDistanceAction_Param_Unit_Choice_Miles"))),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var ct = ctx.CancellationToken;
        var to = await ctx.GetContentAsync("to");
        if (to.IsEmpty) throw new ActionException(Strings.Get("GetDistanceAction_Error_Enter_Where_To_Measure_To"));
        var a = await LocationService.ResolveAsync(ctx, await ctx.GetContentAsync("from"), ct).ConfigureAwait(false);
        var b = await LocationService.ResolveAsync(ctx, to, ct).ConfigureAwait(false);
        var km = GeoMath.DistanceKm(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
        var inMiles = await ctx.GetChoiceAsync("unit", "km") == "miles";
        return ContentValue.Number(Math.Round(inMiles ? km / GeoMath.KmPerMile : km, 2));
    }
}

public sealed class ShowInMapsAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "loc.maps", Title = Strings.Get("ShowInMapsAction_Title"), Category = ActionCategories.WeatherAndLocation, Icon = "Map24",
        Keywords = new[] { "map", "directions", "navigate", "google maps", "bing maps", "route", "address" },
        Summary = Strings.Get("ShowInMapsAction_Summary"),
        Description = Strings.Get("ShowInMapsAction_Description"),
        OutputKind = ContentKind.Url, OutputName = Strings.Get("ShowInMapsAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("place", Strings.Get("ShowInMapsAction_Param_Place_Label"), isInput: true),
            ParameterDescriptor.ChoiceOf("service", Strings.Get("ShowInMapsAction_Param_Service_Label"), "bing", new Choice("bing", Strings.Get("ShowInMapsAction_Param_Service_Choice_Bing")), new Choice("google", Strings.Get("ShowInMapsAction_Param_Service_Choice_Google")), new Choice("osm", Strings.Get("ShowInMapsAction_Param_Service_Choice_Osm"))),
            ParameterDescriptor.ChoiceOf("mode", Strings.Get("ShowInMapsAction_Param_Mode_Label"), "show", new Choice("show", Strings.Get("ShowInMapsAction_Param_Mode_Choice_Show")), new Choice("directions", Strings.Get("ShowInMapsAction_Param_Mode_Choice_Directions"))),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var ct = ctx.CancellationToken;
        var place = await ctx.GetContentAsync("place");
        LocationItem? location = null;
        string? text = null;
        if (!place.IsEmpty && place[0] is LocationItem l) location = l;
        else if (!place.IsEmpty && !string.IsNullOrWhiteSpace(place.AsText()))
        {
            text = place.AsText().Trim();
            if (GeoMath.TryParseCoordinates(text) is { } c) { location = new LocationItem(c.Lat, c.Lon); text = null; }
        }
        else location = await LocationService.GetCurrentAsync(ctx.Services, TimeSpan.FromSeconds(15), true, false, ct).ConfigureAwait(false);

        var url = MapLinks.Build(await ctx.GetChoiceAsync("service", "bing"), await ctx.GetChoiceAsync("mode", "show"), location, text);
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new ActionException(Strings.Get("ShowInMapsAction_Error_Windows_Couldn_T_Open_Your_Web"), ex);
        }
        return ContentValue.Url(url);
    }
}
