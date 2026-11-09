namespace AutomationPortal.Actions.Windows.Apps;

/// <summary>A rectangle in screen pixels.</summary>
public readonly record struct Box(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public long Area => (long)Math.Max(0, Width) * Math.Max(0, Height);

    public Box Intersect(Box other)
    {
        var x = Math.Max(X, other.X);
        var y = Math.Max(Y, other.Y);
        var r = Math.Min(Right, other.Right);
        var b = Math.Min(Bottom, other.Bottom);
        return r > x && b > y ? new Box(x, y, r - x, b - y) : new Box(x, y, 0, 0);
    }
}

public enum SnapPosition
{
    LeftHalf, RightHalf, TopHalf, BottomHalf,
    TopLeft, TopRight, BottomLeft, BottomRight,
    LeftThird, MiddleThird, RightThird,
    LeftTwoThirds, RightTwoThirds,
    Center,
}

/// <summary>Pure window/monitor maths (no Windows calls) so it can be unit-tested.</summary>
public static class WindowGeometry
{
    public static Box Snap(Box work, SnapPosition position)
    {
        int halfW = work.Width / 2, halfH = work.Height / 2;
        int thirdW = work.Width / 3;
        int rightX = work.X + halfW, rightW = work.Width - halfW;
        int bottomY = work.Y + halfH, bottomH = work.Height - halfH;
        return position switch
        {
            SnapPosition.LeftHalf => new Box(work.X, work.Y, halfW, work.Height),
            SnapPosition.RightHalf => new Box(rightX, work.Y, rightW, work.Height),
            SnapPosition.TopHalf => new Box(work.X, work.Y, work.Width, halfH),
            SnapPosition.BottomHalf => new Box(work.X, bottomY, work.Width, bottomH),
            SnapPosition.TopLeft => new Box(work.X, work.Y, halfW, halfH),
            SnapPosition.TopRight => new Box(rightX, work.Y, rightW, halfH),
            SnapPosition.BottomLeft => new Box(work.X, bottomY, halfW, bottomH),
            SnapPosition.BottomRight => new Box(rightX, bottomY, rightW, bottomH),
            SnapPosition.LeftThird => new Box(work.X, work.Y, thirdW, work.Height),
            SnapPosition.MiddleThird => new Box(work.X + thirdW, work.Y, thirdW, work.Height),
            SnapPosition.RightThird => new Box(work.X + 2 * thirdW, work.Y, work.Width - 2 * thirdW, work.Height),
            SnapPosition.LeftTwoThirds => new Box(work.X, work.Y, work.Width - thirdW, work.Height),
            SnapPosition.RightTwoThirds => new Box(work.X + thirdW, work.Y, work.Width - thirdW, work.Height),
            SnapPosition.Center => CenterIn(work, new Box(0, 0, work.Width * 2 / 3, work.Height * 2 / 3)),
            _ => work,
        };
    }

    /// <summary>Places a window of the same size in the middle of the work area (shrinks it first when it doesn't fit).</summary>
    public static Box CenterIn(Box work, Box window)
    {
        var w = Math.Min(window.Width, work.Width);
        var h = Math.Min(window.Height, work.Height);
        return new Box(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2, w, h);
    }

    /// <summary>Moves a window to another monitor keeping its relative position and size.</summary>
    public static Box MoveToMonitor(Box window, Box fromWork, Box toWork)
    {
        if (fromWork.Width <= 0 || fromWork.Height <= 0) return CenterIn(toWork, window);
        double sx = (double)toWork.Width / fromWork.Width, sy = (double)toWork.Height / fromWork.Height;
        var w = Math.Min((int)Math.Round(window.Width * sx), toWork.Width);
        var h = Math.Min((int)Math.Round(window.Height * sy), toWork.Height);
        var x = toWork.X + (int)Math.Round((window.X - fromWork.X) * sx);
        var y = toWork.Y + (int)Math.Round((window.Y - fromWork.Y) * sy);
        return KeepInside(new Box(x, y, w, h), toWork);
    }

    /// <summary>Slides a window so it lies inside the work area (never changes the size unless it is bigger).</summary>
    public static Box KeepInside(Box window, Box work)
    {
        var w = Math.Min(window.Width, work.Width);
        var h = Math.Min(window.Height, work.Height);
        var x = Math.Clamp(window.X, work.X, work.Right - w);
        var y = Math.Clamp(window.Y, work.Y, work.Bottom - h);
        return new Box(x, y, w, h);
    }

    /// <summary>
    /// Windows 10/11 windows have invisible resize borders, so a window placed exactly on the work area would leave gaps.
    /// Grows <paramref name="target"/> by the difference between the full window rectangle and its visible frame.
    /// </summary>
    public static Box AdjustForInvisibleBorders(Box target, Box windowRect, Box visibleFrame)
    {
        var left = visibleFrame.X - windowRect.X;
        var top = visibleFrame.Y - windowRect.Y;
        var right = windowRect.Right - visibleFrame.Right;
        var bottom = windowRect.Bottom - visibleFrame.Bottom;
        return new Box(target.X - left, target.Y - top, target.Width + left + right, target.Height + top + bottom);
    }

    /// <summary>Index of the work area the window overlaps most (nearest by centre when it overlaps none).</summary>
    public static int MonitorIndexFor(Box window, IReadOnlyList<Box> works)
    {
        if (works.Count == 0) return -1;
        var best = -1;
        long bestArea = 0;
        for (var i = 0; i < works.Count; i++)
        {
            var area = window.Intersect(works[i]).Area;
            if (area > bestArea) { bestArea = area; best = i; }
        }
        if (best >= 0) return best;
        double cx = window.X + window.Width / 2.0, cy = window.Y + window.Height / 2.0;
        return Enumerable.Range(0, works.Count)
            .OrderBy(i => Math.Pow(works[i].X + works[i].Width / 2.0 - cx, 2) + Math.Pow(works[i].Y + works[i].Height / 2.0 - cy, 2))
            .First();
    }

    /// <summary>Monitor order left to right (then top to bottom), as users picture them.</summary>
    public static IReadOnlyList<int> OrderLeftToRight(IReadOnlyList<Box> monitors) =>
        Enumerable.Range(0, monitors.Count).OrderBy(i => monitors[i].X).ThenBy(i => monitors[i].Y).ToList();

    /// <summary>The index (in <paramref name="monitors"/>) of the next monitor to the right, wrapping around; step -1 goes left.</summary>
    public static int NextMonitor(IReadOnlyList<Box> monitors, int current, int step = 1)
    {
        if (monitors.Count == 0) return -1;
        var order = OrderLeftToRight(monitors).ToList();
        var pos = order.IndexOf(current);
        if (pos < 0) pos = 0;
        return order[((pos + step) % order.Count + order.Count) % order.Count];
    }
}
