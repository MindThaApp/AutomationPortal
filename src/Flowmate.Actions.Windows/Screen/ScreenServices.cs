using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Flowmate.Actions.Windows.Apps;
using Flowmate.Actions.Windows.Input;
using Flowmate.Actions.Windows.Native;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace Flowmate.Actions.Windows.Screen;

internal static class ScreenCapture
{
    private const uint PW_RENDERFULLCONTENT = 2;

    public static Bitmap CaptureRect(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ActionException("The area to capture is empty. Select a bigger area.");
        using var _ = DpiScope.PerMonitorV2();
        var screen = Win32.GetDC(IntPtr.Zero);
        var memory = Win32.CreateCompatibleDC(screen);
        var hbitmap = Win32.CreateCompatibleBitmap(screen, width, height);
        try
        {
            if (screen == IntPtr.Zero || memory == IntPtr.Zero || hbitmap == IntPtr.Zero) throw new ExternalException("No screen device.");
            var old = Win32.SelectObject(memory, hbitmap);
            // SRCCOPY | CAPTUREBLT: CAPTUREBLT also grabs layered (transparent) windows. System.Drawing's CopyFromScreen can't pass it.
            var ok = Win32.BitBlt(memory, 0, 0, width, height, screen, x, y, 0x00CC0020 | 0x40000000);
            Win32.SelectObject(memory, old);
            if (!ok) throw new ExternalException("BitBlt failed.");
            using var raw = Image.FromHbitmap(hbitmap);
            var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using var g = Graphics.FromImage(bmp);
            g.DrawImageUnscaled(raw, 0, 0);
            return bmp;
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or OutOfMemoryException)
        {
            throw new ActionException("Windows wouldn't let Flowmate take a screenshot right now. This can happen on the lock screen or in a protected window.", ex);
        }
        finally
        {
            if (hbitmap != IntPtr.Zero) Win32.DeleteObject(hbitmap);
            if (memory != IntPtr.Zero) Win32.DeleteDC(memory);
            if (screen != IntPtr.Zero) Win32.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    public static Bitmap CaptureAllScreens()
    {
        var (l, t, w, h) = InputSender.VirtualScreen();
        return CaptureRect(l, t, w, h);
    }

    public static Bitmap CapturePrimaryScreen()
    {
        var primary = Devices.DisplayService.GetMonitors().FirstOrDefault(m => m.IsPrimary)
                      ?? throw new ActionException("Couldn't find your main screen.");
        return CaptureRect(primary.Bounds.X, primary.Bounds.Y, primary.Bounds.Width, primary.Bounds.Height);
    }

    public static Bitmap CaptureWindow(IntPtr hwnd)
    {
        using var _ = DpiScope.PerMonitorV2();
        var rect = WindowService.GetRect(hwnd);
        var frame = WindowService.GetVisibleFrame(hwnd);
        var w = rect.Width;
        var h = rect.Height;
        if (w <= 0 || h <= 0) throw new ActionException("That window is minimised or has no size. Bring it to the front and try again.");

        using var full = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        bool ok;
        using (var g = Graphics.FromImage(full))
        {
            var hdc = g.GetHdc();
            try { ok = Win32.PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT); }
            finally { g.ReleaseHdc(hdc); }
        }
        var crop = new Rectangle(Math.Max(0, frame.Left - rect.Left), Math.Max(0, frame.Top - rect.Top),
            Math.Min(frame.Width, w), Math.Min(frame.Height, h));
        if (crop.Right > w) crop.Width = w - crop.X;
        if (crop.Bottom > h) crop.Height = h - crop.Y;
        if (ok && !LooksBlank(full))
            return full.Clone(crop, PixelFormat.Format32bppArgb);

        // Some GPU-drawn windows print as black; fall back to copying the pixels from the screen.
        return CaptureRect(frame.Left, frame.Top, frame.Width, frame.Height);
    }

    private static bool LooksBlank(Bitmap bmp)
    {
        var stepX = Math.Max(1, bmp.Width / 24);
        var stepY = Math.Max(1, bmp.Height / 24);
        for (var y = 0; y < bmp.Height; y += stepY)
            for (var x = 0; x < bmp.Width; x += stepX)
            {
                var c = bmp.GetPixel(x, y);
                if (c.A != 0 && (c.R | c.G | c.B) != 0) return false;
            }
        return true;
    }

    public static string? ScreenshotsFolder()
    {
        // FOLDERID_Screenshots
        var known = Win32.KnownFolder(new Guid("B7BEDE81-DF94-4682-A7D8-57A52620B86F"));
        return !string.IsNullOrEmpty(known) ? known : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
    }

    public static (int R, int G, int B) PixelAt(int x, int y)
    {
        using var _ = DpiScope.PerMonitorV2();
        var hdc = Win32.GetDC(IntPtr.Zero);
        try
        {
            var c = Win32.GetPixel(hdc, x, y);
            if (c == 0xFFFFFFFF) throw new ActionException("That spot is outside your screens. Pick a spot that is on screen.");
            return ((int)(c & 0xFF), (int)((c >> 8) & 0xFF), (int)((c >> 16) & 0xFF));
        }
        finally { Win32.ReleaseDC(IntPtr.Zero, hdc); }
    }
}

public static class ColorFormat
{
    public static string Hex(int r, int g, int b) => $"#{r:X2}{g:X2}{b:X2}";
    public static string Rgb(int r, int g, int b) => $"{r}, {g}, {b}";
}

/// <summary>Reads text from pictures with the OCR engine built into Windows.</summary>
public static class OcrService
{
    public static IReadOnlyList<(string Tag, string Name)> Languages() =>
        OcrEngine.AvailableRecognizerLanguages.Select(l => (l.LanguageTag, l.DisplayName)).ToList();

    private static OcrEngine CreateEngine(string? languageTag)
    {
        OcrEngine? engine = null;
        if (!string.IsNullOrWhiteSpace(languageTag) && languageTag != "auto")
        {
            engine = OcrEngine.TryCreateFromLanguage(new Language(languageTag));
            if (engine is null)
                throw new ActionException($"Windows doesn't have text recognition for '{languageTag}'. Add that language in Settings > Time & language > Language & region, or pick another language.");
            return engine;
        }
        engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null)
        {
            var first = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
            if (first is not null) engine = OcrEngine.TryCreateFromLanguage(first);
        }
        return engine ?? throw new ActionException("Windows has no text recognition installed. Add a language in Settings > Time & language > Language & region (tick \"Optical character recognition\").");
    }

    /// <summary>Recognises the text of a picture file. Lines are separated with new lines.</summary>
    public static async Task<string> RecognizeFileAsync(string path, string? languageTag, CancellationToken ct)
    {
        var engine = CreateEngine(languageTag);
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)).AsTask(ct).ConfigureAwait(false);
            using var stream = await file.OpenAsync(FileAccessMode.Read).AsTask(ct).ConfigureAwait(false);
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct).ConfigureAwait(false);
            var transform = new BitmapTransform();
            var max = OcrEngine.MaxImageDimension;
            if (decoder.PixelWidth > max || decoder.PixelHeight > max)
            {
                var scale = (double)max / Math.Max(decoder.PixelWidth, decoder.PixelHeight);
                transform.ScaledWidth = (uint)Math.Max(1, decoder.PixelWidth * scale);
                transform.ScaledHeight = (uint)Math.Max(1, decoder.PixelHeight * scale);
            }
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(ct).ConfigureAwait(false);
            var result = await engine.RecognizeAsync(bitmap).AsTask(ct).ConfigureAwait(false);
            return string.Join("\n", result.Lines.Select(l => l.Text));
        }
        catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            throw new ActionException($"Couldn't read the picture '{Path.GetFileName(path)}'. Check it is a normal picture file (PNG, JPG, BMP…) and still exists.", ex);
        }
    }

    internal static ParameterDescriptor LanguageParam() => new()
    {
        Key = "language", Label = "Language", Type = ParamType.Choice, Default = ParamValue.Choice("auto"),
        ChoicesProvider = (_, _) => Task.FromResult<IReadOnlyList<Choice>>(
            new[] { new Choice("auto", "My Windows languages") }.Concat(Languages().Select(l => new Choice(l.Tag, l.Name))).ToList()),
    };
}
