using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.Core;

namespace AutomationPortal.App.Dialogs;

/// <summary>
/// Full-screen overlays (one per monitor) for picking a screen region or a point. All coordinates handed back
/// are physical pixels in virtual-desktop space, which is what the screen actions expect. The process is
/// PerMonitorV2 DPI aware, so GetCursorPos already returns physical pixels on every monitor.
/// </summary>
public sealed class ScreenSelection
{
    private readonly bool _regionMode;
    private readonly List<OverlayWindow> _overlays = new();
    private readonly TaskCompletionSource<ScreenRect?> _tcs = new();
    private (int X, int Y)? _start;
    private bool _finished;

    private ScreenSelection(bool regionMode) { _regionMode = regionMode; }

    public static async Task<ScreenRect?> SelectRegionAsync(CancellationToken ct)
    {
        var s = new ScreenSelection(true);
        return await s.RunAsync(ct);
    }

    public static async Task<ScreenPoint?> SelectPointAsync(CancellationToken ct)
    {
        var s = new ScreenSelection(false);
        var r = await s.RunAsync(ct);
        return r is { } rect ? new ScreenPoint(rect.X, rect.Y) : null;
    }

    private Task<ScreenRect?> RunAsync(CancellationToken ct)
    {
        var cursorMonitor = Monitors.FromCursor();
        foreach (var m in Monitors.All())
        {
            var o = new OverlayWindow(m, this, _regionMode);
            _overlays.Add(o);
            o.Show();
            if (m.Handle == cursorMonitor.Handle) { o.Activate(); o.Focus(); }
        }
        if (_overlays.Count > 0 && !_overlays.Any(o => o.IsActive)) _overlays[0].Activate();
        if (ct.CanBeCanceled) ct.Register(() => Application.Current.Dispatcher.BeginInvoke(() => Finish(null)));
        return _tcs.Task;
    }

    private void Finish(ScreenRect? result)
    {
        if (_finished) return;
        _finished = true;
        Mouse.Capture(null);
        foreach (var o in _overlays) { try { o.Close(); } catch { } }
        _tcs.TrySetResult(result);
    }

    private void OnDown((int X, int Y) p)
    {
        if (!_regionMode) { Finish(new ScreenRect(p.X, p.Y, 0, 0)); return; }
        _start = p;
    }

    private void OnMove((int X, int Y) p)
    {
        if (_start is not { } s) return;
        var r = Normalize(s, p);
        foreach (var o in _overlays) o.ShowSelection(r);
    }

    private void OnUp((int X, int Y) p)
    {
        if (_start is not { } s) return;
        var r = Normalize(s, p);
        _start = null;
        if (r.Width < 3 || r.Height < 3) { foreach (var o in _overlays) o.ShowSelection(null); return; }
        Finish(r);
    }

    private static ScreenRect Normalize((int X, int Y) a, (int X, int Y) b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private sealed class OverlayWindow : Window
    {
        private readonly MonitorInfo _mon;
        private readonly ScreenSelection _owner;
        private readonly System.Windows.Shapes.Path _dim = new() { Fill = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), IsHitTestVisible = false };
        private readonly Rectangle _frame = new() { Stroke = Brushes.White, StrokeThickness = 1.5, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        private readonly Border _size = new() { Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(220, 30, 30, 30)), Padding = new Thickness(8, 3, 8, 3), IsHitTestVisible = false };
        private readonly TextBlock _sizeText = new() { Foreground = Brushes.White, FontSize = 12 };
        private readonly Canvas _canvas = new();

        public OverlayWindow(MonitorInfo mon, ScreenSelection owner, bool region)
        {
            _mon = mon; _owner = owner;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            Cursor = Cursors.Cross;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = mon.X; Top = mon.Y; Width = 100; Height = 100;
            UseLayoutRounding = true;

            var root = new Grid { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
            root.Children.Add(_dim);
            root.Children.Add(_canvas);
            _canvas.Children.Add(_frame);
            _size.Child = _sizeText;
            _canvas.Children.Add(_size);

            var hint = new Border
            {
                CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 28, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
                Background = new SolidColorBrush(Color.FromArgb(230, 32, 32, 32)),
                Child = new TextBlock
                {
                    Foreground = Brushes.White, FontSize = 14,
                    Text = region ? "Drag to choose an area. Press Esc to cancel." : "Click the spot you want. Press Esc to cancel.",
                },
            };
            root.Children.Add(hint);
            Content = root;

            SourceInitialized += (_, _) =>
            {
                PlaceExactly();
                if (PresentationSource.FromVisual(this) is HwndSource src)
                    src.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
                    {
                        // Moving onto a monitor with another DPI makes Windows suggest a new size; put it back.
                        if (msg == Native.WM_DPICHANGED) Dispatcher.BeginInvoke(PlaceExactly);
                        return IntPtr.Zero;
                    });
            };
            Loaded += (_, _) => { PlaceExactly(); RefreshDim(null); };
            SizeChanged += (_, _) => RefreshDim(null);
            KeyDown += (_, e) => { if (e.Key == Key.Escape) owner.Finish(null); };
            MouseRightButtonDown += (_, _) => owner.Finish(null);
            MouseLeftButtonDown += (_, e) => { CaptureMouse(); owner.OnDown(Monitors.CursorPixels()); e.Handled = true; };
            MouseMove += (_, _) => owner.OnMove(Monitors.CursorPixels());
            MouseLeftButtonUp += (_, e) => { ReleaseMouseCapture(); owner.OnUp(Monitors.CursorPixels()); e.Handled = true; };
        }

        private void PlaceExactly()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, _mon.X, _mon.Y, _mon.Width, _mon.Height, Native.SWP_NOACTIVATE);
        }

        private double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;

        private void RefreshDim(Rect? hole)
        {
            var full = new RectangleGeometry(new Rect(0, 0, Math.Max(1, ActualWidth), Math.Max(1, ActualHeight)));
            _dim.Data = hole is { } h
                ? new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(h))
                : full;
        }

        public void ShowSelection(ScreenRect? r)
        {
            if (r is not { } rect) { _frame.Visibility = Visibility.Collapsed; _size.Visibility = Visibility.Collapsed; RefreshDim(null); return; }
            var left = Math.Max(rect.X, _mon.X); var top = Math.Max(rect.Y, _mon.Y);
            var right = Math.Min(rect.X + rect.Width, _mon.X + _mon.Width); var bottom = Math.Min(rect.Y + rect.Height, _mon.Y + _mon.Height);
            if (right <= left || bottom <= top) { _frame.Visibility = Visibility.Collapsed; _size.Visibility = Visibility.Collapsed; RefreshDim(null); return; }
            var s = Scale;
            var local = new Rect((left - _mon.X) / s, (top - _mon.Y) / s, (right - left) / s, (bottom - top) / s);
            Canvas.SetLeft(_frame, local.X); Canvas.SetTop(_frame, local.Y);
            _frame.Width = local.Width; _frame.Height = local.Height; _frame.Visibility = Visibility.Visible;
            RefreshDim(local);
            _sizeText.Text = $"{rect.Width} × {rect.Height}";
            Canvas.SetLeft(_size, local.X);
            Canvas.SetTop(_size, Math.Max(0, local.Y - 30));
            _size.Visibility = Visibility.Visible;
        }
    }
}
