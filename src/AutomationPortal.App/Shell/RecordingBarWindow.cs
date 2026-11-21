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
/// The small always-on-top bar shown while recording: red dot, the app's name, elapsed time, the step count, Pause, Stop with its
/// shortcut, and the last three steps in plain words ("Click 'Save' in Notepad"), so the user sees what was understood.
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
    private readonly TextBlock _count = new();
    private readonly StackPanel _recent = new() { Margin = new Thickness(24, 8, 0, 0) };
    private readonly string _appName;
    private int _countdown;

    public RecordingBarWindow(RecordingSession session, string? appName = null)
    {
        _session = session;
        _appName = string.IsNullOrWhiteSpace(appName) ? "" : appName;
        Title = Strings.Get("RecordingBarWindow_AutomationPortalRecording");
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
        card.SetResourceReference(Border.BorderBrushProperty, "DsStrokeBrush");
        card.Effect = AutomationPortal.Editor.HighContrast.Shadow(18, 3, 0.35);

        var outer = new StackPanel();
        var row = new StackPanel { Orientation = Orientation.Horizontal };

        _dot.Width = 14; _dot.Height = 14; _dot.CornerRadius = new CornerRadius(7); _dot.Background = AutomationPortal.Editor.HighContrast.Active ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromRgb(0xE0, 0x2E, 0x2E));
        _dot.VerticalAlignment = VerticalAlignment.Center; _dot.Margin = new Thickness(0, 0, 10, 0);
        AutomationProperties.SetName(_dot, "Recording");
        StartPulse();
        row.Children.Add(_dot);

        _label.Text = LabelText(false); _label.FontWeight = FontWeights.SemiBold; _label.FontSize = 14; _label.VerticalAlignment = VerticalAlignment.Center;
        _label.SetResourceReference(TextBlock.ForegroundProperty, "DsTextPrimaryBrush");
        row.Children.Add(_label);

        _time.Text = "0:00"; _time.FontSize = 14; _time.Margin = new Thickness(10, 0, 16, 0); _time.VerticalAlignment = VerticalAlignment.Center; _time.MinWidth = 40;
        _time.SetResourceReference(TextBlock.ForegroundProperty, "DsTextSecondaryBrush");
        AutomationProperties.SetName(_time, "Elapsed time");
        row.Children.Add(_time);
        _count.FontSize = 13; _count.Margin = new Thickness(0, 0, 16, 0); _count.VerticalAlignment = VerticalAlignment.Center;
        _count.SetResourceReference(TextBlock.ForegroundProperty, "DsTextSecondaryBrush");
        AutomationProperties.SetName(_count, "Steps recorded");
        row.Children.Add(_count);

        _pause.Content = "Pause"; _pause.Icon = new SymbolIcon(SymbolRegular.Pause24); _pause.Margin = new Thickness(0, 0, 8, 0);
        _pause.Focusable = false;
        _pause.Click += (_, _) => { _session.TogglePause(); Refresh(); };
        row.Children.Add(_pause);

        var stop = new Wpf.Ui.Controls.Button { Content = $"Stop ({session.StopHotkey})", Icon = new SymbolIcon(SymbolRegular.Stop24), Appearance = ControlAppearance.Danger, Margin = new Thickness(0, 0, 14, 0), Focusable = false };
        stop.Click += (_, _) => _session.Stop();
        row.Children.Add(stop);

        outer.Children.Add(row);
        outer.Children.Add(_recent);
        var safety = new TextBlock { Text = Strings.Get("RecordingBarWindow_PasswordsAreNeverRecorded"), FontSize = AutomationPortal.Editor.TypeRamp.Readable, Margin = new Thickness(24, 6, 0, 0) };
        safety.SetResourceReference(TextBlock.ForegroundProperty, "DsTextTertiaryBrush");
        outer.Children.Add(safety);
        card.Child = outer;
        Content = card;

        _tick.Tick += (_, _) => Refresh();
        _tick.Start();
        Closed += (_, _) => _tick.Stop();
        _ = session.Finished.ContinueWith(_ => Dispatcher.BeginInvoke(new Action(Close)), TaskScheduler.Default);
        SourceInitialized += (_, _) => Place();
    }

    private bool? _wasPaused;

    private void StartPulse()
    {
        if (AutomationPortal.Editor.Motion.Reduced) return;
        AutomationPortal.Editor.Motion.Begin(_dot, OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
    }

    private void Refresh()
    {
        if (_countdown > 0) { _label.Text = $"Starting in {_countdown}…"; return; }
        _time.Text = RecordingSession.FormatElapsed(_session.Elapsed);
        _count.Text = _session.StepCount == 1 ? "1 step" : $"{_session.StepCount} steps";
        var recent = _session.RecentSteps();
        if (_recent.Children.Count != recent.Count || recent.Where((s, i) => ((TextBlock)_recent.Children[i]).Text != s).Any())
        {
            _recent.Children.Clear();
            foreach (var s in recent)
            {
                var line = new TextBlock { Text = s, FontSize = 13, Margin = new Thickness(0, 1, 0, 1), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 520 };
                line.SetResourceReference(TextBlock.ForegroundProperty, "DsTextSecondaryBrush");
                _recent.Children.Add(line);
            }
        }
        var paused = _session.IsPaused;
        _label.Text = LabelText(paused);
        _pause.Content = paused ? "Resume" : "Pause";
        _pause.Icon = new SymbolIcon(paused ? SymbolRegular.Record24 : SymbolRegular.Pause24);
        if (_wasPaused == paused) return;
        _wasPaused = paused;
        if (paused) { _dot.BeginAnimation(OpacityProperty, null); _dot.Opacity = 0.4; }
        else StartPulse();
    }

    private string LabelText(bool paused) =>
        (paused ? "Paused" : "Recording") + (_appName.Length > 0 ? " " + _appName : "");

    /// <summary>Shows "Starting in 3…", "2…", "1…" before recording starts, so the user can get to the app.</summary>
    public async Task CountdownAsync(int seconds)
    {
        for (_countdown = seconds; _countdown > 0; _countdown--)
        {
            Refresh();
            await Task.Delay(1000);
        }
        _label.Text = LabelText(false);
        Refresh();
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
