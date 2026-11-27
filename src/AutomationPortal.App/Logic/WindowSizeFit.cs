namespace AutomationPortal.App.Logic;

/// <summary>Sizes in device-independent pixels (DIPs).</summary>
public readonly record struct DipSize(double Width, double Height);

/// <summary>Keeps a window's opening size and minimum size inside the work area of the monitor it opens on.</summary>
public static class WindowSizeFit
{
    /// <summary>The work area of a monitor in DIPs, from its physical work area and its scale (1.5 at 150 %).</summary>
    public static DipSize WorkAreaDips(int workWidthPx, int workHeightPx, double scale)
    {
        if (scale <= 0) scale = 1;
        return new DipSize(workWidthPx / scale, workHeightPx / scale);
    }

    /// <summary>
    /// Clamps the width and height to the work area, and lowers the minimum size so it never exceeds the clamped size
    /// (a window may not be forced larger than the screen it is on). A work area with no size leaves everything as it was.
    /// </summary>
    public static (DipSize Size, DipSize Min) Clamp(DipSize desired, DipSize min, DipSize work)
    {
        if (work.Width <= 0 || work.Height <= 0) return (desired, min);
        var w = Math.Floor(Math.Min(desired.Width, work.Width));
        var h = Math.Floor(Math.Min(desired.Height, work.Height));
        return (new DipSize(w, h), new DipSize(Math.Min(min.Width, w), Math.Min(min.Height, h)));
    }
}
