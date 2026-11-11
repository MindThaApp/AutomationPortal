using System.Globalization;

namespace AutomationPortal.App.Pages;

/// <summary>Checks the "Where I am" boxes. Both coordinates must be given together and be in range.</summary>
internal static class HomeLocationInput
{
    public static (double? Latitude, double? Longitude, string? Problem) Check(string latText, string lonText)
    {
        latText = latText.Trim(); lonText = lonText.Trim();
        if (latText.Length == 0 && lonText.Length == 0) return (null, null, null);
        if (latText.Length == 0 || lonText.Length == 0) return (null, null, "Fill in both latitude and longitude, or leave both empty.");
        if (!TryParse(latText, out var lat)) return (null, null, "Latitude must be a number, like 51.5.");
        if (!TryParse(lonText, out var lon)) return (null, null, "Longitude must be a number, like -0.12.");
        if (lat is < -90 or > 90) return (null, null, "Latitude must be between -90 and 90.");
        if (lon is < -180 or > 180) return (null, null, "Longitude must be between -180 and 180.");
        return (lat, lon, null);
    }

    private static bool TryParse(string t, out double d) =>
        (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d) || double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out d))
        && double.IsFinite(d);
}
