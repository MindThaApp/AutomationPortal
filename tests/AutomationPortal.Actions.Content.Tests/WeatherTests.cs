using System.Globalization;
using System.Net;
using AutomationPortal.Actions.Content.Actions;
using AutomationPortal.Actions.Content.Utilities;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using static AutomationPortal.Actions.Content.Tests.Kit;

namespace AutomationPortal.Actions.Content.Tests;

public class WeatherTests
{
    private const string ForecastJson = """
        {"latitude":51.5,"longitude":-0.12,"utc_offset_seconds":3600,"timezone":"Europe/London",
         "current":{"time":"2026-10-02T09:00","temperature_2m":17.2,"relative_humidity_2m":82,"apparent_temperature":16.1,"is_day":1,"precipitation":0.2,"weather_code":61,"wind_speed_10m":11.5,"wind_direction_10m":225},
         "daily":{"time":["2026-10-02","2026-10-03","2026-10-04"],"weather_code":[61,3,0],"temperature_2m_max":[18.4,16.0,19.5],"temperature_2m_min":[9.1,8.0,7.2],
                  "sunrise":["2026-10-02T07:05","2026-10-03T07:07","2026-10-04T07:08"],"sunset":["2026-10-02T18:30","2026-10-03T18:28","2026-10-04T18:26"],
                  "precipitation_probability_max":[80,20,0],"precipitation_sum":[2.5,0,0]},
         "hourly":{"time":["2026-10-02T08:00","2026-10-02T09:00","2026-10-02T10:00","2026-10-02T11:00"],"temperature_2m":[16,17,18,18.5],"weather_code":[3,61,61,0],
                   "precipitation_probability":[10,60,70,0],"wind_speed_10m":[10,11,12,9]}}
        """;

    private const string ParisJson = """
        {"results":[
          {"id":1,"name":"Paris","latitude":48.85341,"longitude":2.3488,"country_code":"FR","timezone":"Europe/Paris","population":2138551,"country":"France","admin1":"Île-de-France"},
          {"id":2,"name":"Paris","latitude":33.66094,"longitude":-95.55551,"country_code":"US","timezone":"America/Chicago","population":24936,"country":"United States","admin1":"Texas"},
          {"id":3,"name":"Paris","latitude":43.2,"longitude":-80.38,"country_code":"CA","country":"Canada","admin1":"Ontario"}],
         "generationtime_ms":0.6}
        """;

    [Theory]
    [InlineData(0, "Clear sky")] [InlineData(1, "Mainly clear")] [InlineData(2, "Partly cloudy")] [InlineData(3, "Overcast")]
    [InlineData(45, "Fog")] [InlineData(51, "Light drizzle")] [InlineData(61, "Light rain")] [InlineData(63, "Rain")] [InlineData(65, "Heavy rain")]
    [InlineData(71, "Light snow")] [InlineData(75, "Heavy snow")] [InlineData(80, "Light rain showers")] [InlineData(95, "Thunderstorm")]
    [InlineData(99, "Thunderstorm with hail")] [InlineData(1234, "Unknown weather")]
    public void Weather_codes_in_plain_words(int code, string expected) => Assert.Equal(expected, WeatherService.Describe(code));

    [Fact]
    public void Every_documented_weather_code_has_a_description()
    {
        foreach (var code in new[] { 0, 1, 2, 3, 45, 48, 51, 53, 55, 56, 57, 61, 63, 65, 66, 67, 71, 73, 75, 77, 80, 81, 82, 85, 86, 95, 96, 99 })
            Assert.NotEqual("Unknown weather", WeatherService.Describe(code));
    }

    [Theory]
    [InlineData(0, "N")] [InlineData(44, "NE")] [InlineData(90, "E")] [InlineData(225, "SW")] [InlineData(350, "N")] [InlineData(360, "N")] [InlineData(-90, "W")]
    public void Compass_points(double degrees, string expected) => Assert.Equal(expected, WeatherService.Compass(degrees));

    [Fact]
    public void Units_follow_the_region_with_overrides()
    {
        Assert.Equal(new WeatherUnits(true, true), WeatherUnits.For("auto", "US"));
        Assert.Equal(new WeatherUnits(false, true), WeatherUnits.For("auto", "GB"));
        Assert.Equal(new WeatherUnits(false, false), WeatherUnits.For("auto", "DE"));
        Assert.Equal(new WeatherUnits(false, false), WeatherUnits.For("metric", "US"));
        Assert.Equal(new WeatherUnits(true, true), WeatherUnits.For("imperial", "DE"));
        Assert.Equal("°F", WeatherUnits.For("imperial").Temperature);
        Assert.Equal("km/h", WeatherUnits.For("metric").Wind);
        Assert.Equal("mph", WeatherUnits.For("imperial").Wind);
        Assert.NotNull(WeatherUnits.For("auto"));
    }

    [Fact]
    public void Builds_the_current_weather_dictionary()
    {
        var v = WeatherService.BuildCurrent(ForecastJson, null, "London", 51.5, -0.12, WeatherUnits.For("metric"));
        var d = Assert.IsType<DictionaryItem>(v[0]);
        Assert.Equal("17° and light rain in London", d.Get("Summary").AsText());
        Assert.Equal(17.2, d.Get("Temperature").AsNumber());
        Assert.Equal(16.1, d.Get("Feels Like").AsNumber());
        Assert.Equal("Light rain", d.Get("Condition").AsText());
        Assert.Equal(82, d.Get("Humidity").AsNumber());
        Assert.Equal(11.5, d.Get("Wind").AsNumber());
        Assert.Equal("SW", d.Get("Wind Direction").AsText());
        Assert.Equal(18.4, d.Get("High").AsNumber());
        Assert.Equal(9.1, d.Get("Low").AsNumber());
        Assert.Equal(80, d.Get("Rain Chance").AsNumber());
        Assert.Equal("°C", d.Get("Temperature Unit").AsText());
        Assert.Equal("km/h", d.Get("Wind Unit").AsText());

        var sunrise = d.Get("Sunrise").AsDate()!.Value;
        Assert.Equal(new DateTime(2026, 10, 2, 7, 5, 0), sunrise.DateTime);
        Assert.Equal(TimeSpan.FromHours(1), sunrise.Offset);
        Assert.Equal(new DateTime(2026, 10, 2, 18, 30, 0), d.Get("Sunset").AsDate()!.Value.DateTime);

        var loc = Assert.IsType<LocationItem>(d.Get("Location")[0]);
        Assert.Equal(51.5, loc.Latitude);
        Assert.Equal("London", loc.City);
        Assert.Equal(new[] { "Temperature", "Feels Like", "Condition", "Humidity", "Wind", "High", "Low", "Sunrise", "Sunset", "Location" },
            d.Keys.Where(k => new[] { "Temperature", "Feels Like", "Condition", "Humidity", "Wind", "High", "Low", "Sunrise", "Sunset", "Location" }.Contains(k)));
    }

    [Fact]
    public void Current_weather_for_bare_coordinates_names_the_place_sensibly()
    {
        var d = (DictionaryItem)WeatherService.BuildCurrent(ForecastJson, null, "your location", 51.5, -0.12, new WeatherUnits(true, true))[0];
        Assert.Equal("17° and light rain in your location", d.Get("Summary").AsText());
        Assert.Equal("°F", d.Get("Temperature Unit").AsText());
        Assert.Null(((LocationItem)d.Get("Location")[0]).Name);
    }

    [Fact]
    public void Builds_the_daily_forecast()
    {
        var v = WeatherService.BuildDaily(ForecastJson, 2, WeatherUnits.For("metric"));
        Assert.Equal(2, v.Count);
        var first = (DictionaryItem)v[0];
        var dayName = new DateTime(2026, 10, 2).ToString("dddd", CultureInfo.CurrentCulture);
        Assert.Equal(dayName, first.Get("Day").AsText());
        Assert.Equal(new DateTime(2026, 10, 2), first.Get("Date").AsDate()!.Value.DateTime);
        Assert.Equal("Light rain", first.Get("Condition").AsText());
        Assert.Equal(18.4, first.Get("High").AsNumber());
        Assert.Equal(9.1, first.Get("Low").AsNumber());
        Assert.Equal(80, first.Get("Rain Chance").AsNumber());
        Assert.Equal(2.5, first.Get("Rain").AsNumber());
        Assert.Equal($"{dayName}: Light rain, 18°/9°", first.Get("Summary").AsText());
        Assert.Equal("Overcast", ((DictionaryItem)v[1]).Get("Condition").AsText());
        Assert.Equal(3, WeatherService.BuildDaily(ForecastJson, 10, WeatherUnits.For("metric")).Count);
    }

    [Fact]
    public void Builds_the_hourly_forecast_from_the_current_hour()
    {
        var now = new DateTimeOffset(2026, 10, 2, 8, 30, 0, TimeSpan.Zero);   // 09:30 in the forecast's time zone (+01:00)
        var v = WeatherService.BuildHourly(ForecastJson, 10, WeatherUnits.For("metric"), now);
        Assert.Equal(3, v.Count);
        var first = (DictionaryItem)v[0];
        Assert.Equal("09:00", first.Get("Hour").AsText());
        Assert.Equal(17, first.Get("Temperature").AsNumber());
        Assert.Equal("Light rain", first.Get("Condition").AsText());
        Assert.Equal(60, first.Get("Rain Chance").AsNumber());
        Assert.Equal(11, first.Get("Wind").AsNumber());
        Assert.Equal("09:00: 17°, light rain", first.Get("Summary").AsText());
        Assert.Equal(2, WeatherService.BuildHourly(ForecastJson, 2, WeatherUnits.For("metric"), now).Count);
    }

    [Fact]
    public void Parses_places_and_names_them_without_repeating_parts()
    {
        var places = WeatherService.ParsePlaces(ParisJson);
        Assert.Equal(3, places.Count);
        Assert.Equal("Paris, Île-de-France, France", places[0].FullName);
        Assert.Equal("Paris, Texas, United States", places[1].FullName);
        var loc = places[1].ToLocation();
        Assert.Equal("Paris, Texas, United States", loc.DisplayText);
        Assert.Equal("Paris", loc.City);
        Assert.Equal("United States", loc.Country);
        Assert.Empty(WeatherService.ParsePlaces("{\"generationtime_ms\":0.5}"));
        Assert.Equal("Singapore", new GeoPlace("Singapore", "Singapore", "Singapore", "SG", 1, 1, null, 0).FullName);
        Assert.Equal("Monaco", new GeoPlace("Monaco", null, "Monaco", "MC", 1, 1, null, 0).FullName);
    }

    private static TestHost Host(out StubHandler handler, string forecast = ForecastJson, string geo = ParisJson)
    {
        handler = new StubHandler(req => req.RequestUri!.Host == "geocoding-api.open-meteo.com" ? Json(geo) : Json(forecast));
        var host = new TestHost();
        host.Services.Http = handler.Client();
        return host;
    }

    [Fact]
    public async Task Find_location_returns_location_items_and_narrows_by_the_words_after_a_comma()
    {
        using var host = Host(out var handler);
        var r = await Run(host, new FindLocationByNameAction(), null, ("name", "Paris"));
        var loc = Assert.IsType<LocationItem>(Assert.Single(r));
        Assert.Equal(48.85341, loc.Latitude);
        Assert.Contains("name=Paris", handler.Requests[0].RequestUri!.Query);

        var texas = (LocationItem)(await Run(host, new FindLocationByNameAction(), null, ("name", "Paris, Texas")))[0];
        Assert.Equal(-95.55551, texas.Longitude);
        Assert.Contains("name=Paris&", handler.Requests[1].RequestUri!.Query);
        var canada = (LocationItem)(await Run(host, new FindLocationByNameAction(), null, ("name", "Paris, CA")))[0];
        Assert.Equal("Canada", canada.Country);
        var france = (LocationItem)(await Run(host, new FindLocationByNameAction(), null, ("name", "paris, france")))[0];
        Assert.Equal("France", france.Country);

        var several = await Run(host, new FindLocationByNameAction(), null, ("name", "Paris"), ("limit", 2));
        Assert.Equal(2, several.Count);
        Assert.All(several, i => Assert.IsType<LocationItem>(i));
    }

    [Fact]
    public async Task Find_location_explains_when_nothing_matches_or_nothing_is_typed()
    {
        using var host = Host(out _, geo: "{\"generationtime_ms\":0.3}");
        var ex = await Fails(Run(host, new FindLocationByNameAction(), null, ("name", "Zzzzxxqqq")));
        Assert.Contains("Couldn't find a place called 'Zzzzxxqqq'", ex.Message);
        var blank = await Fails(Run(host, new FindLocationByNameAction(), null, ("name", "  ")));
        Assert.Contains("name of a place", blank.Message);
    }

    [Fact]
    public async Task Current_weather_action_geocodes_the_city_and_applies_units()
    {
        using var host = Host(out var handler);
        var r = await Run(host, new GetCurrentWeatherAction(), null, ("location", "Paris, Texas"), ("units", C("imperial")));
        var d = (DictionaryItem)r[0];
        Assert.Equal("17° and light rain in Paris", d.Get("Summary").AsText());
        Assert.Equal("°F", d.Get("Temperature Unit").AsText());
        var forecastCall = handler.Requests.Single(q => q.RequestUri!.Host == "api.open-meteo.com").RequestUri!.Query;
        Assert.Contains("temperature_unit=fahrenheit", forecastCall);
        Assert.Contains("wind_speed_unit=mph", forecastCall);
        Assert.Contains("latitude=33.6609", forecastCall);
        Assert.Contains("longitude=-95.5555", forecastCall);
        Assert.Contains("timezone=auto", forecastCall);

        var metric = await Run(host, new GetCurrentWeatherAction(), null, ("location", "Paris"), ("units", C("metric")));
        Assert.Contains("temperature_unit=celsius", handler.Requests.Last().RequestUri!.Query);
        Assert.Equal("°C", ((DictionaryItem)metric[0]).Get("Temperature Unit").AsText());
    }

    [Fact]
    public async Task Current_weather_takes_a_location_item_or_coordinates_without_geocoding()
    {
        using var host = Host(out var handler);
        var fromItem = await Run(host, new GetCurrentWeatherAction(), ContentValue.From(new LocationItem(51.5, -0.12, "London, England", "London", "United Kingdom")), ("units", C("metric")));
        Assert.Equal("17° and light rain in London", ((DictionaryItem)fromItem[0]).Get("Summary").AsText());
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.Host.StartsWith("geocoding"));

        var coords = await Run(host, new GetCurrentWeatherAction(), null, ("location", "51.5, -0.12"), ("units", C("metric")));
        Assert.Equal("17° and light rain in your location", ((DictionaryItem)coords[0]).Get("Summary").AsText());
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.Host.StartsWith("geocoding"));

        var bare = await Run(host, new GetCurrentWeatherAction(), ContentValue.From(new LocationItem(10, 20)), ("units", C("metric")));
        Assert.Contains("in your location", ((DictionaryItem)bare[0]).Get("Summary").AsText());
    }

    [Fact]
    public async Task Weather_without_a_place_or_with_a_broken_service_is_friendly()
    {
        using var host = Host(out _);
        var ex = await Fails(Run(host, new GetCurrentWeatherAction(), ContentValue.Empty));
        Assert.Contains("Choose a place", ex.Message);

        host.Services.Http = new StubHandler(req => req.RequestUri!.Host.StartsWith("geocoding") ? Json(ParisJson) : Resp(HttpStatusCode.TooManyRequests, "slow down")).Client();
        var busy = await Fails(Run(host, new GetCurrentWeatherAction(), null, ("location", "Paris")));
        Assert.Contains("Open-Meteo", busy.Message);
        Assert.Contains("429", busy.Message);

        host.Services.Http = new StubHandler(_ => throw new HttpRequestException("offline")).Client();
        var offline = await Fails(Run(host, new GetCurrentWeatherAction(), null, ("location", "Paris")));
        Assert.Contains("Couldn't reach", offline.Message);
    }

    [Fact]
    public async Task Forecast_action_daily_and_hourly()
    {
        using var host = Host(out var handler);
        var daily = await Run(host, new GetWeatherForecastAction(), null, ("location", "Paris"), ("days", 3), ("units", C("metric")));
        Assert.Equal(3, daily.Count);
        Assert.All(daily, i => Assert.IsType<DictionaryItem>(i));
        var q = handler.Requests.Last().RequestUri!.Query;
        Assert.Contains("forecast_days=3", q);
        Assert.DoesNotContain("hourly=", q);

        var hourly = await Run(host, new GetWeatherForecastAction(), null, ("location", "Paris"), ("period", C("hourly")), ("hours", 48), ("units", C("metric")));
        var hq = handler.Requests.Last().RequestUri!.Query;
        Assert.Contains("hourly=", hq);
        Assert.Contains("forecast_days=3", hq);
        Assert.All(hourly, i => Assert.IsType<DictionaryItem>(i));
    }
}
