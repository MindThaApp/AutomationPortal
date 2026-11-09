using System.Globalization;
using System.Text.Json;

namespace Flowmate.Triggers;

public enum WeatherCondition { Rain, Snow, Above, Below }

public sealed record HourlyForecast(DateTime Time, double Temperature, double Rain, double Snowfall);

public sealed record WeatherAlert(DateTime At, string Message);

/// <summary>Decides whether a forecast deserves an alert. Pure; the trigger feeds it Open-Meteo data.</summary>
public static class WeatherAlertEvaluator
{
    /// <summary>Millimetres of rain in one hour that count as "rain" (a drizzle starts around 0.2).</summary>
    public const double RainThreshold = 0.2;
    /// <summary>Centimetres of snow in one hour that count as "snow".</summary>
    public const double SnowThreshold = 0.1;

    public static WeatherAlert? Evaluate(IReadOnlyList<HourlyForecast> hours, WeatherCondition condition, double threshold, int hoursAhead, DateTime now, string unitSymbol = "°C")
    {
        var from = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
        var until = now.AddHours(Math.Max(1, hoursAhead));
        foreach (var h in hours.Where(h => h.Time >= from && h.Time <= until).OrderBy(h => h.Time))
        {
            var when = h.Time.Date == now.Date ? $"around {h.Time:t}" : $"{h.Time:dddd} around {h.Time:t}";
            switch (condition)
            {
                case WeatherCondition.Rain when h.Rain >= RainThreshold:
                    return new WeatherAlert(h.Time, $"Rain is expected {when}.");
                case WeatherCondition.Snow when h.Snowfall >= SnowThreshold:
                    return new WeatherAlert(h.Time, $"Snow is expected {when}.");
                case WeatherCondition.Above when h.Temperature > threshold:
                    return new WeatherAlert(h.Time, $"It will be warmer than {Fmt(threshold)}{unitSymbol} {when} ({Fmt(h.Temperature)}{unitSymbol}).");
                case WeatherCondition.Below when h.Temperature < threshold:
                    return new WeatherAlert(h.Time, $"It will be colder than {Fmt(threshold)}{unitSymbol} {when} ({Fmt(h.Temperature)}{unitSymbol}).");
            }
        }
        return null;
    }

    private static string Fmt(double d) => Math.Round(d, 1).ToString("0.#", CultureInfo.CurrentCulture);

    /// <summary>Reads the <c>hourly</c> block of an Open-Meteo forecast (times in the location's own clock, temperature_2m, rain, snowfall).</summary>
    public static IReadOnlyList<HourlyForecast> ParseHourly(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("hourly", out var hourly)) return Array.Empty<HourlyForecast>();
        var times = hourly.GetProperty("time").EnumerateArray().Select(e => e.GetString() ?? "").ToList();
        double[] Series(string name) => hourly.TryGetProperty(name, out var arr)
            ? arr.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : 0).ToArray()
            : new double[times.Count];
        var temp = Series("temperature_2m");
        var rain = Series("rain");
        var snow = Series("snowfall");
        var list = new List<HourlyForecast>();
        for (var i = 0; i < times.Count; i++)
        {
            if (!DateTime.TryParse(times[i], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) continue;
            list.Add(new HourlyForecast(t, i < temp.Length ? temp[i] : 0, i < rain.Length ? rain[i] : 0, i < snow.Length ? snow[i] : 0));
        }
        return list;
    }
}
