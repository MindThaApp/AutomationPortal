using System.Windows.Media.Imaging;
using AutomationPortal.Core;

namespace AutomationPortal.Actions.Documents.Tests;

public class ImageTests
{
    private static ImageItem First(ContentValue v) => Assert.IsType<ImageItem>(v[0]);

    [Fact]
    public async Task ResizeByWidthKeepsShapeAndSavesNextToOriginal()
    {
        using var d = new TempDir(); using var host = T.Host();
        var src = await T.MakeImage(d.File("a.jpg"), 400, 200);
        var r = await T.Run(host, new ResizeImagesAction(), ContentValue.Images(new[] { src }), ("by", T.Choice("width")), ("width", 100));
        var img = First(r);
        Assert.Equal(Path.Combine(d.Path, "a-resized.jpg"), img.Path);
        Assert.Equal((100, 50), (img.Width, img.Height));
        var info = await T.Info(img.Path);
        Assert.Equal((100, 50), (info.Width, info.Height));
        Assert.True(File.Exists(src));
        Assert.Equal((400, 200), ((await T.Info(src)).Width, (await T.Info(src)).Height));
    }

    [Theory]
    [InlineData("height", "height", 50, 100, 50)]
    [InlineData("longest", "longest", 100, 100, 50)]
    [InlineData("percent", "percent", 25, 100, 50)]
    public async Task ResizeModes(string by, string key, double value, int w, int h)
    {
        using var d = new TempDir(); using var host = T.Host();
        var src = await T.MakeImage(d.File("a.png"), 400, 200);
        var r = await T.Run(host, new ResizeImagesAction(), ContentValue.Images(new[] { src }), ("by", T.Choice(by)), (key, value));
        Assert.Equal((w, h), (First(r).Width, First(r).Height));
    }

    [Fact]
    public async Task NeverEnlargeKeepsSmallPicturesAsTheyAre()
    {
        using var d = new TempDir(); using var host = T.Host();
        var src = await T.MakeImage(d.File("a.png"), 100, 50);
        var kept = await T.Run(host, new ResizeImagesAction(), ContentValue.Images(new[] { src }), ("by", T.Choice("width")), ("width", 400), ("neverEnlarge", true));
        Assert.Equal(100, First(kept).Width);
        var grown = await T.Run(host, new ResizeImagesAction(), ContentValue.Images(new[] { src }), ("by", T.Choice("width")), ("width", 400), ("neverEnlarge", false));
        Assert.Equal((400, 200), (First(grown).Width, First(grown).Height));
    }

    [Fact]
    public async Task ResizeTwiceNeverOverwritesAndTempModeUsesRunFolder()
    {
        using var d = new TempDir(); using var host = T.Host();
        var src = await T.MakeImage(d.File("a.jpg"), 40, 20);
        var a = await T.Run(host, new ResizeImagesAction(), ContentValue.Images(new[] { src }), ("by", T.Choice("percent")), ("percent", 50));
        var b = await T.Run(host, new ResizeImagesAction(), ContentValue.Images(new[] { src }), ("by", T.Choice("percent")), ("percent", 50));
        Assert.Equal("a-resized.jpg", Path.GetFileName(First(a).Path));
        Assert.Equal("a-resized (2).jpg", Path.GetFileName(First(b).Path));
        var t = await T.Run(host, new ResizeImagesAction(), ContentValue.Images(new[] { src }), ("by", T.Choice("percent")), ("percent", 50), ("saveTo", T.Choice("temp")));
        Assert.StartsWith(host.Root, First(t).Path, StringComparison.OrdinalIgnoreCase);
        var folder = d.Folder("out");
        var f = await T.Run(host, new ResizeImagesAction(), ContentValue.Images(new[] { src }), ("by", T.Choice("percent")), ("percent", 50), ("saveTo", T.Choice("folder")), ("saveFolder", ParamValue.File(folder)));
        Assert.Equal(folder, Path.GetDirectoryName(First(f).Path));
    }

    [Fact]
    public async Task ResizeKeepsPhotoInfoForJpegs()
    {
        using var d = new TempDir(); using var host = T.Host();
        var src = await T.MakeImage(d.File("a.jpg"), 400, 200, md => md.SetQuery("/app1/ifd/exif/{ushort=36867}", "2019:07:08 09:10:11"));
        var r = await T.Run(host, new ResizeImagesAction(), ContentValue.Images(new[] { src }), ("by", T.Choice("width")), ("width", 100));
        Assert.Equal(new DateTime(2019, 7, 8, 9, 10, 11), (await T.Info(First(r).Path)).DateTaken);
    }

    [Fact]
    public async Task ConvertBetweenAllFormats()
    {
        using var d = new TempDir(); using var host = T.Host();
        var src = await T.MakeImage(d.File("a.png"), 60, 30);
        foreach (var (id, ext) in new[] { ("jpeg", ".jpg"), ("bmp", ".bmp"), ("gif", ".gif"), ("tiff", ".tiff"), ("png", ".png") })
        {
            var r = await T.Run(host, new ConvertImagesAction(), ContentValue.Images(new[] { src }), ("format", T.Choice(id)), ("suffix", "-" + id));
            var path = First(r).Path;
            Assert.EndsWith(ext, path);
            var info = await T.Info(path);
            Assert.Equal((60, 30), (info.Width, info.Height));
            var bytes = File.ReadAllBytes(path);
            Assert.True(id switch
            {
                "jpeg" => bytes[0] == 0xFF && bytes[1] == 0xD8,
                "png" => bytes[1] == (byte)'P' && bytes[2] == (byte)'N',
                "bmp" => bytes[0] == (byte)'B' && bytes[1] == (byte)'M',
                "gif" => bytes[0] == (byte)'G' && bytes[1] == (byte)'I',
                _ => (bytes[0] == (byte)'I' && bytes[1] == (byte)'I') || (bytes[0] == (byte)'M' && bytes[1] == (byte)'M'),
            }, $"{id} header");
        }
    }

    [Fact]
    public async Task JpegQualityChangesFileSize()
    {
        using var d = new TempDir(); using var host = T.Host();
        // noisy picture so quality matters
        var src = d.File("noise.png");
        await StaWorker.Imaging.RunAsync(() =>
        {
            var rnd = new Random(1); var data = new byte[200 * 200 * 3]; rnd.NextBytes(data);
            var bmp = BitmapSource.Create(200, 200, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null, data, 600);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp)); using var fs = File.Create(src); enc.Save(fs);
        });
        var lo = await T.Run(host, new ConvertImagesAction(), ContentValue.Images(new[] { src }), ("format", T.Choice("jpeg")), ("quality", 10), ("suffix", "-lo"));
        var hi = await T.Run(host, new ConvertImagesAction(), ContentValue.Images(new[] { src }), ("format", T.Choice("jpeg")), ("quality", 95), ("suffix", "-hi"));
        Assert.True(new FileInfo(First(lo).Path).Length < new FileInfo(First(hi).Path).Length);
    }

    [Fact]
    public async Task TransparentPngBecomesWhiteWhenConvertedToJpeg()
    {
        using var d = new TempDir(); using var host = T.Host();
        var src = d.File("clear.png");
        await StaWorker.Imaging.RunAsync(() =>
        {
            var data = new byte[10 * 10 * 4]; // fully transparent
            var bmp = BitmapSource.Create(10, 10, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, data, 40);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp)); using var fs = File.Create(src); enc.Save(fs);
        });
        var r = await T.Run(host, new ConvertImagesAction(), ContentValue.Images(new[] { src }), ("format", T.Choice("jpeg")));
        var c = await T.PixelAt(First(r).Path, 5, 5);
        Assert.True(c.R > 240 && c.G > 240 && c.B > 240, c.ToString());
    }

    [Theory]
    [InlineData("1:1", "center", 200, 200)]
    [InlineData("16:9", "center", 356, 200)]
    [InlineData("4:3", "center", 267, 200)]
    [InlineData("9:16", "center", 112, 200)]
    public async Task CropByRatio(string shape, string anchor, int w, int h)
    {
        using var d = new TempDir(); using var host = T.Host();
        var src = await T.MakeImage(d.File("a.png"), 400, 200);
        var r = await T.Run(host, new CropImagesAction(), ContentValue.Images(new[] { src }), ("shape", T.Choice(shape)), ("anchor", T.Choice(anchor)));
        Assert.Equal((w, h), (First(r).Width, First(r).Height));
    }

    [Fact]
    public void CropAnchorsPickTheRightCorner()
    {
        Assert.Equal((0, 0, 200, 200), ImageEngine.ComputeCrop(400, 200, new CropSpec("ratio", 0, 0, 0, 0, "1:1", "left")));
        Assert.Equal((200, 0, 200, 200), ImageEngine.ComputeCrop(400, 200, new CropSpec("ratio", 0, 0, 0, 0, "1:1", "right")));
        Assert.Equal((100, 0, 200, 200), ImageEngine.ComputeCrop(400, 200, new CropSpec("ratio", 0, 0, 0, 0, "1:1", "center")));
        Assert.Equal((0, 0, 100, 200), ImageEngine.ComputeCrop(100, 400, new CropSpec("ratio", 0, 0, 0, 0, "1:2", "top")));
        Assert.Equal((0, 200, 100, 200), ImageEngine.ComputeCrop(100, 400, new CropSpec("ratio", 0, 0, 0, 0, "1:2", "bottomLeft")));
    }

    [Fact]
    public async Task CropAnchorLeftKeepsTheRedHalf()
    {
        using var d = new TempDir(); using var host = T.Host();
        var src = await T.MakeImage(d.File("a.png"), 400, 200);
        var r = await T.Run(host, new CropImagesAction(), ContentValue.Images(new[] { src }), ("shape", T.Choice("1:1")), ("anchor", T.Choice("left")));
        Assert.True(T.IsRed(await T.PixelAt(First(r).Path, 10, 100)));
        Assert.True(T.IsRed(await T.PixelAt(First(r).Path, 190, 100)));
        var right = await T.Run(host, new CropImagesAction(), ContentValue.Images(new[] { src }), ("shape", T.Choice("1:1")), ("anchor", T.Choice("right")), ("suffix", "-r"));
        Assert.True(T.IsBlue(await T.PixelAt(First(right).Path, 10, 100)));
    }

    [Fact]
    public async Task CropByBoxClampsAndRejectsOutside()
    {
        using var d = new TempDir(); using var host = T.Host();
        var src = await T.MakeImage(d.File("a.png"), 100, 100);
        var r = await T.Run(host, new CropImagesAction(), ContentValue.Images(new[] { src }), ("shape", T.Choice("box")), ("x", 80), ("y", 10), ("width", 50), ("height", 20));
        Assert.Equal((20, 20), (First(r).Width, First(r).Height));
        var ex = await Assert.ThrowsAsync<ActionException>(() => T.Run(host, new CropImagesAction(), ContentValue.Images(new[] { src }), ("shape", T.Choice("box")), ("x", 500)));
        Assert.Contains("outside the picture", ex.Message);
    }

    [Fact]
    public async Task RotateAndFlip()
    {
        using var d = new TempDir(); using var host = T.Host();
        var src = await T.MakeImage(d.File("a.png"), 100, 40);
        var right = First(await T.Run(host, new RotateImagesAction(), ContentValue.Images(new[] { src }), ("action", T.Choice("right"))));
        Assert.Equal((40, 100), (right.Width, right.Height));
        Assert.True(T.IsRed(await T.PixelAt(right.Path, 20, 10)));     // red (left) goes to the top
        Assert.True(T.IsBlue(await T.PixelAt(right.Path, 20, 90)));
        var left = First(await T.Run(host, new RotateImagesAction(), ContentValue.Images(new[] { src }), ("action", T.Choice("left"))));
        Assert.True(T.IsRed(await T.PixelAt(left.Path, 20, 90)));
        var r180 = First(await T.Run(host, new RotateImagesAction(), ContentValue.Images(new[] { src }), ("action", T.Choice("180"))));
        Assert.True(T.IsBlue(await T.PixelAt(r180.Path, 10, 20)));
        var flipH = First(await T.Run(host, new RotateImagesAction(), ContentValue.Images(new[] { src }), ("action", T.Choice("flipH"))));
        Assert.Equal((100, 40), (flipH.Width, flipH.Height));
        Assert.True(T.IsBlue(await T.PixelAt(flipH.Path, 10, 20)));
        var flipV = First(await T.Run(host, new RotateImagesAction(), ContentValue.Images(new[] { src }), ("action", T.Choice("flipV"))));
        Assert.True(T.IsRed(await T.PixelAt(flipV.Path, 10, 20)));
    }

    [Fact]
    public async Task ExifOrientationIsRespected()
    {
        using var d = new TempDir(); using var host = T.Host();
        // stored 100x40 but tagged "rotate 90 clockwise" (6): displays as 40x100 with red on top
        var src = await T.MakeImage(d.File("phone.jpg"), 100, 40, md => md.SetQuery("/app1/ifd/{ushort=274}", (ushort)6));
        var info = await T.Info(src);
        Assert.Equal((40, 100), (info.Width, info.Height));
        var upright = First(await T.Run(host, new RotateImagesAction(), ContentValue.Images(new[] { src }), ("action", T.Choice("upright"))));
        Assert.Equal((40, 100), (upright.Width, upright.Height));
        Assert.True(T.IsRed(await T.PixelAt(upright.Path, 20, 10)));
        Assert.Equal(1, (await T.Info(upright.Path)).Orientation);
        var resized = First(await T.Run(host, new ResizeImagesAction(), ContentValue.Images(new[] { src }), ("by", T.Choice("width")), ("width", 20)));
        Assert.Equal((20, 50), (resized.Width, resized.Height));
        Assert.Equal(1, (await T.Info(resized.Path)).Orientation);
    }

    [Fact]
    public async Task CombineLayoutsAndSpacing()
    {
        using var d = new TempDir(); using var host = T.Host();
        var a = await T.MakeImage(d.File("a.png"), 100, 50);
        var b = await T.MakeImage(d.File("b.png"), 100, 50);
        var c = await T.MakeImage(d.File("c.png"), 100, 50);
        var all = ContentValue.Images(new[] { a, b, c });
        var h = First(await T.Run(host, new CombineImagesAction(), all, ("layout", T.Choice("horizontal")), ("spacing", 10)));
        Assert.Equal((320, 50), (h.Width, h.Height));
        var v = First(await T.Run(host, new CombineImagesAction(), all, ("layout", T.Choice("vertical")), ("spacing", 10), ("name", "Stack")));
        Assert.Equal((100, 170), (v.Width, v.Height));
        Assert.Equal("Stack.png", Path.GetFileName(v.Path));
        var g = First(await T.Run(host, new CombineImagesAction(), all, ("layout", T.Choice("grid")), ("columns", 2), ("spacing", 5), ("name", "Grid")));
        Assert.Equal((205, 105), (g.Width, g.Height));
        Assert.True(T.IsRed(await T.PixelAt(g.Path, 10, 25)));
    }

    [Fact]
    public async Task CombineBackgroundFillsGapsAndMatchSizeScales()
    {
        using var d = new TempDir(); using var host = T.Host();
        var a = await T.MakeImage(d.File("a.png"), 100, 100);
        var b = await T.MakeImage(d.File("b.png"), 50, 50);
        var all = ContentValue.Images(new[] { a, b });
        var natural = First(await T.Run(host, new CombineImagesAction(), all, ("layout", T.Choice("horizontal")), ("matchSize", false), ("background", "#00FF00"), ("name", "N")));
        Assert.Equal((150, 100), (natural.Width, natural.Height));
        var green = await T.PixelAt(natural.Path, 125, 5);
        Assert.True(green.G > 200 && green.R < 60, green.ToString());
        var scaled = First(await T.Run(host, new CombineImagesAction(), all, ("layout", T.Choice("horizontal")), ("matchSize", true), ("name", "S")));
        Assert.Equal((200, 100), (scaled.Width, scaled.Height));
    }

    [Fact]
    public async Task StripRemovesLocationCameraAndDateButKeepsPicture()
    {
        using var d = new TempDir(); using var host = T.Host();
        var src = await T.MakeImage(d.File("a.jpg"), 80, 40, md =>
        {
            md.SetQuery("/app1/ifd/exif/{ushort=36867}", "2019:07:08 09:10:11");
            md.SetQuery("/app1/ifd/{ushort=271}", "ACME");
            md.SetQuery("/app1/ifd/{ushort=272}", "Snapper 3");
        });
        Assert.NotNull((await T.Info(src)).DateTaken);
        var r = First(await T.Run(host, new RemovePhotoInfoAction(), ContentValue.Images(new[] { src })));
        var info = await T.Info(r.Path);
        Assert.Null(info.DateTaken);
        Assert.Null(info.CameraModel);
        Assert.Null(info.Latitude);
        Assert.Equal((80, 40), (info.Width, info.Height));
        Assert.Equal("a-clean.jpg", Path.GetFileName(r.Path));
        Assert.True(T.IsRed(await T.PixelAt(r.Path, 10, 20)));
    }

    [Fact]
    public async Task AddTextChangesPixelsAndKeepsSize()
    {
        using var d = new TempDir(); using var host = T.Host();
        var src = d.File("blank.png");
        await StaWorker.Imaging.RunAsync(() =>
        {
            var data = Enumerable.Repeat((byte)0, 300 * 100 * 3).ToArray(); // black
            var bmp = BitmapSource.Create(300, 100, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null, data, 900);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp)); using var fs = File.Create(src); enc.Save(fs);
        });
        var r = First(await T.Run(host, new AddTextToImageAction(), ContentValue.Images(new[] { src }),
            ("text", "HELLO"), ("position", T.Choice("center")), ("size", 40), ("color", "#FFFFFF"), ("bold", true), ("shadow", false)));
        Assert.Equal((300, 100), (r.Width, r.Height));
        Assert.Equal("blank-text.png", Path.GetFileName(r.Path));
        // some pixel near the middle must now be bright
        var bright = false;
        for (var x = 100; x < 200 && !bright; x += 2)
            for (var y = 30; y < 70 && !bright; y += 2) { var c = await T.PixelAt(r.Path, x, y); bright = c.R > 200 && c.G > 200; }
        Assert.True(bright, "expected white text pixels");
        Assert.True((await T.PixelAt(r.Path, 2, 2)).R < 20);
    }

    [Fact]
    public async Task AddTextPositionsPlaceTextInDifferentPlaces()
    {
        using var d = new TempDir(); using var host = T.Host();
        var src = d.File("blank.png");
        await StaWorker.Imaging.RunAsync(() =>
        {
            var data = Enumerable.Repeat((byte)0, 300 * 200 * 3).ToArray();
            var bmp = BitmapSource.Create(300, 200, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null, data, 900);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp)); using var fs = File.Create(src); enc.Save(fs);
        });
        async Task<(bool Top, bool Bottom)> Where(string pos)
        {
            var r = First(await T.Run(host, new AddTextToImageAction(), ContentValue.Images(new[] { src }), ("text", "WWW"), ("position", T.Choice(pos)), ("size", 15), ("shadow", false), ("suffix", "-" + pos)));
            bool Any(int y0, int y1) { var found = false; for (var y = y0; y < y1; y += 2) for (var x = 0; x < 300; x += 2) { var c = T.PixelAt(r.Path, x, y).Result; if (c.R > 200) found = true; } return found; }
            return (Any(0, 60), Any(140, 200));
        }
        Assert.Equal((true, false), await Where("topLeft"));
        Assert.Equal((false, true), await Where("bottomRight"));
    }

    [Fact]
    public async Task DetailsReadDimensionsDateCameraAndLocation()
    {
        using var d = new TempDir(); using var host = T.Host();
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
        var input = ContentValue.Images(new[] { src });
        Assert.Equal("120 × 80", (await T.Run(host, new GetImageDetailsAction(), input)).AsText());
        Assert.Equal(120, (await T.Run(host, new GetImageDetailsAction(), input, ("detail", T.Choice("width")))).AsNumber());
        var taken = (await T.Run(host, new GetImageDetailsAction(), input, ("detail", T.Choice("dateTaken")))).AsDate();
        Assert.Equal(new DateTime(2018, 12, 25, 8, 30, 0), taken!.Value.DateTime);
        Assert.Equal("Canon EOS R5", (await T.Run(host, new GetImageDetailsAction(), input, ("detail", T.Choice("camera")))).AsText());
        var loc = Assert.IsType<LocationItem>((await T.Run(host, new GetImageDetailsAction(), input, ("detail", T.Choice("location"))))[0]);
        Assert.Equal(48.858, loc.Latitude, 3);
        Assert.Equal(-2.35, loc.Longitude, 3);
        var all = Assert.IsType<DictionaryItem>((await T.Run(host, new GetImageDetailsAction(), input, ("detail", T.Choice("all"))))[0]);
        Assert.Equal("trip.jpg", all.Get("Name").AsText());
        Assert.Equal(80, all.Get("Height").AsNumber());
    }

    private static ulong Rational(uint num, uint den) => ((ulong)den << 32) | num;

    [Fact]
    public async Task DetailsWithoutPhotoInfoGiveNothingForDateAndLocation()
    {
        using var d = new TempDir(); using var host = T.Host();
        var src = await T.MakeImage(d.File("plain.png"), 10, 10);
        var input = ContentValue.Images(new[] { src });
        Assert.True((await T.Run(host, new GetImageDetailsAction(), input, ("detail", T.Choice("dateTaken")))).IsEmpty);
        Assert.True((await T.Run(host, new GetImageDetailsAction(), input, ("detail", T.Choice("location")))).IsEmpty);
    }

    [Fact]
    public async Task NonImagesGiveFriendlyErrors()
    {
        using var d = new TempDir(); using var host = T.Host();
        var fake = d.File("broken.jpg", "this is not a jpeg");
        var ex = await Assert.ThrowsAsync<ActionException>(() => T.Run(host, new ResizeImagesAction(), ContentValue.Images(new[] { fake }), ("by", T.Choice("width")), ("width", 10)));
        Assert.Contains("Couldn't open 'broken.jpg'", ex.Message);
        var ex2 = await Assert.ThrowsAsync<ActionException>(() => T.Run(host, new ResizeImagesAction(), ContentValue.Images(new[] { Path.Combine(d.Path, "missing.jpg") }), ("by", T.Choice("width")), ("width", 10)));
        Assert.Contains("Couldn't find", ex2.Message);
    }

    [Fact]
    public async Task PrepareForAiShrinksBigAndConvertsOddFormats()
    {
        using var d = new TempDir();
        var big = await T.MakeImage(d.File("big.png"), 3000, 1500);
        var (data, media) = await StaWorker.Imaging.RunAsync(() => ImageEngine.PrepareForAi(big));
        Assert.Equal("image/jpeg", media);
        using var ms = new MemoryStream(data);
        var info = await StaWorker.Imaging.RunAsync(() => BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0].PixelWidth);
        Assert.Equal(2000, info);
        var bmp = await T.MakeImage(d.File("x.bmp"), 50, 50);
        Assert.Equal("image/jpeg", (await StaWorker.Imaging.RunAsync(() => ImageEngine.PrepareForAi(bmp))).MediaType);
        var small = await T.MakeImage(d.File("s.png"), 50, 50);
        Assert.Equal("image/png", (await StaWorker.Imaging.RunAsync(() => ImageEngine.PrepareForAi(small))).MediaType);
    }
}
