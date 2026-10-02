using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Windows.Devices.Geolocation;

namespace Flowmate.Actions.Windows.Location;

public static class GeoMath
{
    private const double EarthRadiusKm = 6371.0088;

    /// <summary>Straight-line (great-circle) distance in kilometres.</summary>
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        static double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    public const double KmPerMile = 1.609344;

    /// <summary>Reads "48.8584, 2.2945" (also "48.8584 N, 2.2945 E" is not supported). Null when the text isn't two numbers.</summary>
    public static (double Lat, double Lon)? TryParseCoordinates(string text)
    {
        var m = Regex.Match(text.Trim(), @"^\(?\s*(-?\d+(?:[.,]\d+)?)\s*[,;\s]\s*(-?\d+(?:[.,]\d+)?)\s*\)?$");
        if (!m.Success) return null;
        if (!double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) return null;
        if (!double.TryParse(m.Groups[2].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) return null;
        return Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180 ? (lat, lon) : null;
    }
}

/// <summary>Turns the JSON that web services give back into <see cref="LocationItem"/>s.</summary>
public static class LocationParsers
{
    /// <summary>Nominatim "reverse" or one "search" result.</summary>
    public static LocationItem ParsePlace(JsonElement place, double lat, double lon)
    {
        string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        place.TryGetProperty("address", out var address);
        var city = Str(address, "city") ?? Str(address, "town") ?? Str(address, "village") ?? Str(address, "municipality") ?? Str(address, "county");
        var country = Str(address, "country");
        var road = Str(address, "road");
        var house = Str(address, "house_number");
        string? name = null;
        if (!string.IsNullOrWhiteSpace(road))
            name = string.Join(", ", new[] { string.IsNullOrWhiteSpace(house) ? road : $"{house} {road}", city }.Where(s => !string.IsNullOrWhiteSpace(s)));
        else if (!string.IsNullOrWhiteSpace(Str(place, "name"))) name = Str(place, "name");
        else if (Str(place, "display_name") is { } display) name = string.Join(", ", display.Split(',').Take(2).Select(p => p.Trim()));
        return new LocationItem(lat, lon, name, city, country);
    }

    public static LocationItem? ParseReverse(string json, double lat, double lon)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.Object && !doc.RootElement.TryGetProperty("error", out _)
            ? ParsePlace(doc.RootElement, lat, lon) : null;
    }

    public static LocationItem? ParseSearch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0) return null;
        var first = doc.RootElement[0];
        double Num(string name) => double.Parse(first.GetProperty(name).GetString() ?? "0", CultureInfo.InvariantCulture);
        return ParsePlace(first, Num("lat"), Num("lon"));
    }

    /// <summary>Approximate position from an "IP address to location" service (ipwho.is or ipapi.co).</summary>
    public static LocationItem? ParseIpLocation(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False) return null;
        if (!root.TryGetProperty("latitude", out var la) || !root.TryGetProperty("longitude", out var lo)) return null;
        if (la.ValueKind != JsonValueKind.Number || lo.ValueKind != JsonValueKind.Number) return null;
        string? Str(string n) => root.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var city = Str("city");
        var country = Str("country") ?? Str("country_name");
        return new LocationItem(la.GetDouble(), lo.GetDouble(), null, city, country);
    }
}

public static class MapLinks
{
    public static string Build(string service, string mode, LocationItem? location, string? text)
    {
        var inv = CultureInfo.InvariantCulture;
        string Coord(LocationItem l) => $"{l.Latitude.ToString("0.#####", inv)},{l.Longitude.ToString("0.#####", inv)}";
        var query = location is not null ? Coord(location) : text ?? "";
        var esc = Uri.EscapeDataString(query);
        var directions = mode == "directions";
        switch (service)
        {
            case "google":
                return directions ? $"https://www.google.com/maps/dir/?api=1&destination={esc}" : $"https://www.google.com/maps/search/?api=1&query={esc}";
            case "osm":
                if (location is not null)
                    return directions
                        ? $"https://www.openstreetmap.org/directions?to={Uri.EscapeDataString(Coord(location))}"
                        : $"https://www.openstreetmap.org/?mlat={location.Latitude.ToString("0.#####", inv)}&mlon={location.Longitude.ToString("0.#####", inv)}#map=16/{location.Latitude.ToString("0.#####", inv)}/{location.Longitude.ToString("0.#####", inv)}";
                return $"https://www.openstreetmap.org/search?query={esc}";
            default:
                if (location is not null)
                {
                    var lat = location.Latitude.ToString("0.#####", inv);
                    var lon = location.Longitude.ToString("0.#####", inv);
                    return directions ? $"https://www.bing.com/maps?rtp=~pos.{lat}_{lon}" : $"https://www.bing.com/maps?cp={lat}~{lon}&lvl=16&sp=point.{lat}_{lon}_";
                }
                return directions ? $"https://www.bing.com/maps?rtp=~adr.{esc}" : $"https://www.bing.com/maps?q={esc}";
        }
    }
}

internal static class LocationService
{
    private const string NominatimBase = "https://nominatim.openstreetmap.org";

    public static async Task<LocationItem> GetCurrentAsync(FlowmateServices services, TimeSpan timeout, bool allowApproximate, bool lookupName, CancellationToken ct)
    {
        LocationItem? here = null;
        string? problem = null;
        try { here = await FromWindowsAsync(timeout, ct).ConfigureAwait(false); }
        catch (UnauthorizedAccessException) { problem = "location is turned off for desktop apps"; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { problem = "Windows didn't find your location in time"; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { problem = "Windows couldn't work out your location"; }

        if (here is null && allowApproximate)
        {
            try { here = await FromInternetAddressAsync(services.Http, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested) { }
        }
        if (here is null)
            throw new ActionException($"Couldn't find your location: {problem ?? "no location service answered"}. Turn on Settings > Privacy & security > Location (including \"Let desktop apps access your location\"), or allow the approximate location from your internet connection.");

        if (lookupName && string.IsNullOrWhiteSpace(here.Name))
        {
            var named = await TryReverseAsync(services.Http, here.Latitude, here.Longitude, ct).ConfigureAwait(false);
            if (named is not null) here = named with { City = named.City ?? here.City, Country = named.Country ?? here.Country };
        }
        return here;
    }

    private static async Task<LocationItem?> FromWindowsAsync(TimeSpan timeout, CancellationToken ct)
    {
        var access = await Geolocator.RequestAccessAsync().AsTask(ct).ConfigureAwait(false);
        if (access == GeolocationAccessStatus.Denied) throw new UnauthorizedAccessException();
        var geolocator = new Geolocator { DesiredAccuracyInMeters = 100 };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var position = await geolocator.GetGeopositionAsync(TimeSpan.FromMinutes(2), timeout).AsTask(cts.Token).ConfigureAwait(false);
        var p = position.Coordinate.Point.Position;
        return new LocationItem(p.Latitude, p.Longitude);
    }

    private static async Task<LocationItem?> FromInternetAddressAsync(HttpClient http, CancellationToken ct)
    {
        foreach (var url in new[] { "https://ipwho.is/", "https://ipapi.co/json/" })
        {
            try
            {
                using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) continue;
                var found = LocationParsers.ParseIpLocation(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                if (found is not null) return found;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException) { }
        }
        return null;
    }

    public static async Task<LocationItem?> TryReverseAsync(HttpClient http, double lat, double lon, CancellationToken ct)
    {
        try
        {
            var url = $"{NominatimBase}/reverse?format=jsonv2&zoom=16&addressdetails=1&lat={lat.ToString(CultureInfo.InvariantCulture)}&lon={lon.ToString(CultureInfo.InvariantCulture)}";
            using var request = NominatimRequest(url);
            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            return LocationParsers.ParseReverse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), lat, lon);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested) { return null; }
    }

    public static async Task<LocationItem?> GeocodeAsync(HttpClient http, string query, CancellationToken ct)
    {
        try
        {
            var url = $"{NominatimBase}/search?format=jsonv2&limit=1&addressdetails=1&q={Uri.EscapeDataString(query)}";
            using var request = NominatimRequest(url);
            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            return LocationParsers.ParseSearch(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested) { return null; }
    }

    // Nominatim's usage policy asks for an identifying User-Agent (the shared client already sends "Flowmate/1.0").
    private static HttpRequestMessage NominatimRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept-Language", CultureInfo.CurrentUICulture.Name);
        return request;
    }

    /// <summary>Turns a Location, coordinates text, or an address into a place. Empty input = where I am now.</summary>
    public static async Task<LocationItem> ResolveAsync(ActionContext ctx, ContentValue value, CancellationToken ct)
    {
        if (value.IsEmpty) return await GetCurrentAsync(ctx.Services, TimeSpan.FromSeconds(15), true, false, ct).ConfigureAwait(false);
        if (value[0] is LocationItem l) return l;
        var text = value.AsText().Trim();
        if (GeoMath.TryParseCoordinates(text) is { } c) return new LocationItem(c.Lat, c.Lon);
        if (text.Length == 0) return await GetCurrentAsync(ctx.Services, TimeSpan.FromSeconds(15), true, false, ct).ConfigureAwait(false);
        return await GeocodeAsync(ctx.Services.Http, text, ct).ConfigureAwait(false)
               ?? throw new ActionException($"Couldn't find the place '{text}'. Check the spelling, add the city, or use coordinates like 48.8584, 2.2945.");
    }
}
