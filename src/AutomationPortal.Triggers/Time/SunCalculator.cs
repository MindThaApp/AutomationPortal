namespace AutomationPortal.Triggers;

/// <summary>Sunrise and sunset in UTC; null when the sun doesn't rise or set that day (polar day / night).</summary>
public sealed record SunTimes(DateTimeOffset? Sunrise, DateTimeOffset? Sunset);

/// <summary>Sunrise and sunset with the NOAA solar-calculator algorithm (accurate to about a minute outside the polar circles).</summary>
public static class SunCalculator
{
    private const double Zenith = 90.833; // refraction + the sun's radius

    /// <param name="localDate">The calendar date at the location.</param>
    /// <param name="latitude">Degrees, north positive.</param>
    /// <param name="longitude">Degrees, east positive.</param>
    public static SunTimes Calculate(DateOnly localDate, double latitude, double longitude)
    {
        var midnightUtc = new DateTime(localDate.Year, localDate.Month, localDate.Day, 0, 0, 0, DateTimeKind.Utc);
        var jd = midnightUtc.ToOADate() + 2415018.5;
        return new SunTimes(EventUtc(midnightUtc, jd, latitude, longitude, sunrise: true), EventUtc(midnightUtc, jd, latitude, longitude, sunrise: false));
    }

    private static DateTimeOffset? EventUtc(DateTime midnightUtc, double jd, double lat, double lon, bool sunrise)
    {
        var minutes = EventMinutes(jd, lat, lon, sunrise);
        if (minutes is null) return null;
        // Second pass at the time just found makes the answer more accurate.
        var refined = EventMinutes(jd + minutes.Value / 1440.0, lat, lon, sunrise);
        if (refined is null) return null;
        return new DateTimeOffset(midnightUtc, TimeSpan.Zero).AddMinutes(refined.Value);
    }

    private static double? EventMinutes(double jd, double lat, double lon, bool sunrise)
    {
        var t = (jd - 2451545.0) / 36525.0;
        var eqTime = EquationOfTime(t);
        var decl = Declination(t);
        var latRad = Rad(lat);
        var cosHa = Math.Cos(Rad(Zenith)) / (Math.Cos(latRad) * Math.Cos(Rad(decl))) - Math.Tan(latRad) * Math.Tan(Rad(decl));
        if (cosHa is > 1 or < -1) return null;
        var ha = Deg(Math.Acos(cosHa));
        var delta = sunrise ? lon + ha : lon - ha;
        return 720 - 4 * delta - eqTime;
    }

    private static double Rad(double d) => d * Math.PI / 180;
    private static double Deg(double r) => r * 180 / Math.PI;

    private static double MeanLongitude(double t) => Mod(280.46646 + t * (36000.76983 + t * 0.0003032), 360);
    private static double MeanAnomaly(double t) => 357.52911 + t * (35999.05029 - 0.0001537 * t);
    private static double Eccentricity(double t) => 0.016708634 - t * (0.000042037 + 0.0000001267 * t);

    private static double Declination(double t)
    {
        var m = Rad(MeanAnomaly(t));
        var center = Math.Sin(m) * (1.914602 - t * (0.004817 + 0.000014 * t)) + Math.Sin(2 * m) * (0.019993 - 0.000101 * t) + Math.Sin(3 * m) * 0.000289;
        var trueLong = MeanLongitude(t) + center;
        var apparent = trueLong - 0.00569 - 0.00478 * Math.Sin(Rad(125.04 - 1934.136 * t));
        return Deg(Math.Asin(Math.Sin(Rad(Obliquity(t))) * Math.Sin(Rad(apparent))));
    }

    private static double Obliquity(double t)
    {
        var seconds = 21.448 - t * (46.815 + t * (0.00059 - t * 0.001813));
        var mean = 23 + (26 + seconds / 60) / 60;
        return mean + 0.00256 * Math.Cos(Rad(125.04 - 1934.136 * t));
    }

    private static double EquationOfTime(double t)
    {
        var e = Obliquity(t);
        var l0 = Rad(MeanLongitude(t));
        var ecc = Eccentricity(t);
        var m = Rad(MeanAnomaly(t));
        var y = Math.Tan(Rad(e) / 2);
        y *= y;
        var eq = y * Math.Sin(2 * l0) - 2 * ecc * Math.Sin(m) + 4 * ecc * y * Math.Sin(m) * Math.Cos(2 * l0)
                 - 0.5 * y * y * Math.Sin(4 * l0) - 1.25 * ecc * ecc * Math.Sin(2 * m);
        return Deg(eq) * 4;
    }

    private static double Mod(double x, double m) => ((x % m) + m) % m;

    /// <summary>The next sunrise or sunset (plus an offset) strictly after <paramref name="after"/>, in local time.</summary>
    public static DateTime? NextEvent(DateTime after, double latitude, double longitude, bool sunrise, TimeSpan offset, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var start = DateOnly.FromDateTime(after.Date.AddDays(-1));
        for (var i = 0; i < 400; i++)
        {
            var date = start.AddDays(i);
            var times = Calculate(date, latitude, longitude);
            var utc = sunrise ? times.Sunrise : times.Sunset;
            if (utc is null) continue;
            var local = TimeZoneInfo.ConvertTime(utc.Value, zone).DateTime + offset;
            if (local > after) return local;
        }
        return null;
    }
}
