using System.Globalization;
using System.Text.Json;
using AutomationPortal.Core;

namespace AutomationPortal.Actions.Content.Utilities;

public sealed record GeoPlace(string Name, string? Region, string Country, string? CountryCode, double Latitude, double Longitude, string? Timezone, long Population)
{
    /// <summary>"London, England, United Kingdom"</summary>
    public string FullName => string.Join(", ", new[] { Name, Region, Country }
        .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).Distinct(StringComparer.OrdinalIgnoreCase));

    public LocationItem ToLocation() => new(Latitude, Longitude, FullName, Name, string.IsNullOrWhiteSpace(Country) ? null : Country);
}

public sealed record WeatherUnits(bool Fahrenheit, bool Mph)
{
    public string Temperature => Fahrenheit ? "°F" : "°C";
    public string Wind => Mph ? "mph" : "km/h";
    public string Rain => Fahrenheit ? "in" : "mm";

    /// <summary>"auto" follows the region (US-style °F + mph, UK °C + mph, otherwise °C + km/h); "metric" and "imperial" override.</summary>
    public static WeatherUnits For(string choice, string? regionCode = null)
    {
        switch (choice)
        {
            case "metric": return new WeatherUnits(false, false);
            case "imperial": return new WeatherUnits(true, true);
        }
        if (regionCode is null)
        {
            try { regionCode = RegionInfo.CurrentRegion.TwoLetterISORegionName; } catch (ArgumentException) { regionCode = ""; }
        }
        return regionCode.ToUpperInvariant() switch
        {
            "US" or "LR" or "MM" or "BS" or "BZ" or "KY" or "PW" => new WeatherUnits(true, true),
            "GB" => new WeatherUnits(false, true),
            _ => new WeatherUnits(false, false),
        };
    }
}

/// <summary>Open-Meteo geocoding and forecast (no key needed).</summary>
public static class WeatherService
{
    private const string GeocodingUrl = "https://geocoding-api.open-meteo.com/v1/search";
    private const string ForecastUrl = "https://api.open-meteo.com/v1/forecast";

    public static string Describe(int wmoCode) => wmoCode switch
    {
        0 => Strings.Get("Weather_Condition_ClearSky"), 1 => Strings.Get("Weather_Condition_MainlyClear"), 2 => Strings.Get("Weather_Condition_PartlyCloudy"), 3 => Strings.Get("Weather_Condition_Overcast"),
        45 => Strings.Get("Weather_Condition_Fog"), 48 => Strings.Get("Weather_Condition_FreezingFog"),
        51 => Strings.Get("Weather_Condition_LightDrizzle"), 53 => Strings.Get("Weather_Condition_Drizzle"), 55 => Strings.Get("Weather_Condition_HeavyDrizzle"), 56 => Strings.Get("Weather_Condition_LightFreezingDrizzle"), 57 => Strings.Get("Weather_Condition_FreezingDrizzle"),
        61 => Strings.Get("Weather_Condition_LightRain"), 63 => Strings.Get("Weather_Condition_Rain"), 65 => Strings.Get("Weather_Condition_HeavyRain"), 66 => Strings.Get("Weather_Condition_LightFreezingRain"), 67 => Strings.Get("Weather_Condition_FreezingRain"),
        71 => Strings.Get("Weather_Condition_LightSnow"), 73 => Strings.Get("Weather_Condition_Snow"), 75 => Strings.Get("Weather_Condition_HeavySnow"), 77 => Strings.Get("Weather_Condition_SnowGrains"),
        80 => Strings.Get("Weather_Condition_LightRainShowers"), 81 => Strings.Get("Weather_Condition_RainShowers"), 82 => Strings.Get("Weather_Condition_HeavyRainShowers"),
        85 => Strings.Get("Weather_Condition_LightSnowShowers"), 86 => Strings.Get("Weather_Condition_HeavySnowShowers"),
        95 => Strings.Get("Weather_Condition_Thunderstorm"), 96 => Strings.Get("Weather_Condition_ThunderstormWithLightHail"), 99 => Strings.Get("Weather_Condition_ThunderstormWithHail"),
        _ => Strings.Get("WeatherService_Message_UnknownWeather"),
    };

    public static string Compass(double degrees)
    {
        var names = new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        return names[(int)Math.Round(((degrees % 360) + 360) % 360 / 45.0) % 8];
    }

    /// <summary>Finds places by name. "London, UK" or "Paris, Texas" narrows the search by the words after the comma.</summary>
    public static async Task<IReadOnlyList<GeoPlace>> FindPlacesAsync(HttpClient http, string query, int count, CancellationToken ct)
    {
        query = query.Trim();
        if (query.Length == 0) throw new ActionException(Strings.Get("WeatherService_Error_TypeTheNameOf"));
        var parts = query.Split(',', 2, StringSplitOptions.TrimEntries);
        var name = parts[0];
        var hint = parts.Length > 1 ? parts[1] : "";
        var url = new Uri($"{GeocodingUrl}?name={Uri.EscapeDataString(name)}&count={(hint.Length > 0 ? 20 : Math.Clamp(count, 1, 20))}&language=en&format=json");
        var json = await GetJsonAsync(http, url, ct);
        var places = ParsePlaces(json);
        if (hint.Length > 0)
        {
            var narrowed = places.Where(p => Matches(p, hint)).ToList();
            if (narrowed.Count > 0) places = narrowed;
        }
        if (places.Count == 0)
            throw new ActionException(Strings.Format("WeatherService_Error_CouldnTFindA", query));
        return places.Take(count).ToList();
    }

    private static bool Matches(GeoPlace p, string hint) =>
        string.Equals(p.CountryCode, hint, StringComparison.OrdinalIgnoreCase)
        || (p.Country ?? "").Contains(hint, StringComparison.OrdinalIgnoreCase)
        || (p.Region ?? "").Contains(hint, StringComparison.OrdinalIgnoreCase)
        || (hint.Equals("UK", StringComparison.OrdinalIgnoreCase) && p.CountryCode == "GB");

    internal static List<GeoPlace> ParsePlaces(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<GeoPlace>();
        if (!doc.RootElement.TryGetProperty("results", out var results)) return list;
        foreach (var r in results.EnumerateArray())
        {
            string? S(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            list.Add(new GeoPlace(S("name") ?? "", S("admin1"), S("country") ?? "", S("country_code"),
                r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(), S("timezone"),
                r.TryGetProperty("population", out var pop) && pop.ValueKind == JsonValueKind.Number ? pop.GetInt64() : 0));
        }
        return list;
    }

    public static async Task<string> GetForecastJsonAsync(HttpClient http, double lat, double lon, WeatherUnits units, int days, bool hourly, CancellationToken ct)
    {
        var inv = CultureInfo.InvariantCulture;
        var url = $"{ForecastUrl}?latitude={lat.ToString("0.####", inv)}&longitude={lon.ToString("0.####", inv)}" +
                  "&current=temperature_2m,relative_humidity_2m,apparent_temperature,is_day,precipitation,weather_code,wind_speed_10m,wind_direction_10m" +
                  "&daily=weather_code,temperature_2m_max,temperature_2m_min,sunrise,sunset,precipitation_probability_max,precipitation_sum" +
                  (hourly ? "&hourly=temperature_2m,weather_code,precipitation_probability,wind_speed_10m" : "") +
                  $"&temperature_unit={(units.Fahrenheit ? "fahrenheit" : "celsius")}&wind_speed_unit={(units.Mph ? "mph" : "kmh")}" +
                  $"&precipitation_unit={(units.Fahrenheit ? "inch" : "mm")}&timezone=auto&forecast_days={Math.Clamp(days, 1, 16)}";
        return await GetJsonAsync(http, new Uri(url), ct);
    }

    private static async Task<string> GetJsonAsync(HttpClient http, Uri url, CancellationToken ct)
    {
        return await HttpHelpers.GuardAsync(url, ct, async () =>
        {
            using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new ActionException(Strings.Format("WeatherService_Error_TheWeatherServiceOpen", (int)response.StatusCode));
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        });
    }

    // ---- building results ---------------------------------------------------------------------

    private static DateTimeOffset Local(string time, int offsetSeconds) =>
        new(DateTime.SpecifyKind(DateTime.Parse(time, CultureInfo.InvariantCulture), DateTimeKind.Unspecified), TimeSpan.FromSeconds(offsetSeconds));

    private static double Num(JsonElement e, string name, int index = -1)
    {
        var v = index >= 0 ? e.GetProperty(name)[index] : e.GetProperty(name);
        return v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN;
    }

    private static ContentValue NumOrBlank(double d) => double.IsNaN(d) ? ContentValue.Text("") : ContentValue.Number(Math.Round(d, 1));

    private static string Lower(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];

    public static ContentValue BuildCurrent(string json, GeoPlace? place, string placeName, double lat, double lon, WeatherUnits units)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var offset = root.TryGetProperty("utc_offset_seconds", out var o) ? o.GetInt32() : 0;
        var cur = root.GetProperty("current");
        var daily = root.GetProperty("daily");
        var temp = Num(cur, "temperature_2m");
        var condition = Describe((int)Num(cur, "weather_code"));
        var summary = Strings.Format("WeatherService_Summary_Current", Math.Round(temp), Lower(condition), placeName);
        var yourLocation = Strings.Get("WeatherActions_Message_YourLocation");
        var location = place?.ToLocation() ?? new LocationItem(lat, lon, placeName == yourLocation ? null : placeName, placeName == yourLocation ? null : placeName);

        var entries = new List<KeyValuePair<string, ContentValue>>
        {
            new("Temperature", NumOrBlank(temp)),
            new("Feels Like", NumOrBlank(Num(cur, "apparent_temperature"))),
            new("Condition", ContentValue.Text(condition)),
            new("Humidity", NumOrBlank(Num(cur, "relative_humidity_2m"))),
            new("Wind", NumOrBlank(Num(cur, "wind_speed_10m"))),
            new("Wind Direction", ContentValue.Text(Compass(Num(cur, "wind_direction_10m")))),
            new("High", NumOrBlank(Num(daily, "temperature_2m_max", 0))),
            new("Low", NumOrBlank(Num(daily, "temperature_2m_min", 0))),
            new("Rain Chance", NumOrBlank(Num(daily, "precipitation_probability_max", 0))),
            new("Sunrise", ContentValue.Date(Local(daily.GetProperty("sunrise")[0].GetString()!, offset))),
            new("Sunset", ContentValue.Date(Local(daily.GetProperty("sunset")[0].GetString()!, offset))),
            new("Location", ContentValue.From(location)),
            new("Summary", ContentValue.Text(summary)),
            new("Temperature Unit", ContentValue.Text(units.Temperature)),
            new("Wind Unit", ContentValue.Text(units.Wind)),
        };
        return ContentValue.Dict(entries, summary);
    }

    public static ContentValue BuildDaily(string json, int days, WeatherUnits units)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var offset = root.TryGetProperty("utc_offset_seconds", out var o) ? o.GetInt32() : 0;
        var daily = root.GetProperty("daily");
        var times = daily.GetProperty("time");
        var items = new List<ContentItem>();
        for (var i = 0; i < times.GetArrayLength() && i < days; i++)
        {
            var date = Local(times[i].GetString()!, offset);
            var condition = Describe((int)Num(daily, "weather_code", i));
            var hi = Num(daily, "temperature_2m_max", i); var lo = Num(daily, "temperature_2m_min", i);
            var day = date.ToString("dddd", CultureInfo.CurrentCulture);
            var daySummary = Strings.Format("WeatherService_Summary_Day", day, condition, Math.Round(hi), Math.Round(lo));
            items.Add(new DictionaryItem(new List<KeyValuePair<string, ContentValue>>
            {
                new("Date", ContentValue.Date(date)),
                new("Day", ContentValue.Text(day)),
                new("Condition", ContentValue.Text(condition)),
                new("High", NumOrBlank(hi)),
                new("Low", NumOrBlank(lo)),
                new("Rain Chance", NumOrBlank(Num(daily, "precipitation_probability_max", i))),
                new("Rain", NumOrBlank(Num(daily, "precipitation_sum", i))),
                new("Sunrise", ContentValue.Date(Local(daily.GetProperty("sunrise")[i].GetString()!, offset))),
                new("Sunset", ContentValue.Date(Local(daily.GetProperty("sunset")[i].GetString()!, offset))),
                new("Summary", ContentValue.Text(daySummary)),
            }, daySummary));
        }
        return new ContentValue(items);
    }

    /// <summary>Hours from the current hour on, in the place's own time.</summary>
    public static ContentValue BuildHourly(string json, int hours, WeatherUnits units, DateTimeOffset nowUtc)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var offset = root.TryGetProperty("utc_offset_seconds", out var o) ? o.GetInt32() : 0;
        var hourly = root.GetProperty("hourly");
        var times = hourly.GetProperty("time");
        var nowLocal = nowUtc.ToOffset(TimeSpan.FromSeconds(offset));
        var startOfHour = new DateTimeOffset(nowLocal.Year, nowLocal.Month, nowLocal.Day, nowLocal.Hour, 0, 0, nowLocal.Offset);
        var items = new List<ContentItem>();
        for (var i = 0; i < times.GetArrayLength() && items.Count < hours; i++)
        {
            var t = Local(times[i].GetString()!, offset);
            if (t < startOfHour) continue;
            var condition = Describe((int)Num(hourly, "weather_code", i));
            var temp = Num(hourly, "temperature_2m", i);
            var hourSummary = Strings.Format("WeatherService_Summary_Hour", t, Math.Round(temp), Lower(condition));
            items.Add(new DictionaryItem(new List<KeyValuePair<string, ContentValue>>
            {
                new("Time", ContentValue.Date(t)),
                new("Hour", ContentValue.Text(t.ToString("HH:mm", CultureInfo.InvariantCulture))),
                new("Temperature", NumOrBlank(temp)),
                new("Condition", ContentValue.Text(condition)),
                new("Rain Chance", NumOrBlank(Num(hourly, "precipitation_probability", i))),
                new("Wind", NumOrBlank(Num(hourly, "wind_speed_10m", i))),
                new("Summary", ContentValue.Text(hourSummary)),
            }, hourSummary));
        }
        return new ContentValue(items);
    }
}
