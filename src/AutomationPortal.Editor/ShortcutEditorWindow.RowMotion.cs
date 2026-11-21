using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using AutomationPortal.Core;
using AutomationPortal.Editor.UI;
using AutomationPortal.Editor.ViewModels;

namespace AutomationPortal.Editor;

/// <summary>
/// Motion for the step list: an added step opens and fades in, a removed one fades out while the rows below close the gap,
/// a moved one slides to its new place. Visual only: the rows have already changed when this runs.
/// </summary>
internal sealed partial class ShortcutEditorWindow
{
    private sealed record Ghost(Point At, Size Size, BitmapSource Picture);

    private sealed class RowSnapshot
    {
        public Dictionary<ActionRowViewModel, double> Ys { get; } = new();
        public HashSet<ActionRowViewModel> All { get; } = new();
        public List<Ghost> Ghosts { get; } = new();
        public double ScrollOffset { get; init; }
    }

    private RowSnapshot? _rowSnapshot;

    private ScrollViewer? ListScroller => Ui.Descendants<ScrollViewer>(ActionList).FirstOrDefault();

    private void InitRowMotion() => _vm.RowsChanging += OnRowsChanging;

    private void OnRowsChanging(IReadOnlyList<ActionRowViewModel> removing)
    {
        if (Motion.Reduced || !IsLoaded || _rowSnapshot is not null) return;
        var added = _vm.Doc.Actions.Count - (_vm.Rows.Count - removing.Count);
        if (!RowMotionPlan.IsSmallChange(Math.Max(0, added), removing.Count)) return;

        var snap = new RowSnapshot { ScrollOffset = ListScroller?.VerticalOffset ?? 0 };
        foreach (var r in _vm.Rows)
        {
            snap.All.Add(r);
            if (ActionList.ItemContainerGenerator.ContainerFromItem(r) is not ListBoxItem c || !c.IsVisible || c.ActualHeight <= 0) continue;
            var at = c.TransformToVisual(DropOverlay).Transform(new Point());
            snap.Ys[r] = at.Y;
            if (removing.Contains(r)) snap.Ghosts.Add(new Ghost(at, new Size(c.ActualWidth, c.ActualHeight), Capture(c)));
        }
        _rowSnapshot = snap;

        void OnLayout(object? s, EventArgs e)
        {
            ActionList.LayoutUpdated -= OnLayout;
            var taken = _rowSnapshot;
            _rowSnapshot = null;
            if (taken is not null) PlayRowMotion(taken);
        }
        ActionList.LayoutUpdated += OnLayout;
    }

    private BitmapSource Capture(FrameworkElement element)
    {
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(element.ActualWidth * scale)), Math.Max(1, (int)Math.Ceiling(element.ActualHeight * scale)),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        bitmap.Freeze();
        return bitmap;
    }

    private void PlayRowMotion(RowSnapshot snap)
    {
        if (Motion.Reduced) return;
        var added = _vm.Rows.Where(r => !snap.All.Contains(r)).ToList();

        foreach (var ghost in snap.Ghosts) FadeGhost(ghost);

        if (added.Count > 0)
        {
            foreach (var row in added)
                if (ActionList.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem c) OpenRow(c);
            return;
        }

        if (Math.Abs((ListScroller?.VerticalOffset ?? 0) - snap.ScrollOffset) > 0.5) return;
        var after = new Dictionary<ActionRowViewModel, double>();
        foreach (var r in snap.Ys.Keys)
            if (ActionList.ItemContainerGenerator.ContainerFromItem(r) is ListBoxItem c && c.IsVisible)
                after[r] = c.TransformToVisual(DropOverlay).Transform(new Point()).Y;
        foreach (var (row, shift) in RowMotionPlan.Shifts(snap.Ys, after))
            if (ActionList.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem c) SlideRow(c, shift);
    }

    /// <summary>A step being dragged lifts: a small shadow and a slightly larger size. Returns what to hand to <see cref="SettleRows"/>.</summary>
    private List<ListBoxItem> LiftRows(IEnumerable<ActionRowViewModel> rows)
    {
        var lifted = new List<ListBoxItem>();
        foreach (var row in rows)
        {
            if (ActionList.ItemContainerGenerator.ContainerFromItem(row) is not ListBoxItem c) continue;
            lifted.Add(c);
            c.Effect = HighContrast.Shadow(14, 3, 0.3);
            if (Motion.Reduced) continue;
            var scale = new ScaleTransform(1, 1);
            c.RenderTransformOrigin = new Point(0.5, 0.5);
            c.RenderTransform = scale;
            Motion.Begin(scale, ScaleTransform.ScaleXProperty, Anim(1, 1.015, MotionRole.Fast));
            Motion.Begin(scale, ScaleTransform.ScaleYProperty, Anim(1, 1.015, MotionRole.Fast));
        }
        return lifted;
    }

    /// <summary>The dragged steps settle back: the shadow goes and the size returns.</summary>
    private static void SettleRows(IReadOnlyList<ListBoxItem> lifted)
    {
        foreach (var c in lifted)
        {
            c.Effect = null;
            if (c.RenderTransform is ScaleTransform scale)
            {
                void Done() { if (ReferenceEquals(c.RenderTransform, scale)) c.RenderTransform = Transform.Identity; }
                var back = Anim(scale.ScaleX, 1, MotionRole.Enter, Done);
                Motion.Begin(scale, ScaleTransform.ScaleXProperty, back);
                Motion.Begin(scale, ScaleTransform.ScaleYProperty, Anim(scale.ScaleY, 1, MotionRole.Enter));
            }
        }
    }

    /// <summary>A step that was just added gets the keyboard cursor in its first field.</summary>
    private void FocusNewStep(ActionInstance action)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var row = _vm.Rows.FirstOrDefault(r => ReferenceEquals(r.Action, action));
            if (row is null || ActionList.ItemContainerGenerator.ContainerFromItem(row) is not ListBoxItem c) return;
            var field = Ui.Descendants<FrameworkElement>(c).FirstOrDefault(e => e is TextBoxBase or System.Windows.Controls.ComboBox or TokenView or HotkeyRecorder && e.Focusable && e.IsVisible && e.IsEnabled);
            (field ?? c).Focus();
        }), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private static DoubleAnimation Anim(double from, double to, MotionRole role, Action? done = null)
    {
        var a = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(Motion.Nominal(role))) { EasingFunction = Motion.EasingFor(role) };
        if (done is not null) a.Completed += (_, _) => done();
        return a;
    }

    private static void OpenRow(ListBoxItem container)
    {
        var scale = new ScaleTransform(1, 0);
        container.LayoutTransform = scale;
        Motion.Begin(container, OpacityProperty, Anim(0, 1, MotionRole.Enter, () => container.BeginAnimation(OpacityProperty, null)));
        Motion.Begin(scale, ScaleTransform.ScaleYProperty, Anim(0, 1, MotionRole.Enter, () =>
        {
            if (ReferenceEquals(container.LayoutTransform, scale)) container.LayoutTransform = Transform.Identity;
        }));
    }

    private static void SlideRow(ListBoxItem container, double from)
    {
        var move = new TranslateTransform(0, from);
        container.RenderTransform = move;
        var slide = Anim(from, 0, MotionRole.Fast, () =>
        {
            if (ReferenceEquals(container.RenderTransform, move)) container.RenderTransform = Transform.Identity;
        });
        slide.EasingFunction = Motion.EaseOut;   // the Fast role has no easing of its own; a row arriving slows down
        Motion.Begin(move, TranslateTransform.YProperty, slide);
    }

    private void FadeGhost(Ghost ghost)
    {
        var scale = new ScaleTransform(1, 1);
        var image = new Image
        {
            Source = ghost.Picture, Width = ghost.Size.Width, Height = ghost.Size.Height, IsHitTestVisible = false,
            RenderTransform = scale, RenderTransformOrigin = new Point(0.5, 0.5),
        };
        Canvas.SetLeft(image, ghost.At.X);
        Canvas.SetTop(image, ghost.At.Y);
        DropOverlay.Children.Add(image);
        void Done() => DropOverlay.Children.Remove(image);
        Motion.Begin(image, OpacityProperty, Anim(1, 0, MotionRole.Leave, Done));
        Motion.Begin(scale, ScaleTransform.ScaleYProperty, Anim(1, 0.85, MotionRole.Leave));
    }
}
