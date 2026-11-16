using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using AutomationPortal.Core;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace AutomationPortal.Actions.Documents;

/// <summary>
/// Picture editing on Windows.Graphics.Imaging (WIC underneath), with no WPF. Used when a host hasn't set
/// <see cref="ImageEngine.Backend"/>. Pixels are handled as upright BGRA arrays; text is drawn with GDI+.
/// Gaps against the WPF backend: JPEG-to-JPEG keeps the common photo tags (camera, date, exposure, location) rather than every EXIF
/// entry, and resampling is a triangle filter rather than WPF's high-quality scaler.
/// </summary>
public sealed class WinRtImageBackend : IImageBackend
{
    private sealed class Pix
    {
        public Pix(int w, int h, byte[]? data = null) { W = w; H = h; D = data ?? new byte[w * h * 4]; }
        public int W { get; }
        public int H { get; }
        public byte[] D { get; }
    }

    private sealed record Loaded(Pix Pixels, Dictionary<string, BitmapTypedValue>? Metadata, ImageFormat? SourceFormat);

    private static T Wait<T>(IAsyncOperation<T> op) => op.AsTask().GetAwaiter().GetResult();
    private static TR Wait<TR, TP>(IAsyncOperationWithProgress<TR, TP> op) => op.AsTask().GetAwaiter().GetResult();
    private static void Wait(IAsyncAction op) => op.AsTask().GetAwaiter().GetResult();

    // ---- load / save -------------------------------------------------------------------------

    private static readonly string[] IfdTags = { "/app1/ifd/{ushort=271}", "/app1/ifd/{ushort=272}", "/app1/ifd/{ushort=305}", "/app1/ifd/{ushort=306}",
        "/app1/ifd/{ushort=282}", "/app1/ifd/{ushort=283}", "/app1/ifd/{ushort=296}",
        "/app1/ifd/exif/{ushort=33434}", "/app1/ifd/exif/{ushort=33437}", "/app1/ifd/exif/{ushort=34855}", "/app1/ifd/exif/{ushort=36867}",
        "/app1/ifd/exif/{ushort=36868}", "/app1/ifd/exif/{ushort=37386}", "/app1/ifd/exif/{ushort=42036}",
        "/app1/ifd/gps/{ushort=1}", "/app1/ifd/gps/{ushort=2}", "/app1/ifd/gps/{ushort=3}", "/app1/ifd/gps/{ushort=4}",
        "/app1/ifd/gps/{ushort=5}", "/app1/ifd/gps/{ushort=6}" };

    private static BitmapDecoder OpenDecoder(string path, out InMemoryRandomAccessStream stream)
    {
        if (!File.Exists(path)) throw new ActionException($"Couldn't find the picture '{Path.GetFileName(path)}'. Check it still exists.");
        try
        {
            var bytes = File.ReadAllBytes(path);
            stream = new InMemoryRandomAccessStream();
            Wait(stream.WriteAsync(bytes.AsBuffer()));
            stream.Seek(0);
            return Wait(BitmapDecoder.CreateAsync(stream));
        }
        catch (IOException ex)
        {
            throw new ActionException($"Couldn't open '{Path.GetFileName(path)}'. Close any program that is using it and try again.", ex);
        }
        catch (Exception ex) when (ex is not ActionException)
        {
            throw new ActionException($"Couldn't open '{Path.GetFileName(path)}' as a picture. It may be damaged or in a format Windows can't read.", ex);
        }
    }

    private static object? Query(BitmapDecoder decoder, params string[] queries)
    {
        foreach (var q in queries)
        {
            try
            {
                var set = Wait(decoder.BitmapProperties.GetPropertiesAsync(new[] { q }));
                if (set.TryGetValue(q, out var v) && v.Value is { } value) return value;
            }
            catch { /* not in this file or format */ }
        }
        return null;
    }

    private static BitmapTypedValue? QueryTyped(BitmapDecoder decoder, string q)
    {
        try
        {
            var set = Wait(decoder.BitmapProperties.GetPropertiesAsync(new[] { q }));
            return set.TryGetValue(q, out var v) && v.Value is not null ? v : null;
        }
        catch { return null; }
    }

    private static int ReadOrientation(BitmapDecoder decoder)
    {
        var v = Query(decoder, "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}");
        try { return v is null ? 1 : System.Convert.ToInt32(v, CultureInfo.InvariantCulture) is >= 1 and <= 8 and var o ? o : 1; }
        catch { return 1; }
    }

    private static Loaded Load(string path, bool keepMetadata = true)
    {
        var decoder = OpenDecoder(path, out var stream);
        using (stream)
        {
            try
            {
                var sb = Wait(decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(),
                    ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage));
                using (sb)
                {
                    var buf = new Windows.Storage.Streams.Buffer((uint)(sb.PixelWidth * sb.PixelHeight * 4));
                    sb.CopyToBuffer(buf);
                    var pix = new Pix(sb.PixelWidth, sb.PixelHeight, buf.ToArray());
                    var srcFormat = ImageFormats.FromExtension(Path.GetExtension(path));
                    Dictionary<string, BitmapTypedValue>? md = null;
                    if (keepMetadata && srcFormat == ImageFormat.Jpeg)
                    {
                        foreach (var q in IfdTags)
                            if (QueryTyped(decoder, q) is { } v) (md ??= new())[q] = v;
                    }
                    return new Loaded(pix, md, srcFormat);
                }
            }
            catch (Exception ex) when (ex is not ActionException)
            {
                throw new ActionException($"Couldn't open '{Path.GetFileName(path)}' as a picture. It may be damaged or in a format Windows can't read.", ex);
            }
        }
    }

    private static bool HasAlpha(Pix p)
    {
        var d = p.D;
        for (var i = 3; i < d.Length; i += 4) if (d[i] < 255) return true;
        return false;
    }

    private static Pix Flatten(Pix p)
    {
        var o = new Pix(p.W, p.H);
        for (var i = 0; i < p.D.Length; i += 4)
        {
            var a = p.D[i + 3];
            o.D[i] = (byte)((p.D[i] * a + 255 * (255 - a) + 127) / 255);
            o.D[i + 1] = (byte)((p.D[i + 1] * a + 255 * (255 - a) + 127) / 255);
            o.D[i + 2] = (byte)((p.D[i + 2] * a + 255 * (255 - a) + 127) / 255);
            o.D[i + 3] = 255;
        }
        return o;
    }

    private static byte[] Encode(Pix p, ImageFormat format, int quality, Dictionary<string, BitmapTypedValue>? metadata)
    {
        var id = format switch
        {
            ImageFormat.Jpeg => BitmapEncoder.JpegEncoderId,
            ImageFormat.Bmp => BitmapEncoder.BmpEncoderId,
            ImageFormat.Gif => BitmapEncoder.GifEncoderId,
            ImageFormat.Tiff => BitmapEncoder.TiffEncoderId,
            _ => BitmapEncoder.PngEncoderId,
        };
        var alpha = format is ImageFormat.Png or ImageFormat.Tiff ? BitmapAlphaMode.Straight : BitmapAlphaMode.Ignore;

        byte[] Run(bool zip)
        {
            var options = new BitmapPropertySet();
            if (format == ImageFormat.Jpeg) options.Add("ImageQuality", new BitmapTypedValue(Math.Clamp(quality, 1, 100) / 100f, PropertyType.Single));
            if (format == ImageFormat.Tiff && zip) options.Add("TiffCompressionMethod", new BitmapTypedValue((byte)6, PropertyType.UInt8));
            using var ras = new InMemoryRandomAccessStream();
            var encoder = Wait(BitmapEncoder.CreateAsync(id, ras, options));
            using var sb = SoftwareBitmap.CreateCopyFromBuffer(p.D.AsBuffer(), BitmapPixelFormat.Bgra8, p.W, p.H, alpha);
            encoder.SetSoftwareBitmap(sb);
            if (metadata is { Count: > 0 })
            {
                var set = new BitmapPropertySet();
                foreach (var (k, v) in metadata) set.Add(k, v);
                Wait(encoder.BitmapProperties.SetPropertiesAsync(set));
            }
            Wait(encoder.FlushAsync());
            ras.Seek(0);
            var bytes = new byte[ras.Size];
            Wait(ras.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None));
            return bytes;
        }

        try { return Run(zip: true); }
        catch (Exception ex) when (format == ImageFormat.Tiff && ex is COMException or ArgumentException or InvalidOperationException) { return Run(zip: false); }
    }

    private static byte[] EncodeSafe(Pix p, string path, ImageFormat format, int quality, Dictionary<string, BitmapTypedValue>? metadata)
    {
        try { return Encode(p, format, quality, metadata); }
        catch (Exception ex) when (metadata is not null && ex is COMException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return Encode(p, format, quality, null);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw new ActionException($"Couldn't save the picture '{Path.GetFileName(path)}' as {format.ToString().ToUpperInvariant()}. Choose another picture type.", ex);
        }
    }

    private static ImageResult Save(Pix p, string path, ImageFormat format, int quality, Dictionary<string, BitmapTypedValue>? metadata, ImageFormat? sourceFormat)
    {
        if (format is ImageFormat.Jpeg or ImageFormat.Bmp or ImageFormat.Gif && HasAlpha(p)) p = Flatten(p);
        var useMetadata = metadata is not null && format == ImageFormat.Jpeg && sourceFormat == ImageFormat.Jpeg;
        var bytes = EncodeSafe(p, path, format, quality, useMetadata ? metadata : null);
        // Never replaces a file: a name taken since it was picked moves on to "name (2)" (NewFile removes only its own file on failure).
        try { path = NewFile.Write(path, s => s.Write(bytes)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ActionException($"Couldn't save the picture '{Path.GetFileName(path)}'. Check the folder exists and you can save there.", ex);
        }
        return new ImageResult(path, p.W, p.H);
    }

    private static ImageFormat TargetFormat(string dst, ImageFormat? fallback = null) =>
        ImageFormats.FromExtension(Path.GetExtension(dst)) ?? fallback ?? ImageFormat.Png;

    // ---- pixel operations --------------------------------------------------------------------

    private static (int[][] Index, float[][] Weight) Kernel(int srcLen, int dstLen)
    {
        var scale = (double)dstLen / srcLen;
        var r = Math.Max(1, 1 / scale);
        var idx = new int[dstLen][];
        var wts = new float[dstLen][];
        for (var o = 0; o < dstLen; o++)
        {
            var c = (o + 0.5) / scale - 0.5;
            var lo = (int)Math.Ceiling(c - r);
            var hi = (int)Math.Floor(c + r);
            var il = new List<int>();
            var wl = new List<float>();
            double sum = 0;
            for (var i = lo; i <= hi; i++)
            {
                var w = 1 - Math.Abs(i - c) / r;
                if (w <= 0) continue;
                il.Add(Math.Clamp(i, 0, srcLen - 1)); wl.Add((float)w); sum += w;
            }
            if (il.Count == 0) { il.Add(Math.Clamp((int)Math.Round(c), 0, srcLen - 1)); wl.Add(1); sum = 1; }
            idx[o] = il.ToArray();
            wts[o] = wl.Select(w => (float)(w / sum)).ToArray();
        }
        return (idx, wts);
    }

    private static Pix Scale(Pix s, int nw, int nh)
    {
        if (nw == s.W && nh == s.H) return s;
        var (xi, xw) = Kernel(s.W, nw);
        var (yi, yw) = Kernel(s.H, nh);
        var o = new Pix(nw, nh);
        var acc = new float[s.W * 4];
        for (var oy = 0; oy < nh; oy++)
        {
            Array.Clear(acc);
            for (var k = 0; k < yi[oy].Length; k++)
            {
                var wy = yw[oy][k];
                var row = yi[oy][k] * s.W * 4;
                for (var x = 0; x < s.W; x++)
                {
                    var i = row + x * 4;
                    var a = s.D[i + 3] / 255f;
                    var wa = wy * a;
                    acc[x * 4] += wa * s.D[i];
                    acc[x * 4 + 1] += wa * s.D[i + 1];
                    acc[x * 4 + 2] += wa * s.D[i + 2];
                    acc[x * 4 + 3] += wa;
                }
            }
            for (var ox = 0; ox < nw; ox++)
            {
                float b = 0, g = 0, r = 0, a = 0;
                for (var k = 0; k < xi[ox].Length; k++)
                {
                    var j = xi[ox][k] * 4; var w = xw[ox][k];
                    b += w * acc[j]; g += w * acc[j + 1]; r += w * acc[j + 2]; a += w * acc[j + 3];
                }
                var d = (oy * nw + ox) * 4;
                if (a > 0.0001f) { b /= a; g /= a; r /= a; }
                o.D[d] = (byte)Math.Clamp((int)MathF.Round(b), 0, 255);
                o.D[d + 1] = (byte)Math.Clamp((int)MathF.Round(g), 0, 255);
                o.D[d + 2] = (byte)Math.Clamp((int)MathF.Round(r), 0, 255);
                o.D[d + 3] = (byte)Math.Clamp((int)MathF.Round(a * 255), 0, 255);
            }
        }
        return o;
    }

    private static Pix Map(Pix s, int nw, int nh, Func<int, int, (int X, int Y)> where)
    {
        var o = new Pix(nw, nh);
        for (var y = 0; y < s.H; y++)
            for (var x = 0; x < s.W; x++)
            {
                var (nx, ny) = where(x, y);
                System.Buffer.BlockCopy(s.D, (y * s.W + x) * 4, o.D, (ny * nw + nx) * 4, 4);
            }
        return o;
    }

    private static Pix Rot(Pix s, int degrees) => degrees switch
    {
        90 => Map(s, s.H, s.W, (x, y) => (s.H - 1 - y, x)),
        180 => Map(s, s.W, s.H, (x, y) => (s.W - 1 - x, s.H - 1 - y)),
        270 => Map(s, s.H, s.W, (x, y) => (y, s.W - 1 - x)),
        _ => s,
    };

    private static Pix Flip(Pix s, bool horizontal) =>
        horizontal ? Map(s, s.W, s.H, (x, y) => (s.W - 1 - x, y)) : Map(s, s.W, s.H, (x, y) => (x, s.H - 1 - y));

    private static Pix CropTo(Pix s, int x, int y, int w, int h)
    {
        var o = new Pix(w, h);
        for (var r = 0; r < h; r++) System.Buffer.BlockCopy(s.D, ((y + r) * s.W + x) * 4, o.D, r * w * 4, w * 4);
        return o;
    }

    private static void Fill(Pix p, (byte A, byte R, byte G, byte B) c)
    {
        for (var i = 0; i < p.D.Length; i += 4) { p.D[i] = c.B; p.D[i + 1] = c.G; p.D[i + 2] = c.R; p.D[i + 3] = c.A; }
    }

    private static void Blit(Pix dst, Pix s, int dx, int dy)
    {
        for (var y = 0; y < s.H; y++)
        {
            var ty = dy + y;
            if (ty < 0 || ty >= dst.H) continue;
            for (var x = 0; x < s.W; x++)
            {
                var tx = dx + x;
                if (tx < 0 || tx >= dst.W) continue;
                var si = (y * s.W + x) * 4; var di = (ty * dst.W + tx) * 4;
                var sa = s.D[si + 3] / 255.0; var da = dst.D[di + 3] / 255.0;
                var oa = sa + da * (1 - sa);
                if (oa <= 0) continue;
                for (var c = 0; c < 3; c++)
                    dst.D[di + c] = (byte)Math.Clamp((int)Math.Round((s.D[si + c] * sa + dst.D[di + c] * da * (1 - sa)) / oa), 0, 255);
                dst.D[di + 3] = (byte)Math.Clamp((int)Math.Round(oa * 255), 0, 255);
            }
        }
    }

    // ---- operations --------------------------------------------------------------------------

    public ImageResult Resize(string src, string dst, ResizeSpec spec, int quality)
    {
        var img = Load(src);
        var (w, h) = ImageEngine.ComputeSize(img.Pixels.W, img.Pixels.H, spec);
        return Save(Scale(img.Pixels, w, h), dst, TargetFormat(dst), quality, img.Metadata, img.SourceFormat);
    }

    public ImageResult Convert(string src, string dst, ImageFormat format, int quality)
    {
        var img = Load(src);
        return Save(img.Pixels, dst, format, quality, img.Metadata, img.SourceFormat);
    }

    public ImageResult Crop(string src, string dst, CropSpec spec, int quality)
    {
        var img = Load(src);
        var (x, y, w, h) = ImageEngine.ComputeCrop(img.Pixels.W, img.Pixels.H, spec);
        return Save(CropTo(img.Pixels, x, y, w, h), dst, TargetFormat(dst), quality, img.Metadata, img.SourceFormat);
    }

    public ImageResult Rotate(string src, string dst, string op, int quality)
    {
        var img = Load(src);
        var s = img.Pixels;
        var result = op switch
        {
            "right" => Rot(s, 90),
            "left" => Rot(s, 270),
            "180" => Rot(s, 180),
            "flipH" => Flip(s, true),
            "flipV" => Flip(s, false),
            "upright" => s,
            _ => throw new ActionException($"Don't know the rotation '{op}'. Choose one from the list."),
        };
        return Save(result, dst, TargetFormat(dst), quality, img.Metadata, img.SourceFormat);
    }

    public ImageResult Strip(string src, string dst, int quality)
    {
        var img = Load(src, keepMetadata: false);
        return Save(img.Pixels, dst, TargetFormat(dst), quality, null, null);
    }

    private static (byte A, byte R, byte G, byte B) ParseColor(string text, (byte A, byte R, byte G, byte B) fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        var t = text.Trim();
        if (t.StartsWith('#') && uint.TryParse(t.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
        {
            switch (t.Length)
            {
                case 7: return (255, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
                case 9: return ((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
                case 4: return (255, (byte)(((v >> 8) & 0xF) * 17), (byte)(((v >> 4) & 0xF) * 17), (byte)((v & 0xF) * 17));
                case 5: return ((byte)(((v >> 12) & 0xF) * 17), (byte)(((v >> 8) & 0xF) * 17), (byte)(((v >> 4) & 0xF) * 17), (byte)((v & 0xF) * 17));
            }
        }
        var named = System.Drawing.Color.FromName(t);
        if (named.IsKnownColor) return (named.A, named.R, named.G, named.B);
        throw new ActionException($"Don't know the colour '{text}'. Use a name like red, or a code like #FF8800.");
    }

    public ImageResult AddText(string src, string dst, TextSpec spec, int quality)
    {
        if (string.IsNullOrEmpty(spec.Text)) throw new ActionException("Type the text to put on the picture.");
        var img = Load(src);
        var w = img.Pixels.W;
        var h = img.Pixels.H;
        var color = ParseColor(spec.Color, (255, 255, 255, 255));
        var em = (float)Math.Max(8, Math.Min(w, h) * Math.Clamp(spec.SizePercent, 1, 100) / 100.0);
        var margin = (float)Math.Max(4, Math.Min(w, h) * 0.03);
        var pos = spec.Position.ToLowerInvariant();
        var align = pos.Contains("left") ? StringAlignment.Near : pos.Contains("right") ? StringAlignment.Far : StringAlignment.Center;

        FontFamily family;
        try { family = new FontFamily(string.IsNullOrWhiteSpace(spec.Font) ? "Segoe UI" : spec.Font); }
        catch (ArgumentException) { family = new FontFamily("Segoe UI"); }
        var style = spec.Bold ? FontStyle.Bold : FontStyle.Regular;
        if (!family.IsStyleAvailable(style)) style = FontStyle.Regular;

        var result = new Pix(w, h);
        using (var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
        {
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try
            {
                var premul = new byte[img.Pixels.D.Length];
                for (var i = 0; i < premul.Length; i += 4)
                {
                    var a = img.Pixels.D[i + 3];
                    premul[i] = (byte)((img.Pixels.D[i] * a + 127) / 255);
                    premul[i + 1] = (byte)((img.Pixels.D[i + 1] * a + 127) / 255);
                    premul[i + 2] = (byte)((img.Pixels.D[i + 2] * a + 127) / 255);
                    premul[i + 3] = a;
                }
                Marshal.Copy(premul, 0, data.Scan0, premul.Length);
            }
            finally { bmp.UnlockBits(data); }

            using (var g = Graphics.FromImage(bmp))
            using (var font = new Font(family, em, style, GraphicsUnit.Pixel))
            using (var fmt = new StringFormat(StringFormat.GenericTypographic) { Alignment = align, FormatFlags = StringFormatFlags.NoClip })
            {
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var width = Math.Max(1, w - 2 * margin);
                var height = g.MeasureString(spec.Text, font, (int)width, fmt).Height;
                var y = pos.StartsWith("top") ? margin : pos.StartsWith("bottom") ? h - margin - height : (h - height) / 2;
                var rect = new RectangleF(margin, Math.Max(0, y), width, h);
                if (spec.Shadow)
                {
                    var off = Math.Max(1, em * 0.06f);
                    using var shadow = new SolidBrush(System.Drawing.Color.FromArgb(170, 0, 0, 0));
                    g.DrawString(spec.Text, font, shadow, new RectangleF(rect.X + off, rect.Y + off, rect.Width, rect.Height), fmt);
                }
                using var brush = new SolidBrush(System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B));
                g.DrawString(spec.Text, font, brush, rect, fmt);
            }

            var read = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try
            {
                var raw = new byte[w * h * 4];
                Marshal.Copy(read.Scan0, raw, 0, raw.Length);
                for (var i = 0; i < raw.Length; i += 4)
                {
                    var a = raw[i + 3];
                    result.D[i + 3] = a;
                    if (a == 0) continue;
                    result.D[i] = (byte)Math.Min(255, (raw[i] * 255 + a / 2) / a);
                    result.D[i + 1] = (byte)Math.Min(255, (raw[i + 1] * 255 + a / 2) / a);
                    result.D[i + 2] = (byte)Math.Min(255, (raw[i + 2] * 255 + a / 2) / a);
                }
            }
            finally { bmp.UnlockBits(read); }
        }
        return Save(result, dst, TargetFormat(dst), quality, img.Metadata, img.SourceFormat);
    }

    public ImageResult Combine(IReadOnlyList<string> sources, string dst, CombineSpec spec, int quality)
    {
        if (sources.Count == 0) throw new ActionException("Choose at least one picture to combine.");
        var images = sources.Select(s => Load(s, keepMetadata: false).Pixels).ToList();
        var spacing = (int)Math.Max(0, spec.Spacing);
        var background = ParseColor(spec.Background, (255, 255, 255, 255));
        var rects = new List<(double X, double Y, double W, double H)>();
        double totalW, totalH;

        switch (spec.Layout)
        {
            case "vertical":
                {
                    var maxW = images.Max(i => (double)i.W);
                    double y = 0;
                    foreach (var i in images)
                    {
                        var s = spec.MatchSize ? maxW / i.W : 1;
                        var iw = i.W * s; var ih = i.H * s;
                        rects.Add(((maxW - iw) / 2, y, iw, ih));
                        y += ih + spacing;
                    }
                    totalW = rects.Max(r => r.X + r.W); totalH = y - spacing;
                    break;
                }
            case "grid":
                {
                    var cols = Math.Clamp(spec.Columns, 1, images.Count);
                    var cellW = images.Max(i => (double)i.W);
                    var cellH = images.Max(i => (double)i.H);
                    for (var n = 0; n < images.Count; n++)
                    {
                        var i = images[n];
                        var s = spec.MatchSize ? Math.Min(cellW / i.W, cellH / i.H) : 1;
                        var iw = i.W * s; var ih = i.H * s;
                        var cx = (n % cols) * (cellW + spacing);
                        var cy = (n / cols) * (cellH + spacing);
                        rects.Add((cx + (cellW - iw) / 2, cy + (cellH - ih) / 2, iw, ih));
                    }
                    var rows = (images.Count + cols - 1) / cols;
                    totalW = cols * cellW + (cols - 1) * spacing; totalH = rows * cellH + (rows - 1) * spacing;
                    break;
                }
            default:
                {
                    var maxH = images.Max(i => (double)i.H);
                    double x = 0;
                    foreach (var i in images)
                    {
                        var s = spec.MatchSize ? maxH / i.H : 1;
                        var iw = i.W * s; var ih = i.H * s;
                        rects.Add((x, (maxH - ih) / 2, iw, ih));
                        x += iw + spacing;
                    }
                    totalW = x - spacing; totalH = rects.Max(r => r.Y + r.H);
                    break;
                }
        }
        var pw = (int)Math.Round(totalW);
        var ph = (int)Math.Round(totalH);
        if (pw < 1 || ph < 1 || pw > 20000 || ph > 20000)
            throw new ActionException("The combined picture would be too large. Use fewer or smaller pictures, or turn off \"Make all the same size\".");
        var canvas = new Pix(pw, ph);
        Fill(canvas, background);
        for (var n = 0; n < images.Count; n++)
        {
            var r = rects[n];
            var tw = (int)Math.Max(1, Math.Round(r.W)); var th = (int)Math.Max(1, Math.Round(r.H));
            Blit(canvas, Scale(images[n], tw, th), (int)Math.Round(r.X), (int)Math.Round(r.Y));
        }
        return Save(canvas, dst, TargetFormat(dst), quality, null, null);
    }

    public (byte[] Data, string MediaType) PrepareForAi(string path)
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
                var info = ReadInfo(path);
                if (Math.Max(info.Width, info.Height) <= MaxEdge && info.Orientation == 1) return (File.ReadAllBytes(path), direct);
            }
            catch { /* fall through and convert */ }
        }
        var src = Load(path, keepMetadata: false).Pixels;
        var edge = Math.Max(src.W, src.H);
        if (edge > MaxEdge)
        {
            var s = (double)MaxEdge / edge;
            src = Scale(src, Math.Max(1, (int)Math.Round(src.W * s)), Math.Max(1, (int)Math.Round(src.H * s)));
        }
        if (HasAlpha(src)) src = Flatten(src);
        return (EncodeSafe(src, path, ImageFormat.Jpeg, 85, null), "image/jpeg");
    }

    public (byte[] Data, int Width, int Height) ToJpegBytes(string path, int quality)
    {
        var src = Load(path, keepMetadata: false).Pixels;
        if (HasAlpha(src)) src = Flatten(src);
        return (EncodeSafe(src, path, ImageFormat.Jpeg, quality, null), src.W, src.H);
    }

    // ---- metadata ----------------------------------------------------------------------------

    private static string? Text(object? o) => (o as string)?.Trim('\0', ' ') is { Length: > 0 } s ? s : null;

    private static double? Degrees(object? value)
    {
        if (value is not System.Collections.IEnumerable seq || value is string) return null;
        var parts = new List<double>();
        foreach (var o in seq)
        {
            ulong packed;
            try { packed = System.Convert.ToUInt64(o, CultureInfo.InvariantCulture); } catch { return null; }
            var num = (uint)(packed & 0xFFFFFFFF);
            var den = (uint)(packed >> 32);
            parts.Add(den == 0 ? 0 : (double)num / den);
        }
        if (parts.Count < 3) return null;
        return parts[0] + parts[1] / 60.0 + parts[2] / 3600.0;
    }

    private static DateTime? ReadDateTaken(BitmapDecoder decoder)
    {
        foreach (var tag in new[] { 36867, 36868 })
        {
            var s = Query(decoder, $"/app1/ifd/exif/{{ushort={tag}}}", $"/ifd/exif/{{ushort={tag}}}") as string;
            if (ImageMetadataReader.ParseExifDate(s) is { } d) return d;
        }
        return Query(decoder, "System.Photo.DateTaken") switch
        {
            DateTimeOffset o when o.Year > 1900 => o.DateTime,
            DateTime t when t.Year > 1900 => t,
            string s => ImageMetadataReader.ParseExifDate(s),
            _ => null,
        };
    }

    public ImageInfo ReadInfo(string path)
    {
        var decoder = OpenDecoder(path, out var stream);
        using (stream)
        {
            var orientation = ReadOrientation(decoder);
            int w = (int)decoder.PixelWidth, h = (int)decoder.PixelHeight;
            if (orientation >= 5) (w, h) = (h, w);
            var make = Text(Query(decoder, "/app1/ifd/{ushort=271}", "/ifd/{ushort=271}"));
            var model = Text(Query(decoder, "/app1/ifd/{ushort=272}", "/ifd/{ushort=272}"));
            double? lat = Degrees(Query(decoder, "/app1/ifd/gps/{ushort=2}", "/ifd/gps/{ushort=2}"));
            double? lon = Degrees(Query(decoder, "/app1/ifd/gps/{ushort=4}", "/ifd/gps/{ushort=4}"));
            if (lat is null || lon is null) { lat = null; lon = null; }
            else
            {
                var latRef = Query(decoder, "/app1/ifd/gps/{ushort=1}", "/ifd/gps/{ushort=1}") as string;
                var lonRef = Query(decoder, "/app1/ifd/gps/{ushort=3}", "/ifd/gps/{ushort=3}") as string;
                if (latRef?.StartsWith("S", StringComparison.OrdinalIgnoreCase) == true) lat = -lat;
                if (lonRef?.StartsWith("W", StringComparison.OrdinalIgnoreCase) == true) lon = -lon;
            }
            return new ImageInfo(w, h, ReadDateTaken(decoder), make, model, lat, lon, orientation);
        }
    }
}
