using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
/// WIC imaging. Every method here must run on an STA thread: call it through <see cref="StaWorker.Imaging"/>.
/// All operations read the file, honour its EXIF orientation, and write a new file (the original is never touched).
/// </summary>
public static class ImageEngine
{
    private sealed record Loaded(BitmapSource Source, BitmapMetadata? Metadata, ImageFormat? SourceFormat);

    // ---- load / save -------------------------------------------------------------------------

    private static Loaded Load(string path, bool keepMetadata = true)
    {
        if (!File.Exists(path)) throw new ActionException($"Couldn't find the picture '{Path.GetFileName(path)}'. Check it still exists.");
        try
        {
            using var fs = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            var original = frame.Metadata as BitmapMetadata;
            var orientation = ImageMetadataReader.ReadOrientation(original);
            BitmapMetadata? md = null;
            if (keepMetadata && original is not null)
            {
                try
                {
                    md = original.Clone();
                    if (orientation != 1) md.SetQuery("/app1/ifd/{ushort=274}", (ushort)1);
                }
                catch { md = null; }
            }
            BitmapSource src = Oriented(frame, orientation);
            var plain = new WriteableBitmap(src);
            plain.Freeze();
            return new Loaded(plain, md, ImageFormats.FromExtension(Path.GetExtension(path)));
        }
        catch (ActionException) { throw; }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or FileFormatException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            throw new ActionException($"Couldn't open '{Path.GetFileName(path)}' as a picture. It may be damaged or in a format Windows can't read.", ex);
        }
        catch (IOException ex)
        {
            throw new ActionException($"Couldn't open '{Path.GetFileName(path)}'. Close any program that is using it and try again.", ex);
        }
    }

    private static BitmapSource Oriented(BitmapSource frame, int orientation) => orientation switch
    {
        2 => Flip(frame, horizontal: true),
        3 => Rot(frame, 180),
        4 => Flip(frame, horizontal: false),
        5 => Rot(Flip(frame, horizontal: true), 270),
        6 => Rot(frame, 90),
        7 => Rot(Flip(frame, horizontal: true), 90),
        8 => Rot(frame, 270),
        _ => frame,
    };

    private static BitmapSource Rot(BitmapSource s, double degrees) => new TransformedBitmap(s, new RotateTransform(degrees));
    private static BitmapSource Flip(BitmapSource s, bool horizontal) =>
        new TransformedBitmap(s, new ScaleTransform(horizontal ? -1 : 1, horizontal ? 1 : -1));

    private static bool HasAlpha(BitmapSource s)
    {
        var f = s.Format;
        if (f == PixelFormats.Bgra32 || f == PixelFormats.Pbgra32 || f == PixelFormats.Prgba64 || f == PixelFormats.Rgba64
            || f == PixelFormats.Prgba128Float || f == PixelFormats.Rgba128Float) return true;
        return s.Palette?.Colors.Any(c => c.A < 255) == true;
    }

    private static BitmapSource Render(int w, int h, Color? background, Action<DrawingContext> draw, BitmapScalingMode mode = BitmapScalingMode.HighQuality)
    {
        var dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, mode);
        using (var dc = dv.RenderOpen())
        {
            if (background is { } bg) dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(0, 0, w, h));
            draw(dc);
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    private static ImageResult Save(BitmapSource src, string path, ImageFormat format, int quality, BitmapMetadata? metadata, ImageFormat? sourceFormat)
    {
        if (format is ImageFormat.Jpeg or ImageFormat.Bmp or ImageFormat.Gif && HasAlpha(src))
        {
            var flat = Render(src.PixelWidth, src.PixelHeight, Colors.White, dc => dc.DrawImage(src, new Rect(0, 0, src.PixelWidth, src.PixelHeight)), BitmapScalingMode.NearestNeighbor);
            var converted = new FormatConvertedBitmap(flat, PixelFormats.Bgr32, null, 0);
            converted.Freeze();
            src = converted;
        }
        var useMetadata = metadata is not null && format == ImageFormat.Jpeg && sourceFormat == ImageFormat.Jpeg;
        try
        {
            Encode(src, path, format, quality, useMetadata ? metadata : null);
        }
        catch (Exception ex) when (useMetadata && ex is NotSupportedException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            Encode(src, path, format, quality, null);
        }
        return new ImageResult(path, src.PixelWidth, src.PixelHeight);
    }

    private static void Encode(BitmapSource src, string path, ImageFormat format, int quality, BitmapMetadata? metadata)
    {
        BitmapEncoder encoder = format switch
        {
            ImageFormat.Jpeg => new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) },
            ImageFormat.Bmp => new BmpBitmapEncoder(),
            ImageFormat.Gif => new GifBitmapEncoder(),
            ImageFormat.Tiff => new TiffBitmapEncoder { Compression = TiffCompressOption.Zip },
            _ => new PngBitmapEncoder(),
        };
        encoder.Frames.Add(metadata is null ? BitmapFrame.Create(src) : BitmapFrame.Create(src, null, metadata, null));
        try
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(fs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(path); } catch { }
            throw new ActionException($"Couldn't save the picture '{Path.GetFileName(path)}'. Check the folder exists and you can save there.", ex);
        }
        catch
        {
            try { File.Delete(path); } catch { }
            throw;
        }
    }

    private static ImageFormat TargetFormat(string dst, ImageFormat? fallback = null) =>
        ImageFormats.FromExtension(Path.GetExtension(dst)) ?? fallback ?? ImageFormat.Png;

    // ---- operations --------------------------------------------------------------------------

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

    public static ImageResult Resize(string src, string dst, ResizeSpec spec, int quality = 90)
    {
        var img = Load(src);
        var (w, h) = ComputeSize(img.Source.PixelWidth, img.Source.PixelHeight, spec);
        BitmapSource result = img.Source;
        if (w != img.Source.PixelWidth || h != img.Source.PixelHeight)
            result = Render(w, h, null, dc => dc.DrawImage(img.Source, new Rect(0, 0, w, h)));
        return Save(result, dst, TargetFormat(dst), quality, img.Metadata, img.SourceFormat);
    }

    public static ImageResult Convert(string src, string dst, ImageFormat format, int quality)
    {
        var img = Load(src);
        return Save(img.Source, dst, format, quality, img.Metadata, img.SourceFormat);
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

    public static ImageResult Crop(string src, string dst, CropSpec spec, int quality = 90)
    {
        var img = Load(src);
        var (x, y, w, h) = ComputeCrop(img.Source.PixelWidth, img.Source.PixelHeight, spec);
        var cropped = new CroppedBitmap(img.Source, new Int32Rect(x, y, w, h));
        var plain = new WriteableBitmap(cropped);
        plain.Freeze();
        return Save(plain, dst, TargetFormat(dst), quality, img.Metadata, img.SourceFormat);
    }

    public static ImageResult Rotate(string src, string dst, string op, int quality = 90)
    {
        var img = Load(src);
        BitmapSource result = op switch
        {
            "right" => Rot(img.Source, 90),
            "left" => Rot(img.Source, 270),
            "180" => Rot(img.Source, 180),
            "flipH" => Flip(img.Source, true),
            "flipV" => Flip(img.Source, false),
            "upright" => img.Source,
            _ => throw new ActionException($"Don't know the rotation '{op}'. Choose one from the list."),
        };
        if (!ReferenceEquals(result, img.Source)) { var w = new WriteableBitmap(result); w.Freeze(); result = w; }
        return Save(result, dst, TargetFormat(dst), quality, img.Metadata, img.SourceFormat);
    }

    /// <summary>Re-encodes the picture with no photo information (location, camera, date). Pixels are kept as they look (rotated upright).</summary>
    public static ImageResult Strip(string src, string dst, int quality = 95)
    {
        var img = Load(src, keepMetadata: false);
        return Save(img.Source, dst, TargetFormat(dst), quality, null, null);
    }

    public static Color ParseColor(string text, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        try { return (Color)ColorConverter.ConvertFromString(text.Trim()); }
        catch (FormatException) { throw new ActionException($"Don't know the colour '{text}'. Use a name like red, or a code like #FF8800."); }
    }

    public static ImageResult AddText(string src, string dst, TextSpec spec, int quality = 90)
    {
        if (string.IsNullOrEmpty(spec.Text)) throw new ActionException("Type the text to put on the picture.");
        var img = Load(src);
        var w = img.Source.PixelWidth;
        var h = img.Source.PixelHeight;
        var color = ParseColor(spec.Color, Colors.White);
        var em = Math.Max(8, Math.Min(w, h) * Math.Clamp(spec.SizePercent, 1, 100) / 100.0);
        var margin = Math.Max(4, Math.Min(w, h) * 0.03);
        var typeface = new Typeface(new FontFamily(string.IsNullOrWhiteSpace(spec.Font) ? "Segoe UI" : spec.Font),
            FontStyles.Normal, spec.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
        var pos = spec.Position.ToLowerInvariant();
        var align = pos.Contains("left") ? TextAlignment.Left : pos.Contains("right") ? TextAlignment.Right : TextAlignment.Center;

        FormattedText Make(Brush brush) => new(spec.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, em, brush, 1.0)
        {
            MaxTextWidth = Math.Max(1, w - 2 * margin),
            TextAlignment = align,
        };
        var ft = Make(new SolidColorBrush(color));
        var y = pos.StartsWith("top") ? margin : pos.StartsWith("bottom") ? h - margin - ft.Height : (h - ft.Height) / 2;
        var origin = new Point(margin, Math.Max(0, y));
        var result = Render(w, h, null, dc =>
        {
            dc.DrawImage(img.Source, new Rect(0, 0, w, h));
            if (spec.Shadow)
            {
                var off = Math.Max(1, em * 0.06);
                dc.DrawText(Make(new SolidColorBrush(Color.FromArgb(170, 0, 0, 0))), new Point(origin.X + off, origin.Y + off));
            }
            dc.DrawText(ft, origin);
        });
        return Save(result, dst, TargetFormat(dst), quality, img.Metadata, img.SourceFormat);
    }

    public static ImageResult Combine(IReadOnlyList<string> sources, string dst, CombineSpec spec, int quality = 90)
    {
        if (sources.Count == 0) throw new ActionException("Choose at least one picture to combine.");
        var images = sources.Select(s => Load(s, keepMetadata: false).Source).ToList();
        var spacing = (int)Math.Max(0, spec.Spacing);
        var background = ParseColor(spec.Background, Colors.White);
        var rects = new List<Rect>();
        double totalW, totalH;

        switch (spec.Layout)
        {
            case "vertical":
                {
                    var maxW = images.Max(i => (double)i.PixelWidth);
                    double y = 0;
                    foreach (var i in images)
                    {
                        var s = spec.MatchSize ? maxW / i.PixelWidth : 1;
                        var iw = i.PixelWidth * s; var ih = i.PixelHeight * s;
                        rects.Add(new Rect((maxW - iw) / 2, y, iw, ih));
                        y += ih + spacing;
                    }
                    totalW = rects.Max(r => r.Right); totalH = y - spacing;
                    break;
                }
            case "grid":
                {
                    var cols = Math.Clamp(spec.Columns, 1, images.Count);
                    var cellW = images.Max(i => (double)i.PixelWidth);
                    var cellH = images.Max(i => (double)i.PixelHeight);
                    for (var n = 0; n < images.Count; n++)
                    {
                        var i = images[n];
                        var s = spec.MatchSize ? Math.Min(cellW / i.PixelWidth, cellH / i.PixelHeight) : 1;
                        var iw = i.PixelWidth * s; var ih = i.PixelHeight * s;
                        var cx = (n % cols) * (cellW + spacing);
                        var cy = (n / cols) * (cellH + spacing);
                        rects.Add(new Rect(cx + (cellW - iw) / 2, cy + (cellH - ih) / 2, iw, ih));
                    }
                    var rows = (images.Count + cols - 1) / cols;
                    totalW = cols * cellW + (cols - 1) * spacing; totalH = rows * cellH + (rows - 1) * spacing;
                    break;
                }
            default:
                {
                    var maxH = images.Max(i => (double)i.PixelHeight);
                    double x = 0;
                    foreach (var i in images)
                    {
                        var s = spec.MatchSize ? maxH / i.PixelHeight : 1;
                        var iw = i.PixelWidth * s; var ih = i.PixelHeight * s;
                        rects.Add(new Rect(x, (maxH - ih) / 2, iw, ih));
                        x += iw + spacing;
                    }
                    totalW = x - spacing; totalH = rects.Max(r => r.Bottom);
                    break;
                }
        }
        var pw = (int)Math.Round(totalW);
        var ph = (int)Math.Round(totalH);
        if (pw < 1 || ph < 1 || pw > 20000 || ph > 20000)
            throw new ActionException("The combined picture would be too large. Use fewer or smaller pictures, or turn off \"Make all the same size\".");
        var result = Render(pw, ph, background, dc =>
        {
            for (var n = 0; n < images.Count; n++)
            {
                var r = rects[n];
                dc.DrawImage(images[n], new Rect(Math.Round(r.X), Math.Round(r.Y), Math.Max(1, Math.Round(r.Width)), Math.Max(1, Math.Round(r.Height))));
            }
        });
        return Save(result, dst, TargetFormat(dst), quality, null, null);
    }

    /// <summary>Image bytes the AI service accepts: JPEG, PNG, GIF or WebP, not too big. Large or unusual formats are converted.</summary>
    public static (byte[] Data, string MediaType) PrepareForAi(string path)
    {
        const int MaxEdge = 2000;
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var direct = ext switch { "jpg" or "jpeg" => "image/jpeg", "png" => "image/png", "gif" => "image/gif", "webp" => "image/webp", _ => null };
        if (!File.Exists(path)) throw new ActionException($"Couldn't find the picture '{Path.GetFileName(path)}'. Check it still exists.");
        if (direct is not null && new FileInfo(path).Length <= 3_000_000)
        {
            try
            {
                if (ext == "webp") return (File.ReadAllBytes(path), direct);
                var info = ImageMetadataReader.Read(path);
                if (Math.Max(info.Width, info.Height) <= MaxEdge && info.Orientation == 1) return (File.ReadAllBytes(path), direct);
            }
            catch { /* fall through and convert */ }
        }
        var img = Load(path, keepMetadata: false);
        var src = img.Source;
        var edge = Math.Max(src.PixelWidth, src.PixelHeight);
        if (edge > MaxEdge)
        {
            var s = (double)MaxEdge / edge;
            var w = Math.Max(1, (int)Math.Round(src.PixelWidth * s));
            var h = Math.Max(1, (int)Math.Round(src.PixelHeight * s));
            src = Render(w, h, Colors.White, dc => dc.DrawImage(img.Source, new Rect(0, 0, w, h)));
        }
        using var ms = new MemoryStream();
        var tmp = new FormatConvertedBitmap(src, PixelFormats.Bgr32, null, 0);
        var enc = new JpegBitmapEncoder { QualityLevel = 85 };
        enc.Frames.Add(BitmapFrame.Create(HasAlpha(src) ? FlattenForAi(src) : tmp));
        enc.Save(ms);
        return (ms.ToArray(), "image/jpeg");
    }

    private static BitmapSource FlattenForAi(BitmapSource src)
    {
        var flat = Render(src.PixelWidth, src.PixelHeight, Colors.White, dc => dc.DrawImage(src, new Rect(0, 0, src.PixelWidth, src.PixelHeight)), BitmapScalingMode.NearestNeighbor);
        var c = new FormatConvertedBitmap(flat, PixelFormats.Bgr32, null, 0);
        c.Freeze();
        return c;
    }

    /// <summary>Reads the pixel size of a picture (upright, as it is displayed).</summary>
    public static ImageResult ReadSize(string path)
    {
        var info = ImageMetadataReader.Read(path);
        return new ImageResult(path, info.Width, info.Height);
    }

    /// <summary>JPEG bytes of the picture flattened on white, upright, for embedding in a PDF.</summary>
    public static (byte[] Data, int Width, int Height) ToJpegBytes(string path, int quality = 92)
    {
        var img = Load(path, keepMetadata: false);
        var src = HasAlpha(img.Source) ? FlattenForAi(img.Source) : img.Source;
        using var ms = new MemoryStream();
        var enc = new JpegBitmapEncoder { QualityLevel = quality };
        enc.Frames.Add(BitmapFrame.Create(src));
        enc.Save(ms);
        return (ms.ToArray(), src.PixelWidth, src.PixelHeight);
    }
}
