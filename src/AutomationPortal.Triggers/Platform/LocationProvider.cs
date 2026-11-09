using System.Text.Json;
using Flowmate.Core;
using Windows.Devices.Geolocation;

namespace Flowmate.Triggers;

public sealed record GeoPoint(double Latitude, double Longitude, string? City, DateTimeOffset FoundAt, string Source);

/// <summary>
/// Where the user is, for sunrise/sunset and weather alerts. Order: the home location in Settings, a home location file the app can write
/// (<c>trigger-state\home-location.json</c>: {"latitude":..,"longitude":..}), the cached result (7 days), Windows location,
/// then an approximate position from the internet address.
/// </summary>
internal sealed class LocationProvider
{
    private const string CacheKey = "location";
    private static readonly TimeSpan CacheLife = TimeSpan.FromDays(7);
    private readonly FlowmateServices _services;
    private readonly TriggerStateStore _state;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private GeoPoint? _memory;

    public LocationProvider(FlowmateServices services, TriggerStateStore state) { _services = services; _state = state; }

    public async Task<GeoPoint?> GetAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var setting = TryHomeSetting();
            if (setting is not null) return setting;
            var home = _state.Load<HomeFile>("home-location");
            if (home is { Latitude: not null, Longitude: not null })
                return new GeoPoint(home.Latitude.Value, home.Longitude.Value, home.City, DateTimeOffset.Now, "home");

            if (_memory is not null && DateTimeOffset.Now - _memory.FoundAt < CacheLife) return _memory;
            var cached = _state.Load<GeoPoint>(CacheKey);
            if (cached is not null && DateTimeOffset.Now - cached.FoundAt < CacheLife) return _memory = cached;

            var found = await FromWindowsAsync(ct) ?? await FromInternetAsync(ct);
            if (found is not null) { _memory = found; _state.Save(CacheKey, found); return found; }
            return cached; // stale is better than nothing
        }
        finally { _gate.Release(); }
    }

    private GeoPoint? TryHomeSetting()
    {
        try
        {
            var s = _services.Settings.Current;
            return s.HomeLatitude is { } la && s.HomeLongitude is { } lo ? new GeoPoint(la, lo, s.HomeCity, DateTimeOffset.Now, "home") : null;
        }
        catch (ActionException) { return null; }
    }

    private sealed class HomeFile { public double? Latitude { get; set; } public double? Longitude { get; set; } public string? City { get; set; } }

    private static async Task<GeoPoint?> FromWindowsAsync(CancellationToken ct)
    {
        try
        {
            var locator = new Geolocator { DesiredAccuracyInMeters = 5000 };
            var op = locator.GetGeopositionAsync(TimeSpan.FromHours(12), TimeSpan.FromSeconds(8));
            var task = op.AsTask(ct);
            if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10), ct)) != task) { op.Cancel(); return null; }
            var p = (await task).Coordinate.Point.Position;
            return new GeoPoint(p.Latitude, p.Longitude, null, DateTimeOffset.Now, "windows");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TriggerLog.Write("Windows location unavailable: " + ex.Message);
            return null;
        }
    }

    private async Task<GeoPoint?> FromInternetAsync(CancellationToken ct)
    {
        foreach (var url in new[] { "https://ipwho.is/", "https://ipapi.co/json/" })
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                var json = await _services.Http.GetStringAsync(url, cts.Token);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False) continue;
                if (root.TryGetProperty("latitude", out var lat) && root.TryGetProperty("longitude", out var lon)
                    && lat.TryGetDouble(out var la) && lon.TryGetDouble(out var lo))
                {
                    var city = root.TryGetProperty("city", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                    return new GeoPoint(la, lo, city, DateTimeOffset.Now, "internet");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { TriggerLog.Write($"Location lookup {url} failed: {ex.Message}"); }
        }
        return null;
    }
}
