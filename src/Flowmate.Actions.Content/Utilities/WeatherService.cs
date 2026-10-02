using System.Globalization;
using System.Text.Json;
using Flowmate.Core;

namespace Flowmate.Actions.Content.Utilities;

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
        0 => "Clear sky", 1 => "Mainly clear", 2 => "Partly cloudy", 3 => "Overcast",
        45 => "Fog", 48 => "Freezing fog",
        51 => "Light drizzle", 53 => "Drizzle", 55 => "Heavy drizzle", 56 => "Light freezing drizzle", 57 => "Freezing drizzle",
        61 => "Light rain", 63 => "Rain", 65 => "Heavy rain", 66 => "Light freezing rain", 67 => "Freezing rain",
        71 => "Light snow", 73 => "Snow", 75 => "Heavy snow", 77 => "Snow grains",
        80 => "Light rain showers", 81 => "Rain showers", 82 => "Heavy rain showers",
        85 => "Light snow showers", 86 => "Heavy snow showers",
        95 => "Thunderstorm", 96 => "Thunderstorm with light hail", 99 => "Thunderstorm with hail",
        _ => "Unknown weather",
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
        if (query.Length == 0) throw new ActionException("Type the name of a place, for example London.");
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
            throw new ActionException($"Couldn't find a place called '{query}'. Check the spelling, or try the city name only.");
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
                throw new ActionException($"The weather service (Open-Meteo) isn't answering properly right now (error {(int)response.StatusCode}). Try again in a few minutes.");
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
        var summary = $"{Math.Round(temp):0}° and {Lower(condition)} in {placeName}";
        var location = place?.ToLocation() ?? new LocationItem(lat, lon, placeName == "your location" ? null : placeName, placeName == "your location" ? null : placeName);

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
            var daySummary = $"{day}: {condition}, {Math.Round(hi):0}°/{Math.Round(lo):0}°";
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
            var hourSummary = $"{t:HH:mm}: {Math.Round(temp):0}°, {Lower(condition)}";
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
