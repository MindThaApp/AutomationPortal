using System.Globalization;

namespace Flowmate.Actions.Documents;

public enum ImageFormat { Jpeg, Png, Bmp, Gif, Tiff }

public static class ImageFormats
{
    public static ImageFormat? FromExtension(string? ext) => (ext ?? "").TrimStart('.').ToLowerInvariant() switch
    {
        "jpg" or "jpeg" or "jpe" or "jfif" => ImageFormat.Jpeg,
        "png" => ImageFormat.Png,
        "bmp" => ImageFormat.Bmp,
        "gif" => ImageFormat.Gif,
        "tif" or "tiff" => ImageFormat.Tiff,
        _ => null,
    };

    public static ImageFormat FromChoice(string id) => id.ToLowerInvariant() switch
    {
        "jpeg" or "jpg" => ImageFormat.Jpeg,
        "bmp" => ImageFormat.Bmp,
        "gif" => ImageFormat.Gif,
        "tiff" or "tif" => ImageFormat.Tiff,
        _ => ImageFormat.Png,
    };

    public static string Extension(ImageFormat f) => f switch
    {
        ImageFormat.Jpeg => ".jpg", ImageFormat.Bmp => ".bmp", ImageFormat.Gif => ".gif", ImageFormat.Tiff => ".tiff", _ => ".png",
    };
}

public sealed record ImageResult(string Path, int Width, int Height)
{
    public ImageItem ToItem() => new(Path, Width, Height);
}

public sealed record ResizeSpec(string By, double Width, double Height, double LongestSide, double Percent, bool NeverEnlarge);
public sealed record CropSpec(string By, double X, double Y, double Width, double Height, string Ratio, string Anchor);
public sealed record TextSpec(string Text, string Position, double SizePercent, string Color, bool Shadow, bool Bold, string Font);
public sealed record CombineSpec(string Layout, int Columns, double Spacing, string Background, bool MatchSize);

/// <summary>
/// Picture decoding, editing and encoding, supplied by the host (see <see cref="ImageEngine.Backend"/>).
/// All operations read the file, honour its EXIF orientation, and write a new file (the original is never touched).
/// Calls arrive on <see cref="StaWorker.Imaging"/>.
/// </summary>
public interface IImageBackend
{
    ImageResult Resize(string src, string dst, ResizeSpec spec, int quality);
    ImageResult Convert(string src, string dst, ImageFormat format, int quality);
    ImageResult Crop(string src, string dst, CropSpec spec, int quality);
    ImageResult Rotate(string src, string dst, string op, int quality);
    ImageResult Strip(string src, string dst, int quality);
    ImageResult AddText(string src, string dst, TextSpec spec, int quality);
    ImageResult Combine(IReadOnlyList<string> sources, string dst, CombineSpec spec, int quality);
    /// <summary>Image bytes the AI service accepts: JPEG, PNG, GIF or WebP, not too big.</summary>
    (byte[] Data, string MediaType) PrepareForAi(string path);
    /// <summary>JPEG bytes of the picture flattened on white, upright.</summary>
    (byte[] Data, int Width, int Height) ToJpegBytes(string path, int quality);
    /// <summary>Size (upright) and EXIF details.</summary>
    ImageInfo ReadInfo(string path);
}

/// <summary>
/// Picture operations used by the image, PDF and AI actions. The work is done by <see cref="Backend"/>, which the host
/// sets once at startup (Flowmate's WPF host uses WIC through WPF imaging; without one the WinRT backend is used). Call through <see cref="StaWorker.Imaging"/>.
/// </summary>
public static class ImageEngine
{
    private static IImageBackend? _backend;
    private static IImageBackend? _default;

    /// <summary>Process-wide. When a host hasn't set one, pictures are handled by <see cref="WinRtImageBackend"/> (no WPF needed).</summary>
    public static IImageBackend Backend
    {
        get => _backend ?? (_default ??= CreateDefault());
        set => _backend = value;
    }

    private static IImageBackend CreateDefault() =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) ? new WinRtImageBackend() : throw FlowmateServices.NotAvailable("edit pictures");

    /// <summary>True when a host set a backend explicitly (the built-in default doesn't count).</summary>
    public static bool HasBackend => _backend is not null;

    public static (int Width, int Height) ComputeSize(int w, int h, ResizeSpec spec)
    {
        double scale = spec.By switch
        {
            "width" => spec.Width > 0 ? spec.Width / w : throw new ActionException("Enter a width bigger than 0."),
            "height" => spec.Height > 0 ? spec.Height / h : throw new ActionException("Enter a height bigger than 0."),
            "longest" => spec.LongestSide > 0 ? spec.LongestSide / Math.Max(w, h) : throw new ActionException("Enter a size bigger than 0."),
            "percent" => spec.Percent > 0 ? spec.Percent / 100.0 : throw new ActionException("Enter a percentage bigger than 0."),
            _ => throw new ActionException("Choose how to resize the pictures."),
        };
        if (spec.NeverEnlarge && scale > 1) scale = 1;
        var nw = Math.Max(1, (int)Math.Round(w * scale));
        var nh = Math.Max(1, (int)Math.Round(h * scale));
        if (nw > 30000 || nh > 30000) throw new ActionException("That would make the picture too large. Choose a smaller size.");
        return (nw, nh);
    }

    public static (int X, int Y, int W, int H) ComputeCrop(int w, int h, CropSpec spec)
    {
        if (spec.By == "box")
        {
            var x = (int)Math.Max(0, spec.X);
            var y = (int)Math.Max(0, spec.Y);
            if (x >= w || y >= h) throw new ActionException($"The crop area starts outside the picture ({w} × {h} pixels). Lower the left/top position.");
            var cw = (int)(spec.Width > 0 ? Math.Min(spec.Width, w - x) : w - x);
            var ch = (int)(spec.Height > 0 ? Math.Min(spec.Height, h - y) : h - y);
            return (x, y, Math.Max(1, cw), Math.Max(1, ch));
        }
        var parts = spec.Ratio.Split(':');
        if (parts.Length != 2 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var rw)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var rh) || rw <= 0 || rh <= 0)
            throw new ActionException($"Don't know the shape '{spec.Ratio}'. Choose one from the list.");
        var aspect = rw / rh;
        int tw, th;
        if ((double)w / h > aspect) { th = h; tw = Math.Max(1, (int)Math.Round(h * aspect)); }
        else { tw = w; th = Math.Max(1, (int)Math.Round(w / aspect)); }
        var a = spec.Anchor.ToLowerInvariant();
        var ox = a.Contains("left") ? 0 : a.Contains("right") ? w - tw : (w - tw) / 2;
        var oy = a.Contains("top") ? 0 : a.Contains("bottom") ? h - th : (h - th) / 2;
        return (ox, oy, tw, th);
    }

    public static ImageResult Resize(string src, string dst, ResizeSpec spec, int quality = 90) => Backend.Resize(src, dst, spec, quality);
    public static ImageResult Convert(string src, string dst, ImageFormat format, int quality) => Backend.Convert(src, dst, format, quality);
    public static ImageResult Crop(string src, string dst, CropSpec spec, int quality = 90) => Backend.Crop(src, dst, spec, quality);
    public static ImageResult Rotate(string src, string dst, string op, int quality = 90) => Backend.Rotate(src, dst, op, quality);

    /// <summary>Re-encodes the picture with no photo information (location, camera, date). Pixels are kept as they look (rotated upright).</summary>
    public static ImageResult Strip(string src, string dst, int quality = 95) => Backend.Strip(src, dst, quality);

    public static ImageResult AddText(string src, string dst, TextSpec spec, int quality = 90) => Backend.AddText(src, dst, spec, quality);
    public static ImageResult Combine(IReadOnlyList<string> sources, string dst, CombineSpec spec, int quality = 90) => Backend.Combine(sources, dst, spec, quality);

    /// <summary>Image bytes the AI service accepts: JPEG, PNG, GIF or WebP, not too big. Large or unusual formats are converted.</summary>
    public static (byte[] Data, string MediaType) PrepareForAi(string path) => Backend.PrepareForAi(path);

    /// <summary>Reads the pixel size of a picture (upright, as it is displayed).</summary>
    public static ImageResult ReadSize(string path)
    {
        var info = ImageMetadataReader.Read(path);
        return new ImageResult(path, info.Width, info.Height);
    }

    /// <summary>JPEG bytes of the picture flattened on white, upright, for embedding in a PDF.</summary>
    public static (byte[] Data, int Width, int Height) ToJpegBytes(string path, int quality = 92) => Backend.ToJpegBytes(path, quality);
}
