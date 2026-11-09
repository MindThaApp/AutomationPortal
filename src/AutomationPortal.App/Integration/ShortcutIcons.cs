using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Flowmate.App.Infrastructure;
using Flowmate.Core;
using Wpf.Ui.Controls;

namespace Flowmate.App.Integration;

/// <summary>
/// Makes the .ico files for Desktop / Start menu / Send To / Jump List entries: the shortcut's Fluent glyph, in white,
/// on its gradient colour, rendered at 16 to 256 px. Files live in <c>%LOCALAPPDATA%\Flowmate\icons</c> and are reused.
/// </summary>
public sealed class ShortcutIcons
{
    public static readonly int[] Sizes = { 16, 24, 32, 48, 64, 128, 256 };

    private readonly string _folder;
    private readonly object _gate = new();

    public ShortcutIcons(string iconsFolder) { _folder = iconsFolder; }

    /// <summary>Path of the .ico for this glyph and colour, creating it on first use. Safe to call from any thread.</summary>
    public string Ensure(string? icon, string? color)
    {
        var name = Icons.IsKnown(icon) ? icon! : "Flash24";
        var key = Palette.Keys.Contains(color ?? "", StringComparer.OrdinalIgnoreCase) ? color!.ToLowerInvariant() : "blue";
        var path = Path.Combine(_folder, $"{name}-{key}.ico");
        lock (_gate)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 0) return path;
            AtomicFile.WriteAllBytes(path, OnStaThread(() => RenderIco(name, key)));
        }
        return path;
    }

    private static T OnStaThread<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var t = new Thread(() => { try { result = work(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        t.Join();
        if (error is not null) throw new IOException("Couldn't draw the icon. " + error.Message, error);
        return result;
    }

    internal static byte[] RenderIco(string icon, string color)
    {
        var pngs = Sizes.Select(s => (Size: s, Png: RenderPng(icon, color, s))).ToList();
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)pngs.Count);
        var offset = 6 + 16 * pngs.Count;
        foreach (var (size, png) in pngs)
        {
            w.Write((byte)(size >= 256 ? 0 : size)); w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0); w.Write((byte)0); w.Write((ushort)1); w.Write((ushort)32);
            w.Write((uint)png.Length); w.Write((uint)offset);
            offset += png.Length;
        }
        foreach (var (_, png) in pngs) w.Write(png);
        return ms.ToArray();
    }

    /// <summary>Must run on an STA thread. Draws the glyph straight from WPF-UI's embedded icon font, so it needs no Application or styles.</summary>
    internal static byte[] RenderPng(string icon, string color, int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var radius = size * 0.22;
            var gradient = new LinearGradientBrush(Palette.Top(color), Palette.Bottom(color), new Point(0.5, 0), new Point(0.5, 1));
            dc.DrawRoundedRectangle(gradient, null, new Rect(0, 0, size, size), radius, radius);

            var font = IconFont.Value;
            if (font.CharacterToGlyphMap.TryGetValue((int)Icons.Parse(icon), out var glyph))
            {
                var run = new GlyphRun(font, 0, false, size * 0.58, 1f, new[] { glyph }, new Point(0, 0), new[] { font.AdvanceWidths[glyph] },
                    null, null, null, null, null, null);
                var ink = run.ComputeInkBoundingBox();
                dc.PushTransform(new TranslateTransform(size / 2 - (ink.X + ink.Width / 2), size / 2 - (ink.Y + ink.Height / 2)));
                dc.DrawGlyphRun(Brushes.White, run);
                dc.Pop();
            }
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    // WPF-UI ships the Fluent icon font inside its assembly. GlyphTypeface only opens files, so it is copied to %TEMP% once.
    private static readonly Lazy<GlyphTypeface> IconFont = new(() =>
    {
        var assembly = typeof(SymbolIcon).Assembly;
        var file = Path.Combine(Path.GetTempPath(), "Flowmate", $"FluentSystemIcons-Regular-{assembly.GetName().Version}.ttf");
        if (!File.Exists(file) || new FileInfo(file).Length == 0)
        {
            using var resources = assembly.GetManifestResourceStream("Wpf.Ui.g.resources") ?? throw new IOException("The icon font is missing.");
            using var reader = new System.Resources.ResourceReader(resources);
            foreach (System.Collections.DictionaryEntry entry in reader)
            {
                if (!string.Equals(entry.Key as string, "resources/fonts/fluentsystemicons-regular.ttf", StringComparison.OrdinalIgnoreCase) || entry.Value is not Stream font) continue;
                AtomicFile.WriteAllBytes(file, ReadAll(font));
                break;
            }
        }
        return new GlyphTypeface(new Uri(file));
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    private static byte[] ReadAll(Stream s) { using var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray(); }
}
