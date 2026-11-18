using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using Image = System.Windows.Controls.Image;
using System.Windows.Media.Imaging;
using AutomationPortal.Editor;

namespace AutomationPortal.App.Infrastructure;

/// <summary>
/// Moves in the tile grid: a tile that leaves fades and shrinks, one that arrives (or comes back with Undo) does the same backwards,
/// and the others slide to their new place. All lengths come from <see cref="Motion"/>, so with animation effects off nothing moves.
/// </summary>
public sealed class TileMotion
{
    private readonly ItemsControl _list;
    private readonly Dictionary<object, Point> _before;
    private readonly List<(Image Image, Point At)> _ghosts;
    private readonly Canvas _ghostLayer;

    private TileMotion(ItemsControl list, Dictionary<object, Point> before, List<(Image, Point)> ghosts, Canvas ghostLayer)
    {
        _list = list; _before = before; _ghosts = ghosts; _ghostLayer = ghostLayer;
    }

    /// <summary>Notes where every tile is now and takes a picture of each tile that is about to leave. Call before <see cref="Sync"/>.</summary>
    public static TileMotion Capture(ItemsControl list, IEnumerable<object> current, IReadOnlyCollection<object> desired, Canvas ghostLayer)
    {
        var keep = new HashSet<object>(desired);
        var before = new Dictionary<object, Point>();
        var ghosts = new List<(Image, Point)>();        foreach (var item in current)
        {
            if (list.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement c || !c.IsLoaded) continue;
            var at = LayoutInformation.GetLayoutSlot(c).TopLeft;
            before[item] = at;
            if (!keep.Contains(item) && c.ActualWidth > 0 && c.ActualHeight > 0)
            {
                var dpi = VisualTreeHelper.GetDpi(c);
                var bmp = new RenderTargetBitmap((int)Math.Ceiling(c.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(c.ActualHeight * dpi.DpiScaleY),
                    96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                bmp.Render(c);
                bmp.Freeze();
                ghosts.Add((new Image { Source = bmp, Width = c.ActualWidth, Height = c.ActualHeight, IsHitTestVisible = false, RenderTransformOrigin = new Point(0.5, 0.5) }, at));
            }
        }
        return new TileMotion(list, before, ghosts, ghostLayer);
    }

    /// <summary>Starts the movement. Call after <see cref="Sync"/>.</summary>
    public void Play()
    {
        foreach (var (image, at) in _ghosts)
        {
            Canvas.SetLeft(image, at.X);
            Canvas.SetTop(image, at.Y);
            var scale = new ScaleTransform(1, 1);
            image.RenderTransform = scale;
            _ghostLayer.Children.Add(image);
            Motion.Animate(image, UIElement.OpacityProperty, 1, 0, MotionRole.Tile, Motion.EaseIn);
            Motion.Animate(scale, ScaleTransform.ScaleXProperty, 1, 0.9, MotionRole.Tile, Motion.EaseIn);
            Motion.Animate(scale, ScaleTransform.ScaleYProperty, 1, 0.9, MotionRole.Tile, Motion.EaseIn);
            var img = image;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Motion.Nominal(MotionRole.Tile) + 40) };
            timer.Tick += (_, _) => { timer.Stop(); _ghostLayer.Children.Remove(img); };
            timer.Start();
        }

        _list.UpdateLayout();
        foreach (var item in _list.Items)
        {
            if (_list.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement c) continue;
            var transform = Transform(c);
            if (!_before.TryGetValue(item, out var old))
            {
                Motion.Animate(c, UIElement.OpacityProperty, 0, 1, MotionRole.Tile, Motion.EaseOut);
                Motion.Animate(transform.Scale, ScaleTransform.ScaleXProperty, 0.9, 1, MotionRole.Tile, Motion.EaseOut);
                Motion.Animate(transform.Scale, ScaleTransform.ScaleYProperty, 0.9, 1, MotionRole.Tile, Motion.EaseOut);
                continue;
            }
            var now = LayoutInformation.GetLayoutSlot(c).TopLeft;
            double dx = old.X - now.X + transform.Move.X, dy = old.Y - now.Y + transform.Move.Y;
            if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5) continue;
            Motion.Animate(transform.Move, TranslateTransform.XProperty, dx, 0, MotionRole.EnterLong, Motion.EaseOut);
            Motion.Animate(transform.Move, TranslateTransform.YProperty, dy, 0, MotionRole.EnterLong, Motion.EaseOut);
        }
    }

    private static (ScaleTransform Scale, TranslateTransform Move) Transform(FrameworkElement c)
    {
        if (c.RenderTransform is TransformGroup { Children.Count: 2 } g && g.Children[0] is ScaleTransform s && g.Children[1] is TranslateTransform t) return (s, t);
        var scale = new ScaleTransform(1, 1);
        var move = new TranslateTransform();
        c.RenderTransformOrigin = new Point(0.5, 0.5);
        c.RenderTransform = new TransformGroup { Children = { scale, move } };
        return (scale, move);
    }

    /// <summary>
    /// Makes <paramref name="items"/> equal to <paramref name="desired"/> with as few moves as it can: items that left are removed,
    /// new ones inserted, and a single tile that changed place is moved once. Existing tiles keep their controls.
    /// </summary>
    public static void Sync(ObservableCollection<object> items, IReadOnlyList<object> desired)
    {
        var keep = new HashSet<object>(desired);
        for (var i = items.Count - 1; i >= 0; i--)
            if (!keep.Contains(items[i])) items.RemoveAt(i);

        for (var i = 0; i < desired.Count; i++)
        {
            if (i < items.Count && ReferenceEquals(items[i], desired[i])) continue;
            var j = IndexOf(items, desired[i], i + 1);
            if (j < 0) { items.Insert(i, desired[i]); continue; }
            // Either bring desired[i] back to i, or push the tile sitting at i forward to where it belongs; take the one that keeps more in place.
            var k = IndexOf(desired, items[i], i + 1);
            if (k > i && k < items.Count && RunAfterForward(items, desired, i, k) > RunAfterBack(items, desired, i, j)) items.Move(i, k);
            else items.Move(j, i);
            if (i < items.Count && !ReferenceEquals(items[i], desired[i])) { var m = IndexOf(items, desired[i], i + 1); if (m > 0) items.Move(m, i); }
        }
    }

    private static int IndexOf(IReadOnlyList<object> list, object item, int from)
    {
        for (var i = from; i < list.Count; i++) if (ReferenceEquals(list[i], item)) return i;
        return -1;
    }

    private static int RunAfterBack(IReadOnlyList<object> items, IReadOnlyList<object> desired, int i, int j)
    {
        var copy = new List<object>(items);
        var x = copy[j]; copy.RemoveAt(j); copy.Insert(i, x);
        return Run(copy, desired, i);
    }

    private static int RunAfterForward(IReadOnlyList<object> items, IReadOnlyList<object> desired, int i, int k)
    {
        var copy = new List<object>(items);
        var x = copy[i]; copy.RemoveAt(i); copy.Insert(k, x);
        return Run(copy, desired, i);
    }

    private static int Run(List<object> copy, IReadOnlyList<object> desired, int from)
    {
        var n = 0;
        while (from + n < copy.Count && from + n < desired.Count && ReferenceEquals(copy[from + n], desired[from + n])) n++;
        return n;
    }
}
