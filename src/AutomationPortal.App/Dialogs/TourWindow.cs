using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using AutomationPortal.Editor;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Dialogs;

/// <summary>
/// "Explore Automation Portal": the feature tour, in the What's new look. One stop at a time: a screenshot, a title, a sentence or two and
/// Show me (which closes the tour and opens that page). Back and Next, step dots, Skip tour on every stop; Esc closes, Enter is Next.
/// It never opens by itself. It is its own window, sized so the picture, the text and the buttons show without scrolling (the middle scrolls only on a small screen).
/// </summary>
public sealed class TourWindow : FluentWindow
{
    private const double Inset = 32;
    private readonly AppHost _host;
    private readonly IReadOnlyList<TourStop> _stops;
    private readonly TourNav _nav;
    private readonly Dictionary<string, ImageSource?> _images = new();
    private readonly List<Rectangle> _dots = new();
    private readonly StackPanel _dotRow = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
    private readonly ContentControl _stage = new() { IsTabStop = false };
    private readonly Wpf.Ui.Controls.Button _back = new(), _next = new(), _skip = new(), _showMe = new();
    private readonly double _imageHeight;

    public TourWindow(AppHost host, IReadOnlyList<TourStop> stops)
    {
        _host = host;
        _stops = stops;
        _nav = new TourNav(stops.Count);
        Title = Strings.Get("Tour_Title");
        var work = SystemParameters.WorkArea;
        var grow = Math.Max(1, host.Settings.Current.TextScale / 100.0);
        Width = Math.Min(680 * grow, work.Width * 0.95);
        MinWidth = Math.Min(460, Width);
        MaxHeight = work.Height * 0.95;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        WindowBackdropType = WindowBackdropType.None;
        ExtendsContentIntoTitleBar = true;
        UseLayoutRounding = true;
        FontSize = Fs(TypeRamp.Body);
        SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
        SetResourceReference(ForegroundProperty, "DsTextPrimaryBrush");
        FontFamily = (FontFamily)Application.Current.FindResource("FlowFont");
        AutomationProperties.SetAutomationId(this, "TourWindow");
        _imageHeight = Math.Max(160, (Width - 2 * Inset) * 0.5);   // screenshots are captured at 2:1

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var bar = new TitleBar { Title = Title, ShowMinimize = false, ShowMaximize = false, CanMaximize = false };
        bar.Icon = new ImageIcon { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/automationportal.png")) };
        root.Children.Add(bar);

        _stage.Margin = new Thickness(Inset, 8, Inset, 12);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _stage };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        for (var i = 0; i < _stops.Count; i++)
        {
            var dot = new Rectangle { Height = 8, Width = 8, RadiusX = 4, RadiusY = 4, Margin = new Thickness(3, 0, 3, 0) };
            _dots.Add(dot);
            _dotRow.Children.Add(dot);
        }
        AutomationProperties.SetAutomationId(_dotRow, "TourProgress");
        Grid.SetRow(_dotRow, 2);
        root.Children.Add(_dotRow);

        var divider = new Border { Height = 1 };
        divider.SetResourceReference(Border.BackgroundProperty, "DsStrokeBrush");
        Grid.SetRow(divider, 3);
        root.Children.Add(divider);

        // Left: Skip tour as a text button. Right: Back, Show me, then the one filled main button.
        var row = new Grid { Margin = new Thickness(Inset, 14, Inset, 20) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _skip.Appearance = ControlAppearance.Transparent;
        _skip.BorderThickness = new Thickness(0);
        _skip.Padding = new Thickness(0, 6, 14, 6);
        _skip.HorizontalAlignment = HorizontalAlignment.Left;
        _skip.VerticalAlignment = VerticalAlignment.Center;
        _skip.SetResourceReference(Control.ForegroundProperty, "DsAccentTextBrush");
        Tip(_skip, "Tour_Skip", "Tour_SkipTip", "TourSkip");
        _skip.Click += (_, _) => { _nav.Skip(); Close(); };
        row.Children.Add(_skip);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        _back.MinWidth = 88;
        _back.Margin = new Thickness(0, 0, 8, 0);
        Tip(_back, "Tour_Back", "Tour_BackTip", "TourBack");
        _back.Click += (_, _) => { if (_nav.Back()) ShowStop(false); };
        _showMe.MinWidth = 96;
        _showMe.Margin = new Thickness(0, 0, 8, 0);
        _showMe.Icon = new SymbolIcon(SymbolRegular.Open24);
        Tip(_showMe, "Tour_ShowMe", "Tour_ShowMeTip", "TourShowMe");
        _showMe.Click += (_, _) => ShowMe();
        _next.Appearance = ControlAppearance.Primary;
        _next.IsDefault = true;
        _next.MinWidth = 110;
        _next.Click += (_, _) => { if (_nav.Next()) ShowStop(true); else Close(); };
        right.Children.Add(_back);
        right.Children.Add(_showMe);
        right.Children.Add(_next);
        Grid.SetColumn(right, 1);
        row.Children.Add(right);
        foreach (var b in new[] { _skip, _back, _showMe, _next }) b.FontSize = Fs(TypeRamp.Body);
        Grid.SetRow(row, 4);
        root.Children.Add(row);
        Content = root;

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Loaded += (_, _) => { ShowStop(null); _next.Focus(); };
        DialogMotion.Attach(this);
    }

    /// <summary>Opens the window (centred on the main window when it is showing) and returns at once.</summary>
    public void ShowFor(Window? owner)
    {
        if (owner is { IsVisible: true })
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Show();
            WindowPlacement.BringToFront(this);
        }
        else WindowPlacement.ShowCentered(this);
    }

    private double Fs(double size) => AppearanceMath.FontSize(size, _host.Settings.Current.TextScale);

    private static void Tip(Wpf.Ui.Controls.Button b, string labelKey, string tipKey, string automationId)
    {
        b.Content = Strings.Get(labelKey);
        b.ToolTip = Strings.Get(tipKey);
        AutomationProperties.SetHelpText(b, Strings.Get(tipKey));
        AutomationProperties.SetAutomationId(b, automationId);
    }

    private void ShowMe()
    {
        var target = _stops[_nav.Current].ShowMe;
        var nav = target is null ? null : WhatsNewWindow.NavFor(target);
        Close();
        if (nav is not null) _host.ShowMainWindow(nav);
    }

    private void ShowStop(bool? forward)
    {
        var i = _nav.Current;
        var stop = _stops[i];
        var dark = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme() == Wpf.Ui.Appearance.ApplicationTheme.Dark;

        var panel = new StackPanel();
        var frame = new Border { Height = _imageHeight, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), ClipToBounds = true };
        frame.SetResourceReference(Border.BackgroundProperty, "FlowEmptyGradient");
        frame.SetResourceReference(Border.BorderBrushProperty, "DsStrokeBrush");
        var source = ImageFor(stop);
        frame.Child = source is null
            ? new SymbolIcon { Symbol = SymbolRegular.Image24, FontSize = 40, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ToolTip = Strings.Get("Tour_ImageMissing") }
            : new System.Windows.Controls.Image { Source = source, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8) };
        AutomationProperties.SetName(frame, stop.Title);
        panel.Children.Add(frame);
        var title = Ui.Text(stop.Title, false, Fs(TypeRamp.PanelTitle), FontWeights.SemiBold);
        title.Margin = new Thickness(0, 16, 0, 0);
        panel.Children.Add(title);
        var body = WelcomeWindow.SecondaryText(stop.Body, Fs(TypeRamp.Body), dark);
        body.Margin = new Thickness(0, 4, 0, 0);
        body.MinHeight = Fs(TypeRamp.Body) * 1.4 * 3;   // room for three lines, so the window does not jump between stops
        panel.Children.Add(body);
        _stage.Content = panel;
        if (forward is bool dir) PageSlide.Play(panel, dir);

        _back.Visibility = _nav.CanGoBack ? Visibility.Visible : Visibility.Collapsed;
        _showMe.Visibility = stop.ShowMe is { } t && WhatsNewWindow.NavFor(t) is not null ? Visibility.Visible : Visibility.Collapsed;
        var last = _nav.IsLast;
        Tip(_next, last ? "Tour_Start" : "Tour_Next", last ? "Tour_StartTip" : "Tour_NextTip", "TourNext");
        for (var d = 0; d < _dots.Count; d++)
        {
            var active = d == i;
            _dots[d].SetResourceReference(Shape.FillProperty, active ? "DsAccentBrush" : "DsTextTertiaryBrush");
            var width = active ? 24.0 : 8.0;
            if (forward is null) _dots[d].Width = width;
            else Motion.Animate(_dots[d], WidthProperty, _dots[d].ActualWidth > 0 ? _dots[d].ActualWidth : _dots[d].Width, width, MotionRole.Enter, Motion.Settle);
        }
        AutomationProperties.SetName(_dotRow, string.Format(Strings.Get("Tour_ProgressFormat"), i + 1, _stops.Count));
        if (i + 1 < _stops.Count)
        {
            var nextStop = _stops[i + 1];
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () => ImageFor(nextStop));
        }
    }

    /// <summary>The stop's picture, loaded the first time it is needed, frozen and kept. Null when it can't be read (a placeholder shows instead).</summary>
    private ImageSource? ImageFor(TourStop stop)
    {
        if (_images.TryGetValue(stop.Id, out var cached)) return cached;
        ImageSource? image = null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = Tour.ImageUri(stop.Image);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            image = bmp;
        }
        catch (Exception ex) when (ex is UriFormatException or IOException or NotSupportedException or InvalidOperationException) { }
        _images[stop.Id] = image;
        return image;
    }
}
