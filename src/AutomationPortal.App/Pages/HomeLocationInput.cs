using System.Globalization;

namespace AutomationPortal.App.Pages;

/// <summary>Checks the "Where I am" boxes. Both coordinates must be given together and be in range.</summary>
internal static class HomeLocationInput
{
    public static (double? Latitude, double? Longitude, string? Problem) Check(string latText, string lonText)
    {
        latText = latText.Trim(); lonText = lonText.Trim();
        if (latText.Length == 0 && lonText.Length == 0) return (null, null, null);
        if (latText.Length == 0 || lonText.Length == 0) return (null, null, Strings.Get("HomeLocationInput_FillInBothLatitude"));
        if (!TryParse(latText, out var lat)) return (null, null, Strings.Get("HomeLocationInput_LatitudeMustBeA"));
        if (!TryParse(lonText, out var lon)) return (null, null, Strings.Get("HomeLocationInput_LongitudeMustBeA"));
        if (lat is < -90 or > 90) return (null, null, Strings.Get("HomeLocationInput_LatitudeMustBeBetween"));
        if (lon is < -180 or > 180) return (null, null, Strings.Get("HomeLocationInput_LongitudeMustBeBetween"));
        return (lat, lon, null);
    }

    private static bool TryParse(string t, out double d) =>
        (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d) || double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out d))
        && double.IsFinite(d);
}
