using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AutomationPortal.App.Logic;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Shell;

/// <summary>
/// The small always-on-top bar shown while recording: red dot, elapsed time, Pause, Stop, "Ignore waits" and the stop shortcut.
/// It never takes keyboard focus, so typing goes to the app being recorded; clicks on it are not recorded (the recorder skips its own process).
/// </summary>
public sealed class RecordingBarWindow : Window
{
    private readonly RecordingSession _session;
    private readonly TextBlock _time = new();
    private readonly TextBlock _label = new();
    private readonly Wpf.Ui.Controls.Button _pause = new();
    private readonly Border _dot = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public RecordingBarWindow(RecordingSession session)
    {
        _session = session;
        Title = "Automation Portal recording";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = (FontFamily)Application.Current.FindResource("FlowFont");
        AutomationProperties.SetName(this, "Automation Portal recording bar");

        var card = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 12, 10), Margin = new Thickness(12) };
        card.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.35, Color = Colors.Black };

        var row = new StackPanel { Orientation = Orientation.Horizontal };

        _dot.Width = 14; _dot.Height = 14; _dot.CornerRadius = new CornerRadius(7); _dot.Background = new SolidColorBrush(Color.FromRgb(0xE0, 0x2E, 0x2E));
        _dot.VerticalAlignment = VerticalAlignment.Center; _dot.Margin = new Thickness(0, 0, 10, 0);
        AutomationProperties.SetName(_dot, "Recording");
        StartPulse();
        row.Children.Add(_dot);

        _label.Text = "Recording"; _label.FontWeight = FontWeights.SemiBold; _label.FontSize = 14; _label.VerticalAlignment = VerticalAlignment.Center;
        _label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        row.Children.Add(_label);

        _time.Text = "0:00"; _time.FontSize = 14; _time.Margin = new Thickness(10, 0, 16, 0); _time.VerticalAlignment = VerticalAlignment.Center; _time.MinWidth = 40;
        _time.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        AutomationProperties.SetName(_time, "Elapsed time");
        row.Children.Add(_time);

        _pause.Content = "Pause"; _pause.Icon = new SymbolIcon(SymbolRegular.Pause24); _pause.Margin = new Thickness(0, 0, 8, 0);
        _pause.Focusable = false;
        _pause.Click += (_, _) => { _session.TogglePause(); Refresh(); };
        row.Children.Add(_pause);

        var stop = new Wpf.Ui.Controls.Button { Content = "Stop", Icon = new SymbolIcon(SymbolRegular.Stop24), Appearance = ControlAppearance.Danger, Margin = new Thickness(0, 0, 14, 0), Focusable = false };
        stop.Click += (_, _) => _session.Stop();
        row.Children.Add(stop);

        var waits = new CheckBox { Content = "Ignore waits", IsChecked = session.DropWaits, VerticalAlignment = VerticalAlignment.Center, Focusable = false,
            ToolTip = "Leave the pauses between your clicks and typing out of the recording" };
        waits.Click += (_, _) => _session.DropWaits = waits.IsChecked == true;
        row.Children.Add(waits);

        var hint = new TextBlock { Text = $"or press {session.StopHotkey}", FontSize = 13, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        row.Children.Add(hint);

        card.Child = row;
        Content = card;

        _tick.Tick += (_, _) => Refresh();
        _tick.Start();
        Closed += (_, _) => _tick.Stop();
        _ = session.Finished.ContinueWith(_ => Dispatcher.BeginInvoke(new Action(Close)), TaskScheduler.Default);
        SourceInitialized += (_, _) => Place();
    }

    private bool? _wasPaused;

    private void StartPulse() =>
        _dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });

    private void Refresh()
    {
        _time.Text = RecordingSession.FormatElapsed(_session.Elapsed);
        var paused = _session.IsPaused;
        _label.Text = paused ? "Paused" : "Recording";
        _pause.Content = paused ? "Resume" : "Pause";
        _pause.Icon = new SymbolIcon(paused ? SymbolRegular.Record24 : SymbolRegular.Pause24);
        if (_wasPaused == paused) return;
        _wasPaused = paused;
        if (paused) { _dot.BeginAnimation(OpacityProperty, null); _dot.Opacity = 0.4; }
        else StartPulse();
    }
    private void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Top + 8;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        Place();
    }
}
