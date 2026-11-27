using System.Windows;
using System.Windows.Media;

namespace AutomationPortal.App.Controls;

/// <summary>
/// Small hand-drawn flags for the language picker: 20 x 15 units, simplified so they read at that size, clipped to 2 px corners.
/// The border is drawn by the picker with a theme brush so white flags stay visible on light themes.
/// </summary>
public static class FlagArt
{
    public const double W = 20, H = 15;

    private static readonly Dictionary<string, DrawingImage> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The regions that have a drawing.</summary>
    public static IReadOnlyCollection<string> Regions { get; } = new[] { "US", "DE", "FR", "ES", "IT", "BR", "JP", "CN", "KR", "RU", "PL", "NL", "TR", "SA", "IN", "ID", "VN", "TH", "SE" };

    public static bool Has(string? region) => region is not null && Regions.Contains(region, StringComparer.OrdinalIgnoreCase);

    /// <summary>The flag for a region, frozen, or null when there is none.</summary>
    public static DrawingImage? For(string? region)
    {
        if (!Has(region)) return null;
        lock (Cache)
        {
            if (Cache.TryGetValue(region!, out var hit)) return hit;
            var g = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, W, H), 2, 2) };
            Draw(region!.ToUpperInvariant(), g.Children);
            var image = new DrawingImage(g);
            image.Freeze();
            Cache[region!] = image;
            return image;
        }
    }

    private static readonly Color Red = C(0xD8, 0x1E, 0x2A), White = Colors.White, Navy = C(0x1F, 0x2F, 0x6B), Blue = C(0x00, 0x39, 0xA6), Gold = C(0xFF, 0xCE, 0x00),
        Green = C(0x00, 0x8C, 0x45), Black = C(0x20, 0x20, 0x20);

    private static Color C(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private static void Draw(string region, DrawingCollection d)
    {
        switch (region)
        {
            case "US":
                Fill(d, White, 0, 0, W, H);
                for (var i = 0; i < 13; i += 2) Fill(d, C(0xB2, 0x22, 0x34), 0, i * H / 13, W, H / 13);
                Fill(d, C(0x3C, 0x3B, 0x6E), 0, 0, 8.5, 7 * H / 13);
                for (var r = 0; r < 3; r++) for (var c = 0; c < 4; c++) Dot(d, White, 1.3 + c * 2, 1.4 + r * 2.4, 0.45);
                break;
            case "DE": HStripes(d, Black, Red, Gold); break;
            case "FR": VStripes(d, C(0x00, 0x55, 0xA4), White, C(0xEF, 0x41, 0x35)); break;
            case "IT": VStripes(d, C(0x00, 0x92, 0x46), White, C(0xCE, 0x2B, 0x37)); break;
            case "ES":
                Fill(d, C(0xC6, 0x0B, 0x1E), 0, 0, W, H);
                Fill(d, C(0xFF, 0xC4, 0x00), 0, H / 4, W, H / 2);
                break;
            case "BR":
                Fill(d, C(0x00, 0x9C, 0x3B), 0, 0, W, H);
                Shape(d, C(0xFF, 0xDF, 0x00), "M 10,1.6 L 18.4,7.5 L 10,13.4 L 1.6,7.5 Z");
                Dot(d, C(0x00, 0x27, 0x76), 10, 7.5, 3.4);
                Shape(d, White, "M 6.8,6.9 Q 10,5.9 13.2,8.1 L 13.1,8.7 Q 10,6.6 6.7,7.5 Z");
                break;
            case "JP":
                Fill(d, White, 0, 0, W, H);
                Dot(d, C(0xBC, 0x00, 0x2D), 10, 7.5, 4.3);
                break;
            case "CN":
                Fill(d, C(0xEE, 0x1C, 0x25), 0, 0, W, H);
                Star(d, C(0xFF, 0xDE, 0x00), 4.2, 4, 2.6, 0);
                Star(d, C(0xFF, 0xDE, 0x00), 8, 1.6, 0.85, 20);
                Star(d, C(0xFF, 0xDE, 0x00), 9.6, 3.2, 0.85, 45);
                Star(d, C(0xFF, 0xDE, 0x00), 9.6, 5.5, 0.85, 0);
                Star(d, C(0xFF, 0xDE, 0x00), 8, 7.1, 0.85, 20);
                break;
            case "KR":
                Fill(d, White, 0, 0, W, H);
                Dot(d, C(0x00, 0x47, 0xA0), 10, 7.5, 3.7);
                Shape(d, C(0xCD, 0x2E, 0x3A), "M 6.3,7.5 A 3.7,3.7 0 0 1 13.7,7.5 A 1.85,1.85 0 0 1 10,7.5 A 1.85,1.85 0 0 0 6.3,7.5 Z");
                Trigram(d, 3.4, 3.0, 33); Trigram(d, 16.6, 12.0, 33);
                Trigram(d, 16.6, 3.0, -33); Trigram(d, 3.4, 12.0, -33);
                break;
            case "RU": HStripes(d, White, C(0x00, 0x39, 0xA6), C(0xD5, 0x2B, 0x1E)); break;
            case "PL":
                Fill(d, White, 0, 0, W, H);
                Fill(d, C(0xDC, 0x14, 0x3C), 0, H / 2, W, H / 2);
                break;
            case "NL": HStripes(d, C(0xAE, 0x1C, 0x28), White, C(0x21, 0x46, 0x8B)); break;
            case "TR":
                Fill(d, C(0xE3, 0x0A, 0x17), 0, 0, W, H);
                Dot(d, White, 7.6, 7.5, 3.8);
                Dot(d, C(0xE3, 0x0A, 0x17), 8.6, 7.5, 3.05);
                Star(d, White, 12.4, 7.5, 1.4, -18);
                break;
            case "SA":
                Fill(d, C(0x00, 0x6C, 0x35), 0, 0, W, H);
                Shape(d, White, "M 5,5.2 Q 7,4.2 8,5.6 Q 9.5,4 11,5.4 Q 12.6,4.2 15,5.4 L 15,6.4 Q 12.6,5.4 11,6.6 Q 9.5,5.2 8,6.8 Q 7,5.4 5,6.4 Z");
                Fill(d, White, 5, 9.4, 9.4, 0.7);
                Fill(d, White, 14.2, 8.9, 0.6, 1.7);
                break;
            case "IN":
                HStripes(d, C(0xFF, 0x99, 0x33), White, C(0x13, 0x88, 0x08));
                d.Add(new GeometryDrawing(null, Pen(C(0x00, 0x00, 0x80), 0.45), new EllipseGeometry(new Point(10, 7.5), 1.9, 1.9)));
                for (var i = 0; i < 12; i++)
                {
                    var a = i * Math.PI / 6;
                    d.Add(new GeometryDrawing(null, Pen(C(0x00, 0x00, 0x80), 0.2), new LineGeometry(new Point(10, 7.5), new Point(10 + 1.8 * Math.Cos(a), 7.5 + 1.8 * Math.Sin(a)))));
                }
                break;
            case "ID":
                Fill(d, White, 0, 0, W, H);
                Fill(d, C(0xCE, 0x11, 0x26), 0, 0, W, H / 2);
                break;
            case "VN":
                Fill(d, C(0xDA, 0x25, 0x1D), 0, 0, W, H);
                Star(d, C(0xFF, 0xCD, 0x00), 10, 7.8, 4.3, 0);
                break;
            case "TH":
                Fill(d, C(0xA5, 0x19, 0x31), 0, 0, W, H);
                Fill(d, White, 0, H / 6, W, H * 4 / 6);
                Fill(d, C(0x2D, 0x2A, 0x4A), 0, H / 3, W, H / 3);
                break;
            case "SE":
                Fill(d, C(0x00, 0x6A, 0xA7), 0, 0, W, H);
                Fill(d, C(0xFE, 0xCC, 0x00), 5.5, 0, 2.6, H);
                Fill(d, C(0xFE, 0xCC, 0x00), 0, 6.2, W, 2.6);
                break;
        }
    }

    private static Brush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private static Pen Pen(Color c, double t) { var p = new Pen(B(c), t); p.Freeze(); return p; }

    private static void Fill(DrawingCollection d, Color c, double x, double y, double w, double h) =>
        d.Add(new GeometryDrawing(B(c), null, new RectangleGeometry(new Rect(x, y, w, h))));

    private static void Dot(DrawingCollection d, Color c, double x, double y, double r) =>
        d.Add(new GeometryDrawing(B(c), null, new EllipseGeometry(new Point(x, y), r, r)));

    private static void Shape(DrawingCollection d, Color c, string path) =>
        d.Add(new GeometryDrawing(B(c), null, Geometry.Parse(path)));

    private static void HStripes(DrawingCollection d, Color top, Color middle, Color bottom)
    {
        Fill(d, top, 0, 0, W, H / 3);
        Fill(d, middle, 0, H / 3, W, H / 3);
        Fill(d, bottom, 0, 2 * H / 3, W, H / 3 + 0.01);
    }

    private static void VStripes(DrawingCollection d, Color left, Color middle, Color right)
    {
        Fill(d, left, 0, 0, W / 3, H);
        Fill(d, middle, W / 3, 0, W / 3, H);
        Fill(d, right, 2 * W / 3, 0, W / 3 + 0.01, H);
    }

    /// <summary>A five-pointed star centred on (x, y), turned by <paramref name="degrees"/>.</summary>
    private static void Star(DrawingCollection d, Color c, double x, double y, double r, double degrees)
    {
        var figure = new PathFigure { IsClosed = true, IsFilled = true };
        for (var i = 0; i < 10; i++)
        {
            var radius = i % 2 == 0 ? r : r * 0.38;
            var a = (degrees - 90 + i * 36) * Math.PI / 180;
            var p = new Point(x + radius * Math.Cos(a), y + radius * Math.Sin(a));
            if (i == 0) figure.StartPoint = p; else figure.Segments.Add(new LineSegment(p, true));
        }
        d.Add(new GeometryDrawing(B(c), null, new PathGeometry(new[] { figure })));
    }

    /// <summary>Three short bars, turned, as the corner marks on the Korean flag.</summary>
    private static void Trigram(DrawingCollection d, double x, double y, double degrees)
    {
        var group = new GeometryGroup { Transform = new RotateTransform(degrees, x, y) };
        for (var i = -1; i <= 1; i++) group.Children.Add(new RectangleGeometry(new Rect(x - 1.3, y + i * 0.75 - 0.25, 2.6, 0.5)));
        d.Add(new GeometryDrawing(B(Black), null, group));
    }
}
