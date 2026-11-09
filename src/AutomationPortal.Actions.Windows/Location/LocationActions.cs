using System.Diagnostics;

namespace AutomationPortal.Actions.Windows.Location;

public sealed class GetCurrentLocationAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "loc.current", Title = "Get Current Location", Category = ActionCategories.WeatherAndLocation, Icon = "LocationRipple24",
        Keywords = new[] { "gps", "where am i", "position", "coordinates", "city", "place" },
        Summary = "Get my current location",
        Description = "Finds where you are, with the place name and city. It uses Windows location when that's turned on, and can fall back to an approximate place from your internet connection.",
        OutputKind = ContentKind.Location, OutputName = "Current Location",
        Parameters = new[]
        {
            ParameterDescriptor.Duration("timeout", "Give up after", 15),
            ParameterDescriptor.Bool("getName", "Look up the place name (uses the internet)", true),
            ParameterDescriptor.Bool("approximate", "Use an approximate place if Windows can't tell", true),
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
        Id = "loc.distance", Title = "Get Distance", Category = ActionCategories.WeatherAndLocation, Icon = "Ruler24",
        Keywords = new[] { "how far", "kilometres", "miles", "between places", "distance" },
        Summary = "Get the distance from {from} to {to} in {unit}",
        Description = "Works out the straight-line distance between two places. Each place can be a location from an earlier action, coordinates like 48.8584, 2.2945, or an address. Leave \"from\" empty to measure from where you are now.",
        OutputKind = ContentKind.Number, OutputName = "Distance", NeedsInternet = false,
        Parameters = new[]
        {
            ParameterDescriptor.Content("from", "From (empty = where I am)"),
            ParameterDescriptor.Content("to", "To", isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("unit", "Unit", "km", new Choice("km", "kilometers"), new Choice("miles", "miles")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var ct = ctx.CancellationToken;
        var to = await ctx.GetContentAsync("to");
        if (to.IsEmpty) throw new ActionException("Enter where to measure to: a location, coordinates or an address.");
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
        Id = "loc.maps", Title = "Show in Maps", Category = ActionCategories.WeatherAndLocation, Icon = "Map24",
        Keywords = new[] { "map", "directions", "navigate", "google maps", "bing maps", "route", "address" },
        Summary = "Show {place} in {service}",
        Description = "Opens a place (a location, coordinates or an address) in Bing Maps, Google Maps or OpenStreetMap in your web browser, or shows directions to it. Leave the place empty to use where you are now.",
        OutputKind = ContentKind.Url, OutputName = "Maps Link",
        Parameters = new[]
        {
            ParameterDescriptor.Content("place", "Place", isInput: true),
            ParameterDescriptor.ChoiceOf("service", "Maps app", "bing", new Choice("bing", "Bing Maps"), new Choice("google", "Google Maps"), new Choice("osm", "OpenStreetMap")),
            ParameterDescriptor.ChoiceOf("mode", "Show", "show", new Choice("show", "the place"), new Choice("directions", "directions to it")),
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
            throw new ActionException("Windows couldn't open your web browser. Set a default browser in Settings > Apps > Default apps.", ex);
        }
        return ContentValue.Url(url);
    }
}
