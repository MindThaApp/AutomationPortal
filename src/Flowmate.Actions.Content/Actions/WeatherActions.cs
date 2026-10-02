using System.Globalization;
using System.Text.RegularExpressions;
using Flowmate.Actions.Content.Utilities;
using Flowmate.Core;

namespace Flowmate.Actions.Content.Actions;

internal static class WeatherInput
{
    public static readonly Choice[] UnitChoices =
    {
        new("auto", "Automatic (for your region)"), new("metric", "Metric (°C, km/h)"), new("imperial", "Imperial (°F, mph)"),
    };

    private static readonly Regex CoordinatesRx = new(@"^\s*(-?\d{1,2}(?:\.\d+)?)\s*[,;]\s*(-?\d{1,3}(?:\.\d+)?)\s*$", RegexOptions.Compiled);

    /// <summary>Works out which place to use from a Location item, "lat, lon" text or a place name. Place is null for plain coordinates.</summary>
    public static async Task<(GeoPlace? Place, string Name, double Lat, double Lon)> ResolveAsync(ActionContext ctx, string key)
    {
        var content = await ctx.GetContentAsync(key);
        if (content.FirstItem is LocationItem loc)
        {
            var name = !string.IsNullOrWhiteSpace(loc.City) ? loc.City! : !string.IsNullOrWhiteSpace(loc.Name) ? loc.Name! : "your location";
            return (null, name, loc.Latitude, loc.Longitude);
        }
        var text = content.AsText().Trim();
        if (text.Length == 0)
            throw new ActionException("Choose a place first: type a city name such as London, or use a location from an earlier action.");
        var m = CoordinatesRx.Match(text);
        if (m.Success)
            return (null, "your location", double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
        var place = (await WeatherService.FindPlacesAsync(ctx.Services.Http, text, 1, ctx.CancellationToken))[0];
        return (place, place.Name, place.Latitude, place.Longitude);
    }
}

public sealed class GetCurrentWeatherAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "weather.current", Title = "Get Current Weather", Category = ActionCategories.WeatherAndLocation, Icon = "WeatherSunny24",
        Keywords = new[] { "temperature", "forecast", "rain", "wind", "humidity", "sunrise", "sunset", "conditions", "now" },
        Summary = "Get the current weather for {location}",
        Description = "Gets today's weather for a place: temperature, feels-like, conditions, humidity, wind, high and low, sunrise and sunset. Use the Summary value for a ready-made sentence like \"17° and light rain in London\".",
        InputKinds = new[] { ContentKind.Location, ContentKind.Text }, OutputKind = ContentKind.Dictionary, OutputName = "Current Weather", NeedsInternet = true,
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "location", Label = "Place", Type = ParamType.Content, IsInput = true, Required = true, Placeholder = "London", Hint = "A city name, or a location from an earlier action." },
            ParameterDescriptor.ChoiceOf("units", "Units", "auto", WeatherInput.UnitChoices),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var (place, name, lat, lon) = await WeatherInput.ResolveAsync(ctx, "location");
        var units = WeatherUnits.For(await ctx.GetChoiceAsync("units", "auto"));
        ctx.ReportProgress($"Getting the weather for {name}");
        var json = await WeatherService.GetForecastJsonAsync(ctx.Services.Http, lat, lon, units, 1, false, ctx.CancellationToken);
        return WeatherService.BuildCurrent(json, place, name, lat, lon, units);
    }
}

public sealed class GetWeatherForecastAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "weather.forecast", Title = "Get Weather Forecast", Category = ActionCategories.WeatherAndLocation, Icon = "WeatherPartlyCloudyDay24",
        Keywords = new[] { "week", "daily", "hourly", "tomorrow", "rain", "outlook", "upcoming" },
        Summary = "Get the {period} weather forecast for {location}",
        Description = "Gets a day-by-day or hour-by-hour forecast. You get a list: each day has the date, conditions, high, low and chance of rain; each hour has the temperature, conditions and wind.",
        InputKinds = new[] { ContentKind.Location, ContentKind.Text }, OutputKind = ContentKind.Dictionary, OutputName = "Forecast", NeedsInternet = true,
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "location", Label = "Place", Type = ParamType.Content, IsInput = true, Required = true, Placeholder = "London", Hint = "A city name, or a location from an earlier action." },
            ParameterDescriptor.ChoiceOf("period", "Forecast", "daily", new Choice("daily", "daily"), new Choice("hourly", "hourly")),
            ParameterDescriptor.Number("days", "Days", 7, 1, 16) with { VisibleWhen = new VisibleWhen("period", "daily") },
            ParameterDescriptor.Number("hours", "Hours", 24, 1, 168) with { VisibleWhen = new VisibleWhen("period", "hourly") },
            ParameterDescriptor.ChoiceOf("units", "Units", "auto", WeatherInput.UnitChoices),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var (_, name, lat, lon) = await WeatherInput.ResolveAsync(ctx, "location");
        var units = WeatherUnits.For(await ctx.GetChoiceAsync("units", "auto"));
        var hourly = await ctx.GetChoiceAsync("period", "daily") == "hourly";
        ctx.ReportProgress($"Getting the forecast for {name}");
        if (hourly)
        {
            var hours = (int)Math.Clamp(await ctx.GetNumberAsync("hours", 24), 1, 168);
            var json = await WeatherService.GetForecastJsonAsync(ctx.Services.Http, lat, lon, units, (hours + 23) / 24 + 1, true, ctx.CancellationToken);
            return WeatherService.BuildHourly(json, hours, units, DateTimeOffset.UtcNow);
        }
        var days = (int)Math.Clamp(await ctx.GetNumberAsync("days", 7), 1, 16);
        var dailyJson = await WeatherService.GetForecastJsonAsync(ctx.Services.Http, lat, lon, units, days, false, ctx.CancellationToken);
        return WeatherService.BuildDaily(dailyJson, days, units);
    }
}

public sealed class FindLocationByNameAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "loc.find", Title = "Find Location by Name", Category = ActionCategories.WeatherAndLocation, Icon = "Location24",
        Keywords = new[] { "geocode", "city", "place", "coordinates", "latitude", "longitude", "address", "lookup" },
        Summary = "Find the location of {name}",
        Description = "Looks up a city or place by name and gives you its location (name, country, latitude and longitude). Add a country after a comma to narrow it down, like \"Paris, France\".",
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Location, OutputName = "Location", NeedsInternet = true,
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "name", Label = "Place name", Type = ParamType.Text, IsInput = true, Required = true, Placeholder = "London" },
            ParameterDescriptor.Number("limit", "How many matches", 1, 1, 10),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var name = (await ctx.GetTextAsync("name")).Trim();
        var limit = (int)Math.Clamp(await ctx.GetNumberAsync("limit", 1), 1, 10);
        var places = await WeatherService.FindPlacesAsync(ctx.Services.Http, name, limit, ctx.CancellationToken);
        return new ContentValue(places.Select(p => (ContentItem)p.ToLocation()));
    }
}
