using System.Globalization;
using System.Text.RegularExpressions;
using AutomationPortal.Actions.Content.Utilities;
using AutomationPortal.Core;

namespace AutomationPortal.Actions.Content.Actions;

internal static class WeatherInput
{
    public static readonly Choice[] UnitChoices =
    {
        new("auto", Strings.Get("WeatherActions_Choice_Auto")), new("metric", Strings.Get("WeatherActions_Choice_Metric")), new("imperial", Strings.Get("WeatherActions_Choice_Imperial")),
    };

    private static readonly Regex CoordinatesRx = new(@"^\s*(-?\d{1,2}(?:\.\d+)?)\s*[,;]\s*(-?\d{1,3}(?:\.\d+)?)\s*$", RegexOptions.Compiled);

    /// <summary>Works out which place to use from a Location item, "lat, lon" text or a place name. Place is null for plain coordinates.</summary>
    public static async Task<(GeoPlace? Place, string Name, double Lat, double Lon)> ResolveAsync(ActionContext ctx, string key)
    {
        var content = await ctx.GetContentAsync(key);
        if (content.FirstItem is LocationItem loc)
        {
            var name = !string.IsNullOrWhiteSpace(loc.City) ? loc.City! : !string.IsNullOrWhiteSpace(loc.Name) ? loc.Name! : Strings.Get("WeatherActions_Message_YourLocation");
            return (null, name, loc.Latitude, loc.Longitude);
        }
        var text = content.AsText().Trim();
        if (text.Length == 0)
            throw new ActionException(Strings.Get("WeatherActions_Error_ChooseAPlaceFirst"));
        var m = CoordinatesRx.Match(text);
        if (m.Success)
            return (null, Strings.Get("WeatherActions_Message_YourLocation"), double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
        var place = (await WeatherService.FindPlacesAsync(ctx.Services.Http, text, 1, ctx.CancellationToken))[0];
        return (place, place.Name, place.Latitude, place.Longitude);
    }
}

public sealed class GetCurrentWeatherAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "weather.current", Title = Strings.Get("WeatherCurrent_Title"), Category = ActionCategories.WeatherAndLocation, Icon = "WeatherSunny24",
        Keywords = new[] { "temperature", "forecast", "rain", "wind", "humidity", "sunrise", "sunset", "conditions", "now" },
        Summary = Strings.Get("WeatherCurrent_Summary"),
        Description = Strings.Get("WeatherCurrent_Description"),
        InputKinds = new[] { ContentKind.Location, ContentKind.Text }, OutputKind = ContentKind.Dictionary, OutputName = Strings.Get("WeatherCurrent_OutputName"), NeedsInternet = true,
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "location", Label = Strings.Get("WeatherCurrent_Param_Location_Label"), Type = ParamType.Content, IsInput = true, Required = true, Placeholder = Strings.Get("WeatherCurrent_Param_Location_Placeholder"), Hint = Strings.Get("WeatherCurrent_Param_Location_Hint") },
            ParameterDescriptor.ChoiceOf("units", Strings.Get("WeatherCurrent_Param_Units_Label"), "auto", WeatherInput.UnitChoices),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var (place, name, lat, lon) = await WeatherInput.ResolveAsync(ctx, "location");
        var units = WeatherUnits.For(await ctx.GetChoiceAsync("units", "auto"));
        ctx.ReportProgress(Strings.Format("WeatherCurrent_Progress_GettingTheWeatherFor", name));
        var json = await WeatherService.GetForecastJsonAsync(ctx.Services.Http, lat, lon, units, 1, false, ctx.CancellationToken);
        return WeatherService.BuildCurrent(json, place, name, lat, lon, units);
    }
}

public sealed class GetWeatherForecastAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "weather.forecast", Title = Strings.Get("WeatherForecast_Title"), Category = ActionCategories.WeatherAndLocation, Icon = "WeatherPartlyCloudyDay24",
        Keywords = new[] { "week", "daily", "hourly", "tomorrow", "rain", "outlook", "upcoming" },
        Summary = Strings.Get("WeatherForecast_Summary"),
        Description = Strings.Get("WeatherForecast_Description"),
        InputKinds = new[] { ContentKind.Location, ContentKind.Text }, OutputKind = ContentKind.Dictionary, OutputName = Strings.Get("WeatherForecast_OutputName"), NeedsInternet = true,
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "location", Label = Strings.Get("WeatherForecast_Param_Location_Label"), Type = ParamType.Content, IsInput = true, Required = true, Placeholder = Strings.Get("WeatherForecast_Param_Location_Placeholder"), Hint = Strings.Get("WeatherForecast_Param_Location_Hint") },
            ParameterDescriptor.ChoiceOf("period", Strings.Get("WeatherForecast_Param_Period_Label"), "daily", new Choice("daily", Strings.Get("WeatherForecast_Choice_Daily")), new Choice("hourly", Strings.Get("WeatherForecast_Choice_Hourly"))),
            ParameterDescriptor.Number("days", Strings.Get("WeatherForecast_Param_Days_Label"), 7, 1, 16) with { VisibleWhen = new VisibleWhen("period", "daily") },
            ParameterDescriptor.Number("hours", Strings.Get("WeatherForecast_Param_Hours_Label"), 24, 1, 168) with { VisibleWhen = new VisibleWhen("period", "hourly") },
            ParameterDescriptor.ChoiceOf("units", Strings.Get("WeatherForecast_Param_Units_Label"), "auto", WeatherInput.UnitChoices),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var (_, name, lat, lon) = await WeatherInput.ResolveAsync(ctx, "location");
        var units = WeatherUnits.For(await ctx.GetChoiceAsync("units", "auto"));
        var hourly = await ctx.GetChoiceAsync("period", "daily") == "hourly";
        ctx.ReportProgress(Strings.Format("WeatherForecast_Progress_GettingTheForecastFor", name));
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
        Id = "loc.find", Title = Strings.Get("LocFind_Title"), Category = ActionCategories.WeatherAndLocation, Icon = "Location24",
        Keywords = new[] { "geocode", "city", "place", "coordinates", "latitude", "longitude", "address", "lookup" },
        Summary = Strings.Get("LocFind_Summary"),
        Description = Strings.Get("LocFind_Description"),
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Location, OutputName = Strings.Get("LocFind_OutputName"), NeedsInternet = true,
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "name", Label = Strings.Get("LocFind_Param_Name_Label"), Type = ParamType.Text, IsInput = true, Required = true, Placeholder = Strings.Get("LocFind_Param_Name_Placeholder") },
            ParameterDescriptor.Number("limit", Strings.Get("LocFind_Param_Limit_Label"), 1, 1, 10),
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
