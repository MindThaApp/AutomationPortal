using System.Windows.Media.Imaging;
using Flowmate.Core;

namespace Flowmate.Actions.Documents.Tests;

/// <summary>The same behaviour checks against the WPF and the WinRT picture backends, called directly (the process-wide backend is not touched).</summary>
public class ImageBackendParityTests
{
    public static IEnumerable<object[]> Backends => new[] { new object[] { "wpf" }, new object[] { "winrt" } };

    private static IImageBackend Make(string name) => name == "wpf" ? new Flowmate.Host.Wpf.WpfImageBackend() : new WinRtImageBackend();

    private static Task<TR> OnSta<TR>(Func<TR> f) => StaWorker.Imaging.RunAsync(f);

    private static ResizeSpec ByWidth(double w, bool never = false) => new("width", w, 0, 0, 0, never);

    [Theory, MemberData(nameof(Backends))]
    public async Task Resize_keeps_shape_and_pixels(string name)
    {
        using var d = new TempDir();
        var src = await T.MakeImage(d.File("a.jpg"), 400, 200);
        var dst = d.File("out.png");
        var r = await OnSta(() => Make(name).Resize(src, dst, ByWidth(100), 90));
        Assert.Equal((100, 50), (r.Width, r.Height));
        Assert.True(T.IsRed(await T.PixelAt(dst, 10, 25)));
        Assert.True(T.IsBlue(await T.PixelAt(dst, 90, 25)));
        Assert.Equal((100, 50), ((await OnSta(() => Make(name).ReadInfo(dst))).Width, (await OnSta(() => Make(name).ReadInfo(dst))).Height));
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task Resize_keeps_date_taken_for_jpegs(string name)
    {
        using var d = new TempDir();
        var src = await T.MakeImage(d.File("a.jpg"), 400, 200, md =>
        {
            md.SetQuery("/app1/ifd/exif/{ushort=36867}", "2019:07:08 09:10:11");
            md.SetQuery("/app1/ifd/{ushort=272}", "Snapper 3");
        });
        var dst = d.File("out.jpg");
        await OnSta(() => Make(name).Resize(src, dst, ByWidth(100), 90));
        var info = await OnSta(() => Make(name).ReadInfo(dst));
        Assert.Equal(new DateTime(2019, 7, 8, 9, 10, 11), info.DateTaken);
        Assert.Equal("Snapper 3", info.CameraModel);
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task Convert_between_all_formats(string name)
    {
        using var d = new TempDir();
        var src = await T.MakeImage(d.File("a.png"), 60, 30);
        foreach (var (format, ext) in new[] { (ImageFormat.Jpeg, ".jpg"), (ImageFormat.Bmp, ".bmp"), (ImageFormat.Gif, ".gif"), (ImageFormat.Tiff, ".tiff"), (ImageFormat.Png, ".png") })
        {
            var dst = d.File("c" + ext);
            var r = await OnSta(() => Make(name).Convert(src, dst, format, 90));
            Assert.Equal((60, 30), (r.Width, r.Height));
            var info = await OnSta(() => Make(name).ReadInfo(dst));
            Assert.Equal((60, 30), (info.Width, info.Height));
            var b = File.ReadAllBytes(dst);
            Assert.True(format switch
            {
                ImageFormat.Jpeg => b[0] == 0xFF && b[1] == 0xD8,
                ImageFormat.Png => b[1] == (byte)'P' && b[2] == (byte)'N',
                ImageFormat.Bmp => b[0] == (byte)'B' && b[1] == (byte)'M',
                ImageFormat.Gif => b[0] == (byte)'G' && b[1] == (byte)'I',
                _ => (b[0] == (byte)'I' && b[1] == (byte)'I') || (b[0] == (byte)'M' && b[1] == (byte)'M'),
            }, $"{format} header");
            Assert.True(T.IsRed(await T.PixelAt(dst, 5, 15)), $"{format} red");
            Assert.True(T.IsBlue(await T.PixelAt(dst, 55, 15)), $"{format} blue");
        }
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task Jpeg_quality_changes_file_size(string name)
    {
        using var d = new TempDir();
        var src = d.File("noise.png");
        await StaWorker.Imaging.RunAsync(() =>
        {
            var data = new byte[200 * 200 * 3]; new Random(1).NextBytes(data);
            var bmp = BitmapSource.Create(200, 200, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null, data, 600);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp)); using var fs = File.Create(src); enc.Save(fs);
        });
        var lo = d.File("lo.jpg"); var hi = d.File("hi.jpg");
        await OnSta(() => Make(name).Convert(src, lo, ImageFormat.Jpeg, 10));
        await OnSta(() => Make(name).Convert(src, hi, ImageFormat.Jpeg, 95));
        Assert.True(new FileInfo(lo).Length < new FileInfo(hi).Length);
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task Transparent_png_becomes_white_as_jpeg(string name)
    {
        using var d = new TempDir();
        var src = d.File("clear.png");
        await StaWorker.Imaging.RunAsync(() =>
        {
            var bmp = BitmapSource.Create(10, 10, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[400], 40);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp)); using var fs = File.Create(src); enc.Save(fs);
        });
        var dst = d.File("clear.jpg");
        await OnSta(() => Make(name).Convert(src, dst, ImageFormat.Jpeg, 90));
        var c = await T.PixelAt(dst, 5, 5);
        Assert.True(c.R > 240 && c.G > 240 && c.B > 240, c.ToString());
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task Crop_by_ratio_and_box(string name)
    {
        using var d = new TempDir();
        var src = await T.MakeImage(d.File("a.png"), 400, 200);
        var left = d.File("l.png"); var right = d.File("r.png"); var box = d.File("b.png");
        var l = await OnSta(() => Make(name).Crop(src, left, new CropSpec("ratio", 0, 0, 0, 0, "1:1", "left"), 90));
        Assert.Equal((200, 200), (l.Width, l.Height));
        Assert.True(T.IsRed(await T.PixelAt(left, 190, 100)));
        await OnSta(() => Make(name).Crop(src, right, new CropSpec("ratio", 0, 0, 0, 0, "1:1", "right"), 90));
        Assert.True(T.IsBlue(await T.PixelAt(right, 10, 100)));
        var b = await OnSta(() => Make(name).Crop(src, box, new CropSpec("box", 350, 10, 100, 20, "", ""), 90));
        Assert.Equal((50, 20), (b.Width, b.Height));
        Assert.True(T.IsBlue(await T.PixelAt(box, 10, 10)));
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task Rotate_and_flip(string name)
    {
        using var d = new TempDir();
        var src = await T.MakeImage(d.File("a.png"), 100, 40);
        async Task<(ImageResult R, string Path)> Do(string op)
        {
            var dst = d.File(op + ".png");
            return (await OnSta(() => Make(name).Rotate(src, dst, op, 90)), dst);
        }
        var right = await Do("right");
        Assert.Equal((40, 100), (right.R.Width, right.R.Height));
        Assert.True(T.IsRed(await T.PixelAt(right.Path, 20, 10)));
        Assert.True(T.IsBlue(await T.PixelAt(right.Path, 20, 90)));
        Assert.True(T.IsRed(await T.PixelAt((await Do("left")).Path, 20, 90)));
        Assert.True(T.IsBlue(await T.PixelAt((await Do("180")).Path, 10, 20)));
        Assert.True(T.IsBlue(await T.PixelAt((await Do("flipH")).Path, 10, 20)));
        Assert.True(T.IsRed(await T.PixelAt((await Do("flipV")).Path, 10, 20)));
        await Assert.ThrowsAsync<ActionException>(() => Do("sideways"));
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task Exif_orientation_is_respected(string name)
    {
        using var d = new TempDir();
        var src = await T.MakeImage(d.File("phone.jpg"), 100, 40, md => md.SetQuery("/app1/ifd/{ushort=274}", (ushort)6));
        var info = await OnSta(() => Make(name).ReadInfo(src));
        Assert.Equal((40, 100, 6), (info.Width, info.Height, info.Orientation));
        var up = d.File("up.jpg");
        var r = await OnSta(() => Make(name).Rotate(src, up, "upright", 90));
        Assert.Equal((40, 100), (r.Width, r.Height));
        Assert.True(T.IsRed(await T.PixelAt(up, 20, 10)));
        Assert.Equal(1, (await OnSta(() => Make(name).ReadInfo(up))).Orientation);
        var small = d.File("small.jpg");
        var s = await OnSta(() => Make(name).Resize(src, small, ByWidth(20), 90));
        Assert.Equal((20, 50), (s.Width, s.Height));
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task Combine_layouts_and_background(string name)
    {
        using var d = new TempDir();
        var a = await T.MakeImage(d.File("a.png"), 100, 50);
        var b = await T.MakeImage(d.File("b.png"), 100, 50);
        var c = await T.MakeImage(d.File("c.png"), 100, 50);
        var all = new[] { a, b, c };
        var h = await OnSta(() => Make(name).Combine(all, d.File("h.png"), new CombineSpec("horizontal", 2, 10, "white", true), 90));
        Assert.Equal((320, 50), (h.Width, h.Height));
        var v = await OnSta(() => Make(name).Combine(all, d.File("v.png"), new CombineSpec("vertical", 2, 10, "white", true), 90));
        Assert.Equal((100, 170), (v.Width, v.Height));
        var g = await OnSta(() => Make(name).Combine(all, d.File("g.png"), new CombineSpec("grid", 2, 5, "white", true), 90));
        Assert.Equal((205, 105), (g.Width, g.Height));
        Assert.True(T.IsRed(await T.PixelAt(g.Path, 10, 25)));
        var big = await T.MakeImage(d.File("big.png"), 100, 100);
        var small = await T.MakeImage(d.File("small.png"), 50, 50);
        var natural = await OnSta(() => Make(name).Combine(new[] { big, small }, d.File("n.png"), new CombineSpec("horizontal", 2, 0, "#00FF00", false), 90));
        Assert.Equal((150, 100), (natural.Width, natural.Height));
        var green = await T.PixelAt(natural.Path, 125, 5);
        Assert.True(green.G > 200 && green.R < 60, green.ToString());
        var scaled = await OnSta(() => Make(name).Combine(new[] { big, small }, d.File("s.png"), new CombineSpec("horizontal", 2, 0, "white", true), 90));
        Assert.Equal((200, 100), (scaled.Width, scaled.Height));
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task Strip_removes_photo_info(string name)
    {
        using var d = new TempDir();
        var src = await T.MakeImage(d.File("a.jpg"), 80, 40, md =>
        {
            md.SetQuery("/app1/ifd/exif/{ushort=36867}", "2019:07:08 09:10:11");
            md.SetQuery("/app1/ifd/{ushort=271}", "ACME");
        });
        Assert.NotNull((await OnSta(() => Make(name).ReadInfo(src))).DateTaken);
        var dst = d.File("clean.jpg");
        await OnSta(() => Make(name).Strip(src, dst, 95));
        var info = await OnSta(() => Make(name).ReadInfo(dst));
        Assert.Null(info.DateTaken);
        Assert.Null(info.CameraMake);
        Assert.Equal((80, 40), (info.Width, info.Height));
        Assert.True(T.IsRed(await T.PixelAt(dst, 10, 20)));
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task Add_text_draws_where_asked(string name)
    {
        using var d = new TempDir();
        var src = d.File("blank.png");
        await StaWorker.Imaging.RunAsync(() =>
        {
            var data = new byte[300 * 200 * 3];
            var bmp = BitmapSource.Create(300, 200, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null, data, 900);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp)); using var fs = File.Create(src); enc.Save(fs);
        });
        async Task<(bool Top, bool Bottom)> Where(string pos)
        {
            var dst = d.File(pos + ".png");
            var r = await OnSta(() => Make(name).AddText(src, dst, new TextSpec("WWW", pos, 15, "#FFFFFF", false, true, ""), 90));
            Assert.Equal((300, 200), (r.Width, r.Height));
            var top = false; var bottom = false;
            for (var y = 0; y < 200; y += 2)
                for (var x = 0; x < 300; x += 2)
                {
                    if ((await T.PixelAt(dst, x, y)).R <= 200) continue;
                    if (y < 60) top = true; else if (y >= 140) bottom = true;
                }
            return (top, bottom);
        }
        Assert.Equal((true, false), await Where("topLeft"));
        Assert.Equal((false, true), await Where("bottomRight"));
        await Assert.ThrowsAsync<ActionException>(() => OnSta(() => Make(name).AddText(src, d.File("x.png"), new TextSpec("hi", "center", 15, "notacolour", false, false, ""), 90)));
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task Info_reads_date_camera_and_location(string name)
    {
        using var d = new TempDir();
        static ulong Rational(uint num, uint den) => ((ulong)den << 32) | num;
        var src = await T.MakeImage(d.File("trip.jpg"), 120, 80, md =>
        {
            md.SetQuery("/app1/ifd/exif/{ushort=36867}", "2018:12:25 08:30:00");
            md.SetQuery("/app1/ifd/{ushort=271}", "Canon");
            md.SetQuery("/app1/ifd/{ushort=272}", "Canon EOS R5");
            md.SetQuery("/app1/ifd/gps/{ushort=1}", "N");
            md.SetQuery("/app1/ifd/gps/{ushort=3}", "W");
            md.SetQuery("/app1/ifd/gps/{ushort=2}", new ulong[] { Rational(48, 1), Rational(51, 1), Rational(2940, 100) });
            md.SetQuery("/app1/ifd/gps/{ushort=4}", new ulong[] { Rational(2, 1), Rational(21, 1), Rational(0, 1) });
        });
        var info = await OnSta(() => Make(name).ReadInfo(src));
        Assert.Equal((120, 80), (info.Width, info.Height));
        Assert.Equal(new DateTime(2018, 12, 25, 8, 30, 0), info.DateTaken);
        Assert.Equal(("Canon", "Canon EOS R5"), (info.CameraMake, info.CameraModel));
        Assert.Equal(48.858, info.Latitude!.Value, 3);
        Assert.Equal(-2.35, info.Longitude!.Value, 3);
        var plain = await T.MakeImage(d.File("plain.png"), 10, 10);
        var p = await OnSta(() => Make(name).ReadInfo(plain));
        Assert.Null(p.DateTaken); Assert.Null(p.Latitude);
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task Bad_and_missing_files_give_friendly_errors(string name)
    {
        using var d = new TempDir();
        var fake = d.File("broken.jpg", "this is not a jpeg");
        var ex = await Assert.ThrowsAsync<ActionException>(() => OnSta(() => Make(name).Resize(fake, d.File("o.jpg"), ByWidth(10), 90)));
        Assert.Contains("Couldn't open 'broken.jpg'", ex.Message);
        var ex2 = await Assert.ThrowsAsync<ActionException>(() => OnSta(() => Make(name).Resize(Path.Combine(d.Path, "missing.jpg"), d.File("o.jpg"), ByWidth(10), 90)));
        Assert.Contains("Couldn't find", ex2.Message);
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task Prepare_for_ai_and_jpeg_bytes(string name)
    {
        using var d = new TempDir();
        var big = await T.MakeImage(d.File("big.png"), 3000, 1500);
        var (data, media) = await OnSta(() => Make(name).PrepareForAi(big));
        Assert.Equal("image/jpeg", media);
        using var ms = new MemoryStream(data);
        Assert.Equal(2000, await StaWorker.Imaging.RunAsync(() => BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0].PixelWidth));
        var bmp = await T.MakeImage(d.File("x.bmp"), 50, 50);
        Assert.Equal("image/jpeg", (await OnSta(() => Make(name).PrepareForAi(bmp))).MediaType);
        var small = await T.MakeImage(d.File("s.png"), 50, 50);
        Assert.Equal("image/png", (await OnSta(() => Make(name).PrepareForAi(small))).MediaType);
        var (bytes, w, h) = await OnSta(() => Make(name).ToJpegBytes(small, 92));
        Assert.Equal((50, 50), (w, h));
        Assert.True(bytes[0] == 0xFF && bytes[1] == 0xD8);
    }
}
