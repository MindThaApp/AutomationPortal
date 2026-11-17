namespace AutomationPortal.App.Logic;

/// <summary>A window rectangle in physical pixels (virtual-desktop coordinates).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

/// <summary>Decides whether a remembered window place can be used again on the screens that exist now.</summary>
public static class SavedWindowBounds
{
    private const int MinWidth = 300, MinHeight = 200;
    private const int MinVisibleWidth = 120, MinVisibleHeight = 60;

    /// <summary>
    /// The remembered rectangle fitted into a visible work area, or null when it should be ignored
    /// (never saved, minimized or off-screen values, nonsense sizes, no screen it overlaps enough).
    /// </summary>
    public static PixelRect? Fit(PixelRect? saved, IReadOnlyList<PixelRect> workAreas)
    {
        if (saved is not { } r || workAreas.Count == 0) return null;
        if (r.Width < MinWidth || r.Height < MinHeight || r.Width > 100_000 || r.Height > 100_000) return null;

        PixelRect? best = null;
        long bestArea = 0;
        foreach (var w in workAreas)
        {
            var ow = Math.Min(r.Right, w.Right) - Math.Max(r.X, w.X);
            var oh = Math.Min(r.Bottom, w.Bottom) - Math.Max(r.Y, w.Y);
            if (ow < MinVisibleWidth || oh < MinVisibleHeight) continue;
            if ((long)ow * oh > bestArea) { bestArea = (long)ow * oh; best = w; }
        }
        if (best is not { } area) return null;

        var width = Math.Min(r.Width, area.Width);
        var height = Math.Min(r.Height, area.Height);
        var x = Math.Clamp(r.X, area.X, area.Right - width);
        var y = Math.Clamp(r.Y, area.Y, area.Bottom - height);
        return new PixelRect(x, y, width, height);
    }
}
