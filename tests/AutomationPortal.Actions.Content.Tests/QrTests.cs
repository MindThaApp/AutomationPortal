using AutomationPortal.Actions.Content.Actions;
using AutomationPortal.Actions.Content.Utilities;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using QRCoder;
using ZXing;
using ZXing.Common;
using static AutomationPortal.Actions.Content.Tests.Kit;

namespace AutomationPortal.Actions.Content.Tests;

public class QrTests : IDisposable
{
    private readonly TestHost _host = new();
    public void Dispose() => _host.Dispose();

    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    [Fact]
    public async Task Make_creates_a_real_png_image_in_the_run_folder()
    {
        var r = await Run(_host, new MakeQrCodeAction(), ContentValue.Text("https://example.com"));
        var img = Assert.IsType<ImageItem>(Assert.Single(r));
        Assert.True(File.Exists(img.Path));
        Assert.EndsWith(".png", img.Path);
        Assert.Equal(PngSignature, (await File.ReadAllBytesAsync(img.Path)).Take(8));
        Assert.True(img.Width > 100);
        Assert.Equal(img.Width, img.Height);
        Assert.Equal(img.Width, r.Property("Width").AsNumber());
    }

    [Theory]
    [InlineData("https://example.com/path?query=1&other=two")]
    [InlineData("Hello, wörld! ✓ 日本語")]
    [InlineData("12345678")]
    [InlineData("WIFI:T:WPA;S:HomeNet;P:p@ss;;")]
    [InlineData("BEGIN:VCARD\nVERSION:3.0\nFN:Sam Smith\nTEL:+441234567890\nEND:VCARD")]
    [InlineData("a")]
    public async Task Make_then_read_round_trips(string text)
    {
        var made = await Run(_host, new MakeQrCodeAction(), ContentValue.Text(text));
        var read = await Run(_host, new ReadQrCodeAction(), made);
        Assert.Equal(text, read.AsText());
    }

    [Fact]
    public async Task Round_trips_a_long_text_at_every_size_and_protection_level()
    {
        var text = string.Concat(Enumerable.Range(0, 60).Select(i => $"Line {i}; "));
        foreach (var level in new[] { "L", "M", "Q", "H" })
            foreach (var size in new[] { 4, 12, 40 })
            {
                var made = await Run(_host, new MakeQrCodeAction(), ContentValue.Text(text), ("size", size), ("repair", C(level)));
                Assert.Equal(text, (await Run(_host, new ReadQrCodeAction(), made)).AsText());
            }
    }

    [Fact]
    public async Task Bigger_squares_give_bigger_images()
    {
        var small = (ImageItem)(await Run(_host, new MakeQrCodeAction(), ContentValue.Text("size test"), ("size", 4)))[0];
        var big = (ImageItem)(await Run(_host, new MakeQrCodeAction(), ContentValue.Text("size test"), ("size", 20)))[0];
        Assert.Equal(5, big.Width / small.Width);
    }

    [Fact]
    public async Task Make_gives_one_image_per_item()
    {
        var r = await Run(_host, new MakeQrCodeAction(), Texts("one", "two", "three"));
        Assert.Equal(3, r.Count);
        Assert.Equal(3, r.Select(i => ((ImageItem)i).Path).Distinct().Count());
        Assert.Equal(new[] { "one", "two", "three" }, Lines(await Run(_host, new ReadQrCodeAction(), r)));
    }

    [Fact]
    public async Task Make_accepts_url_items()
    {
        var made = await Run(_host, new MakeQrCodeAction(), ContentValue.Url("https://example.org/x"));
        Assert.Equal("https://example.org/x", (await Run(_host, new ReadQrCodeAction(), made)).AsText());
    }

    [Fact]
    public async Task Make_errors_are_friendly()
    {
        var tooLong = await Fails(Run(_host, new MakeQrCodeAction(), ContentValue.Text(new string('x', 6000))));
        Assert.Contains("too much text", tooLong.Message);
        await Fails(Run(_host, new MakeQrCodeAction(), ContentValue.Empty));
        await Fails(Run(_host, new MakeQrCodeAction(), ContentValue.Text("")));
    }

    [Fact]
    public async Task Reads_a_qr_code_from_a_jpeg_style_photo_with_no_white_border_and_on_a_big_canvas()
    {
        // crop away the quiet zone and put the code in the corner of a larger picture
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode("no border here", QRCodeGenerator.ECCLevel.M);
        var bmp = new PngByteQRCode(data).GetGraphic(10, new byte[] { 0, 0, 0 }, new byte[] { 255, 255, 255 }, drawQuietZones: false);
        var path = Path.Combine(_host.Root, "noborder.png");
        Directory.CreateDirectory(_host.Root);
        await File.WriteAllBytesAsync(path, bmp);
        Assert.Equal("no border here", (await Run(_host, new ReadQrCodeAction(), ContentValue.Image(path))).AsText());
    }

    [Fact]
    public async Task Reads_common_barcodes()
    {
        Directory.CreateDirectory(_host.Root);
        foreach (var (format, text) in new[]
        {
            (BarcodeFormat.EAN_13, "5901234123457"), (BarcodeFormat.CODE_128, "AutomationPortal-123"), (BarcodeFormat.CODE_39, "HELLO123"),
            (BarcodeFormat.UPC_A, "036000291452"), (BarcodeFormat.ITF, "12345670"), (BarcodeFormat.DATA_MATRIX, "Matrix 42"), (BarcodeFormat.AZTEC, "Aztec 99"),
        })
        {
            var path = WriteBarcodeBmp(format, text, $"{format}.bmp");
            Assert.Equal(text, (await Run(_host, new ReadQrCodeAction(), ContentValue.Image(path))).AsText());
        }
    }

    [Fact]
    public async Task Reads_two_codes_in_one_picture()
    {
        Directory.CreateDirectory(_host.Root);
        var a = QrBitmap("first code");
        var b = QrBitmap("second code");
        var (pa, wa, ha) = a; var (pb, wb, hb) = b;
        var gap = 40;
        var width = wa + gap + wb; var height = Math.Max(ha, hb);
        var canvas = new byte[width * height * 3];
        Array.Fill(canvas, (byte)255);
        for (var y = 0; y < ha; y++) Buffer.BlockCopy(pa, y * wa * 3, canvas, y * width * 3, wa * 3);
        for (var y = 0; y < hb; y++) Buffer.BlockCopy(pb, y * wb * 3, canvas, (y * width + wa + gap) * 3, wb * 3);
        var path = Path.Combine(_host.Root, "two.bmp");
        await File.WriteAllBytesAsync(path, Bmp24(canvas, width, height));
        var found = Lines(await Run(_host, new ReadQrCodeAction(), ContentValue.Image(path)));
        Assert.Contains("first code", found);
        Assert.Contains("second code", found);
    }

    [Fact]
    public async Task Reading_errors_are_friendly()
    {
        Directory.CreateDirectory(_host.Root);
        var blank = Path.Combine(_host.Root, "blank.bmp");
        await File.WriteAllBytesAsync(blank, Bmp24(Enumerable.Repeat((byte)255, 200 * 200 * 3).ToArray(), 200, 200));
        var none = await Fails(Run(_host, new ReadQrCodeAction(), ContentValue.Image(blank)));
        Assert.Contains("Couldn't find a QR code or barcode", none.Message);

        var missing = await Fails(Run(_host, new ReadQrCodeAction(), ContentValue.Image(Path.Combine(_host.Root, "gone.png"))));
        Assert.Contains("gone.png", missing.Message);

        var fake = Path.Combine(_host.Root, "fake.png");
        await File.WriteAllTextAsync(fake, "not really an image");
        var notImage = await Fails(Run(_host, new ReadQrCodeAction(), ContentValue.Image(fake)));
        Assert.Contains("fake.png", notImage.Message);

        var textFile = Path.Combine(_host.Root, "notes.txt");
        await File.WriteAllTextAsync(textFile, "hi");
        await Fails(Run(_host, new ReadQrCodeAction(), ContentValue.File(textFile)));
        await Fails(Run(_host, new ReadQrCodeAction(), ContentValue.Empty));
    }

    // ---- tiny BMP writer so the tests need no imaging library -----------------------------------

    private static (byte[] Bgr, int Width, int Height) QrBitmap(string text)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var modules = data.ModuleMatrix;
        const int scale = 6, quiet = 4;
        var n = modules.Count + quiet * 2;
        var size = n * scale;
        var px = new byte[size * size * 3];
        Array.Fill(px, (byte)255);
        for (var y = 0; y < modules.Count; y++)
            for (var x = 0; x < modules.Count; x++)
                if (modules[y][x])
                    for (var dy = 0; dy < scale; dy++)
                        for (var dx = 0; dx < scale; dx++)
                        {
                            var o = (((y + quiet) * scale + dy) * size + (x + quiet) * scale + dx) * 3;
                            px[o] = px[o + 1] = px[o + 2] = 0;
                        }
        return (px, size, size);
    }

    private string WriteBarcodeBmp(BarcodeFormat format, string text, string name)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = format,
            Options = new EncodingOptions
            {
                Width = format is BarcodeFormat.DATA_MATRIX or BarcodeFormat.AZTEC ? 300 : 500,
                Height = format is BarcodeFormat.DATA_MATRIX or BarcodeFormat.AZTEC ? 300 : 200,
                Margin = 20,
                PureBarcode = true,
            },
        };
        var pixels = writer.Write(text);
        var bgr = new byte[pixels.Width * pixels.Height * 3];
        for (var i = 0; i < pixels.Width * pixels.Height; i++)
        {
            bgr[i * 3] = pixels.Pixels[i * 4]; bgr[i * 3 + 1] = pixels.Pixels[i * 4 + 1]; bgr[i * 3 + 2] = pixels.Pixels[i * 4 + 2];
        }
        var path = Path.Combine(_host.Root, name);
        File.WriteAllBytes(path, Bmp24(bgr, pixels.Width, pixels.Height));
        return path;
    }

    /// <summary>24-bit BMP from top-down BGR rows.</summary>
    private static byte[] Bmp24(byte[] bgrTopDown, int width, int height)
    {
        var rowSize = (width * 3 + 3) / 4 * 4;
        var data = new byte[54 + rowSize * height];
        data[0] = (byte)'B'; data[1] = (byte)'M';
        BitConverter.GetBytes(data.Length).CopyTo(data, 2);
        BitConverter.GetBytes(54).CopyTo(data, 10);
        BitConverter.GetBytes(40).CopyTo(data, 14);
        BitConverter.GetBytes(width).CopyTo(data, 18);
        BitConverter.GetBytes(height).CopyTo(data, 22);
        BitConverter.GetBytes((short)1).CopyTo(data, 26);
        BitConverter.GetBytes((short)24).CopyTo(data, 28);
        BitConverter.GetBytes(rowSize * height).CopyTo(data, 34);
        for (var y = 0; y < height; y++)
            Buffer.BlockCopy(bgrTopDown, y * width * 3, data, 54 + (height - 1 - y) * rowSize, width * 3);
        return data;
    }
}
