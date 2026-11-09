using Windows.Graphics.Imaging;
using Windows.Storage;
using ZXing;
using ZXing.Common;

namespace AutomationPortal.Actions.Content.Utilities;

/// <summary>Reads QR codes and common barcodes from an image file: pixels via the Windows imaging API, decoding via ZXing.Net.</summary>
public static class BarcodeReading
{
    private const int MaxSide = 3000;

    public static async Task<IReadOnlyList<string>> ReadAsync(string path, CancellationToken ct = default)
    {
        var (pixels, width, height) = await LoadPixelsAsync(path);
        ct.ThrowIfCancellationRequested();

        var results = Decode(pixels, width, height);
        if (results.Count == 0)
        {
            // A code with no white border around it is hard to find: add one and try again.
            var margin = Math.Max(width, height) / 8;
            var (padded, pw, ph) = Pad(pixels, width, height, margin);
            results = Decode(padded, pw, ph);
        }
        return results;
    }

    private static List<string> Decode(byte[] bgra, int width, int height)
    {
        var options = new DecodingOptions { TryHarder = true, TryInverted = true };
        var reader = new BarcodeReaderGeneric { AutoRotate = true, Options = options };
        var source = new RGBLuminanceSource(bgra, width, height, RGBLuminanceSource.BitmapFormat.BGRA32);
        var found = new List<string>();
        try
        {
            var many = reader.DecodeMultiple(source);
            if (many is not null) found.AddRange(many.Where(r => !string.IsNullOrEmpty(r.Text)).Select(r => r.Text));
        }
        catch (Exception) { /* fall through to the single-code reader */ }
        if (found.Count == 0)
        {
            try
            {
                var one = reader.Decode(source);
                if (one is not null && !string.IsNullOrEmpty(one.Text)) found.Add(one.Text);
            }
            catch (Exception) { }
        }
        return found.Distinct().ToList();
    }

    private static async Task<(byte[] Bgra, int Width, int Height)> LoadPixelsAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var transform = new BitmapTransform();
            var w = (int)decoder.PixelWidth; var h = (int)decoder.PixelHeight;
            if (Math.Max(w, h) > MaxSide)
            {
                var scale = (double)MaxSide / Math.Max(w, h);
                transform.ScaledWidth = (uint)Math.Max(1, w * scale);
                transform.ScaledHeight = (uint)Math.Max(1, h * scale);
                w = (int)transform.ScaledWidth; h = (int)transform.ScaledHeight;
            }
            var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
            var bytes = data.DetachPixelData();
            // Premultiplied alpha over white: transparent pixels become white instead of black.
            for (var i = 0; i < bytes.Length; i += 4)
            {
                var add = 255 - bytes[i + 3];
                if (add == 0) continue;
                bytes[i] = (byte)Math.Min(255, bytes[i] + add);
                bytes[i + 1] = (byte)Math.Min(255, bytes[i + 1] + add);
                bytes[i + 2] = (byte)Math.Min(255, bytes[i + 2] + add);
                bytes[i + 3] = 255;
            }
            return (bytes, w, h);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new AutomationPortal.Core.ActionException($"Couldn't open '{Path.GetFileName(path)}' as a picture. Make sure it's a PNG, JPEG, BMP, GIF or similar image file.", ex);
        }
    }

    private static (byte[] Pixels, int Width, int Height) Pad(byte[] src, int w, int h, int margin)
    {
        var nw = w + margin * 2; var nh = h + margin * 2;
        var dst = new byte[nw * nh * 4];
        Array.Fill(dst, (byte)255);
        for (var y = 0; y < h; y++)
            Buffer.BlockCopy(src, y * w * 4, dst, ((y + margin) * nw + margin) * 4, w * 4);
        return (dst, nw, nh);
    }
}
