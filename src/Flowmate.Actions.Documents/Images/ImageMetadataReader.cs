using System.Globalization;

namespace Flowmate.Actions.Documents;

public sealed record ImageInfo(
    int Width, int Height, DateTime? DateTaken, string? CameraMake, string? CameraModel,
    double? Latitude, double? Longitude, int Orientation);

/// <summary>Reads EXIF details through <see cref="ImageEngine.Backend"/>. Run on an STA thread (see <see cref="StaWorker"/>).</summary>
public static class ImageMetadataReader
{
    public static DateTime? ParseExifDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim('\0', ' ');
        if (DateTime.TryParseExact(s, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d.Year > 1900) return d;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) && d.Year > 1900) return d;
        if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out d) && d.Year > 1900) return d;
        return null;
    }

    public static ImageInfo Read(string path) => ImageEngine.Backend.ReadInfo(path);

    /// <summary>Date taken from the photo's EXIF data, or null (also for non-photos). Run on an STA thread.</summary>
    public static DateTime? TryReadDateTaken(string path)
    {
        if (!FileKinds.IsImage(path) || !File.Exists(path)) return null;
        var backend = ImageEngine.Backend;
        try { return backend.ReadInfo(path).DateTaken; } catch { return null; }
    }
}
