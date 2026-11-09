using System.Windows.Media;
using System.Windows.Media.Imaging;
using AutomationPortal.Actions.Documents;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;

namespace AutomationPortal.Actions.Documents.Tests;

/// <summary>A scratch folder under %TEMP% that is deleted afterwards. Tests only ever touch files in here.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "automationportal-b3-tests", Guid.NewGuid().ToString("N")[..8]);
    public TempDir() => Directory.CreateDirectory(Path);
    public string File(string name, string? text = null)
    {
        var p = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
        if (text is not null) System.IO.File.WriteAllText(p, text);
        return p;
    }
    public string Folder(string name) => Directory.CreateDirectory(System.IO.Path.Combine(Path, name)).FullName;
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}

public static class T
{
    public static TestHost Host()
    {
        var host = new TestHost();
        host.With(typeof(RenameFilesAction).Assembly);
        return host;
    }

    public static Task<ContentValue> Run(TestHost host, ShortcutAction action, ContentValue? input, params (string Key, ParamValue Value)[] p) =>
        input is null ? ActionTestHarness.RunAsync(host, action, p) : ActionTestHarness.RunAsync(host, action, input, p);

    public static ParamValue Choice(string id) => ParamValue.Choice(id);

    public static string[] Paths(ContentValue v) => v.OfType<PathItem>().Select(p => p.Path).ToArray();

    // ---- pictures -------------------------------------------------------------------------------

    /// <summary>Left half red, right half blue, so flips and rotations can be checked.</summary>
    public static BitmapSource Pattern(int w, int h)
    {
        var stride = w * 3;
        var data = new byte[stride * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = y * stride + x * 3;
                if (x < w / 2) { data[i] = 0; data[i + 1] = 0; data[i + 2] = 255; }   // BGR: red
                else { data[i] = 255; data[i + 1] = 0; data[i + 2] = 0; }              // blue
            }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr24, null, data, stride);
        bmp.Freeze();
        return bmp;
    }

    public static Task<string> MakeImage(string path, int w, int h, Action<BitmapMetadata>? meta = null, int jpegQuality = 90) =>
        StaWorker.Imaging.RunAsync(() =>
        {
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            BitmapEncoder enc = ext switch
            {
                ".png" => new PngBitmapEncoder(),
                ".bmp" => new BmpBitmapEncoder(),
                ".gif" => new GifBitmapEncoder(),
                ".tif" or ".tiff" => new TiffBitmapEncoder(),
                _ => new JpegBitmapEncoder { QualityLevel = jpegQuality },
            };
            BitmapMetadata? md = null;
            if (meta is not null)
            {
                md = new BitmapMetadata("jpg");
                meta(md);
            }
            enc.Frames.Add(md is null ? BitmapFrame.Create(Pattern(w, h)) : BitmapFrame.Create(Pattern(w, h), null, md, null));
            using var fs = System.IO.File.Create(path);
            enc.Save(fs);
            return path;
        });

    public static Task<ImageInfo> Info(string path) => StaWorker.Imaging.RunAsync(() => ImageMetadataReader.Read(path));

    /// <summary>The colour of one pixel of the displayed (upright) picture.</summary>
    public static Task<Color> PixelAt(string path, int x, int y) =>
        StaWorker.Imaging.RunAsync(() =>
        {
            using var fs = System.IO.File.OpenRead(path);
            var frame = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var px = new byte[4];
            bgra.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), px, 4, 0);
            return Color.FromArgb(px[3], px[2], px[1], px[0]);
        });

    public static bool IsRed(Color c) => c.R > 200 && c.G < 60 && c.B < 60;
    public static bool IsBlue(Color c) => c.B > 200 && c.G < 60 && c.R < 60;
}
