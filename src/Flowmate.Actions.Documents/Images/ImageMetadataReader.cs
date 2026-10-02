using System.Collections;
using System.Globalization;
using System.Windows.Media.Imaging;

namespace Flowmate.Actions.Documents;

public sealed record ImageInfo(
    int Width, int Height, DateTime? DateTaken, string? CameraMake, string? CameraModel,
    double? Latitude, double? Longitude, int Orientation);

/// <summary>Reads EXIF details through WIC metadata queries. Run on an STA thread (see <see cref="StaWorker"/>).</summary>
public static class ImageMetadataReader
{
    public static object? Query(BitmapMetadata? md, params string[] queries)
    {
        if (md is null) return null;
        foreach (var q in queries)
        {
            try
            {
                if (md.ContainsQuery(q) && md.GetQuery(q) is { } v) return v;
            }
            catch { /* unsupported query for this format */ }
        }
        return null;
    }

    public static int ReadOrientation(BitmapMetadata? md)
    {
        var v = Query(md, "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}");
        try { return v is null ? 1 : Convert.ToInt32(v, CultureInfo.InvariantCulture) is >= 1 and <= 8 and var o ? o : 1; }
        catch { return 1; }
    }

    public static DateTime? ReadDateTaken(BitmapMetadata? md)
    {
        foreach (var tag in new[] { 36867, 36868 })
        {
            var s = Query(md, $"/app1/ifd/exif/{{ushort={tag}}}", $"/ifd/exif/{{ushort={tag}}}") as string;
            if (ParseExifDate(s) is { } d) return d;
        }
        try { if (ParseExifDate(md?.DateTaken) is { } d) return d; } catch { }
        return null;
    }

    public static DateTime? ParseExifDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim('\0', ' ');
        if (DateTime.TryParseExact(s, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d.Year > 1900) return d;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) && d.Year > 1900) return d;
        if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out d) && d.Year > 1900) return d;
        return null;
    }

    private static double? Degrees(object? value)
    {
        if (value is not IEnumerable seq || value is string) return null;
        var parts = new List<double>();
        foreach (var o in seq)
        {
            ulong packed;
            try { packed = Convert.ToUInt64(o, CultureInfo.InvariantCulture); } catch { return null; }
            var num = (uint)(packed & 0xFFFFFFFF);
            var den = (uint)(packed >> 32);
            parts.Add(den == 0 ? 0 : (double)num / den);
        }
        if (parts.Count < 3) return null;
        return parts[0] + parts[1] / 60.0 + parts[2] / 3600.0;
    }

    public static (double Lat, double Lon)? ReadGps(BitmapMetadata? md)
    {
        var lat = Degrees(Query(md, "/app1/ifd/gps/{ushort=2}", "/ifd/gps/{ushort=2}"));
        var lon = Degrees(Query(md, "/app1/ifd/gps/{ushort=4}", "/ifd/gps/{ushort=4}"));
        if (lat is null || lon is null) return null;
        var latRef = Query(md, "/app1/ifd/gps/{ushort=1}", "/ifd/gps/{ushort=1}") as string;
        var lonRef = Query(md, "/app1/ifd/gps/{ushort=3}", "/ifd/gps/{ushort=3}") as string;
        if (latRef?.StartsWith("S", StringComparison.OrdinalIgnoreCase) == true) lat = -lat;
        if (lonRef?.StartsWith("W", StringComparison.OrdinalIgnoreCase) == true) lon = -lon;
        return (lat.Value, lon.Value);
    }

    private static string? Text(object? o) => (o as string)?.Trim('\0', ' ') is { Length: > 0 } s ? s : null;

    public static ImageInfo Read(string path)
    {
        using var fs = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnDemand);
        var frame = decoder.Frames[0];
        var md = frame.Metadata as BitmapMetadata;
        var orientation = ReadOrientation(md);
        var (w, h) = orientation >= 5 ? (frame.PixelHeight, frame.PixelWidth) : (frame.PixelWidth, frame.PixelHeight);
        var make = Text(Query(md, "/app1/ifd/{ushort=271}", "/ifd/{ushort=271}"));
        var model = Text(Query(md, "/app1/ifd/{ushort=272}", "/ifd/{ushort=272}"));
        var gps = ReadGps(md);
        return new ImageInfo(w, h, ReadDateTaken(md), make, model, gps?.Lat, gps?.Lon, orientation);
    }

    /// <summary>Date taken from the photo's EXIF data, or null (also for non-photos). Run on an STA thread.</summary>
    public static DateTime? TryReadDateTaken(string path)
    {
        if (!FileKinds.IsImage(path) || !File.Exists(path)) return null;
        try { return Read(path).DateTaken; } catch { return null; }
    }
}
