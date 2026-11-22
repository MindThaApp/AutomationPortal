using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Logic;
using AutomationPortal.App.Services;
using AutomationPortal.Core;
using AutomationPortal.Editor;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Dialogs;

/// <summary>
/// The first-run welcome window: four short steps (hello, starters, try it, crash reports), each with one idea and one main button.
/// Skip and Esc finish at any time. Choices that matter (crash reports) are saved the moment they are made.
/// </summary>
public sealed class WelcomeWindow : FluentWindow
{
    private readonly AppHost _host;
    private readonly WelcomeFlow _flow = new();
    private readonly TaskCompletionSource<bool> _closed = new();
    private readonly List<UserControl> _steps = new();
    private readonly List<(System.Windows.Controls.CheckBox Box, GalleryItem Item)> _starters = new();
    private readonly ContentControl _stepHost = new();
    private readonly List<Rectangle> _dots = new();
    private readonly Wpf.Ui.Controls.Button _next = new(), _back = new(), _skip = new();
    private readonly System.Windows.Controls.CheckBox? _crash;
    private readonly ScrollViewer _scroll = new();
    private bool _finished;
    private readonly StackPanel _dotRow = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 8) };
    private UIElement? _tryInput;

    public WelcomeWindow(AppHost host)
    {
        _host = host;
        Title = Strings.Get("WelcomeDialog_WelcomeToAutomationPortal");
        var work = SystemParameters.WorkArea;
        Width = Math.Min(720, work.Width * 0.95);
        Height = Math.Min(520, work.Height * 0.92);
        MinWidth = Math.Min(420, Width);
        MinHeight = Math.Min(360, Height);
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        WindowBackdropType = HighContrast.Backdrop(WindowBackdropType.Mica);
        ExtendsContentIntoTitleBar = true;
        UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
        SetResourceReference(ForegroundProperty, "DsTextPrimaryBrush");
        FontFamily = (FontFamily)Application.Current.FindResource("FlowFont");
        AutomationProperties.SetAutomationId(this, "WelcomeWindow");

        _steps.Add(BuildHello());
        _steps.Add(BuildStarters());
        _steps.Add(BuildTryIt());
        var (privacy, crash) = BuildPrivacy();
        _steps.Add(privacy);
        _crash = crash;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var bar = new TitleBar { Title = Title, ShowMinimize = false, ShowMaximize = false, CanMaximize = false };
        root.Children.Add(bar);

        _stepHost.Margin = new Thickness(32, 8, 32, 8);
        _stepHost.IsTabStop = false;
        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scroll.Content = _stepHost;
        Grid.SetRow(_scroll, 1);
        root.Children.Add(_scroll);

        var dots = _dotRow;
        for (var i = 0; i < WelcomeFlow.StepCount; i++)
        {
            var dot = new Rectangle { Height = 8, Width = 8, RadiusX = 4, RadiusY = 4, Margin = new Thickness(4, 0, 4, 0) };
            _dots.Add(dot);
            dots.Children.Add(dot);
        }
        AutomationProperties.SetName(dots, "Progress");
        AutomationProperties.SetAutomationId(dots, "WelcomeProgress");
        Grid.SetRow(dots, 2);
        root.Children.Add(dots);

        var row = new Grid { Margin = new Thickness(24, 0, 24, 20) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new WrapPanel();
        _skip.Content = Strings.Get("Welcome2_SkipForNow");
        _skip.IsCancel = true;
        _skip.Margin = new Thickness(0, 0, 8, 0);
        _skip.Click += (_, _) => Finish(false);
        AutomationProperties.SetAutomationId(_skip, "WelcomeSkip");
        _back.Content = Strings.Get("Welcome2_Back");
        _back.MinWidth = 88;
        _back.Click += (_, _) => { if (_flow.Back()) ShowStep(false); };
        AutomationProperties.SetAutomationId(_back, "WelcomeBack");
        left.Children.Add(_skip);
        left.Children.Add(_back);
        Grid.SetColumn(left, 0);
        row.Children.Add(left);
        _next.Appearance = ControlAppearance.Primary;
        _next.IsDefault = true;
        _next.MinWidth = 110;
        _next.Click += (_, _) => { if (!_flow.Next()) Finish(true); else ShowStep(true); };
        AutomationProperties.SetAutomationId(_next, "WelcomeNext");
        Grid.SetColumn(_next, 2);
        row.Children.Add(_next);
        Grid.SetRow(row, 3);
        root.Children.Add(row);
        Content = root;

        // A harmless extra: Esc always means Skip, even while a text box has the focus.
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Finish(false); e.Handled = true; } };
        Closed += (_, _) => { SaveCrashChoice(); _closed.TrySetResult(_finished && _accepted); };
        Loaded += (_, _) => { HighContrast.ApplyBackdrop(this); ShowStep(null); };
        DialogMotion.Attach(this);
    }

    private bool _accepted;

    /// <summary>The starters ticked, in the order they are shown.</summary>
    public IReadOnlyList<GalleryItem> TickedStarters => _starters.Where(s => s.Box.IsChecked == true).Select(s => s.Item).ToList();

    /// <summary>Shows the window and completes with true when it was finished with the last button, false for Skip or Esc.</summary>
    public Task<bool> ShowAsync(Window? owner)
    {
        if (owner is { IsVisible: true })
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Show();
            WindowPlacement.BringToFront(this);
        }
        else WindowPlacement.ShowCentered(this);
        return _closed.Task;
    }

    private void Finish(bool accepted)
    {
        if (_finished) return;
        _finished = true;
        _accepted = accepted;
        Close();
    }

    private void SaveCrashChoice()
    {
        if (_crash is null) return;
        var on = _crash.IsChecked == true;
        WelcomeFlow.SaveCrashChoice(on, v => _host.Settings.Update(x => x.SendCrashReports = v), v => Startup.CrashReporting.Current?.Update(v));
    }

    // Steps ---------------------------------------------------------------------------------------------------------

    private static System.Windows.Controls.TextBlock Heading(string text, double size = TypeRamp.Subtitle)
    {
        var t = Ui.Text(text, size: size, weight: FontWeights.SemiBold);
        t.Margin = new Thickness(0, 0, 0, 6);
        AutomationProperties.SetHeadingLevel(t, AutomationHeadingLevel.Level1);
        return t;
    }

    private static UserControl Step(int index, FrameworkElement content, string name)
    {
        var step = new UserControl { Content = content };
        AutomationProperties.SetAutomationId(step, WelcomeFlow.StepAutomationId(index));
        AutomationProperties.SetName(step, name);
        return step;
    }

    private UserControl BuildHello()
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 520 };
        var hero = BuildHero();
        hero.Margin = new Thickness(0, 4, 0, 12);
        panel.Children.Add(hero);
        var title = Heading(Strings.Get("Welcome2_HelloTitle"), TypeRamp.Title);
        title.TextAlignment = TextAlignment.Center;
        panel.Children.Add(title);
        var body = Ui.Text(Strings.Get("Welcome2_HelloBody"), secondary: true, size: TypeRamp.Body);
        body.TextAlignment = TextAlignment.Center;
        panel.Children.Add(body);
        var hint = Ui.Text(Strings.Get("Welcome2_HelloHint"), secondary: true, size: TypeRamp.Readable);
        hint.TextAlignment = TextAlignment.Center;
        hint.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(hint);
        return Step(WelcomeFlow.HelloStep, panel, Strings.Get("Welcome2_HelloTitle"));
    }

    /// <summary>The lightning bolt in a ring of category colours. It breathes and the colours drift round slowly; with reduced motion or high contrast it stands still and solid.</summary>
    private static FrameworkElement BuildHero()
    {
        const double size = 180;
        var hc = HighContrast.Active;
        var grid = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetAutomationId(grid, "WelcomeHero");
        AutomationProperties.SetName(grid, Strings.Get("Welcome2_HeroName"));

        var disc = new Ellipse { Width = 112, Height = 112, StrokeThickness = 1 };
        if (hc) { disc.Fill = SystemColors.WindowBrush; disc.Stroke = SystemColors.WindowTextBrush; }
        else { disc.SetResourceReference(Shape.FillProperty, "DsCardBrush"); disc.SetResourceReference(Shape.StrokeProperty, "DsStrokeBrush"); }
        grid.Children.Add(disc);

        var orbit = new Canvas { Width = size, Height = size, RenderTransformOrigin = new Point(0.5, 0.5) };
        var spin = new RotateTransform();
        orbit.RenderTransform = spin;
        var colours = new[] { "blue", "green", "orange", "violet", "teal", "magenta" };
        for (var i = 0; i < colours.Length; i++)
        {
            var angle = i * 2 * Math.PI / colours.Length - Math.PI / 2;
            var orb = new Ellipse { Width = 20, Height = 20, StrokeThickness = hc ? 2 : 0 };
            if (hc) { orb.Fill = SystemColors.HighlightBrush; orb.Stroke = SystemColors.WindowTextBrush; }
            else orb.SetResourceReference(Shape.FillProperty, AutomationPortal.Editor.Palette.CategoryResourceKey(colours[i]));
            Canvas.SetLeft(orb, size / 2 + Math.Cos(angle) * 76 - 10);
            Canvas.SetTop(orb, size / 2 + Math.Sin(angle) * 76 - 10);
            orbit.Children.Add(orb);
        }
        grid.Children.Add(orbit);

        var bolt = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 36,0 L 10,34 L 26,34 L 20,60 L 50,22 L 32,22 Z"),
            Stretch = Stretch.Uniform, Width = 46, Height = 56, StrokeLineJoin = PenLineJoin.Round,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        if (hc) { bolt.Fill = SystemColors.HighlightBrush; }
        else bolt.SetResourceReference(Shape.FillProperty, "DsAccentBrush");
        var pulse = new ScaleTransform(1, 1);
        bolt.RenderTransform = pulse;
        grid.Children.Add(bolt);

        if (!hc)
        {
            grid.Loaded += (_, _) =>
            {
                // Arrives with the one bouncy moment, then idles. Idle loops don't run unless motion is full (Motion.Begin).
                Motion.Animate(pulse, ScaleTransform.ScaleXProperty, 0.6, 1, MotionRole.Emphasis, Motion.EaseOut);
                Motion.Animate(pulse, ScaleTransform.ScaleYProperty, 0.6, 1, MotionRole.Emphasis, Motion.EaseOut);
                Motion.Animate(grid, OpacityProperty, 0, 1, MotionRole.Enter);
                if (Motion.Effective != MotionMode.FollowWindows) return;
                var turn = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(48)) { RepeatBehavior = RepeatBehavior.Forever };
                Motion.Begin(spin, RotateTransform.AngleProperty, turn);
            };
        }
        return grid;
    }

    private UserControl BuildStarters()
    {
        var panel = new StackPanel { MaxWidth = 600 };
        panel.Children.Add(Heading(Strings.Get("WelcomeDialog_PickAFewTo")));
        panel.Children.Add(Ui.Text(Strings.Get("Welcome2_StartersBody"), secondary: true, size: TypeRamp.Body));
        var offered = new List<(string FileName, GalleryItem Item)>();
        foreach (var section in _host.GallerySections())
            foreach (var item in section.Items.Where(i => i.Shortcut.ImportQuestions.Count == 0))
                offered.Add((item.FilePath, item));
        var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var n = 0;
        foreach (var item in FirstRun.PickStarters(offered))
        {
            var box = new System.Windows.Controls.CheckBox
            {
                IsChecked = FirstRun.IsTickedByDefault(item.FilePath), HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0), Padding = new Thickness(8, 0, 0, 0),
            };
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var chip = Ui.Chip(item.Shortcut.Icon, item.Shortcut.Color, 36, 20);
            chip.Margin = new Thickness(0, 0, 12, 0);
            row.Children.Add(chip);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Ui.Text(item.Shortcut.Name, size: TypeRamp.Body, weight: FontWeights.SemiBold));
            if (!string.IsNullOrWhiteSpace(item.Shortcut.Description)) text.Children.Add(Ui.Text(item.Shortcut.Description!, secondary: true, size: TypeRamp.Readable));
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            box.Content = row;
            AutomationProperties.SetName(box, item.Shortcut.Name);
            AutomationProperties.SetAutomationId(box, "WelcomeStarter" + ++n);
            var card = new Border { Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 8), BorderThickness = new Thickness(1), Child = box };
            card.SetResourceReference(Border.BackgroundProperty, "DsCardBrush");
            card.SetResourceReference(Border.CornerRadiusProperty, "DsRadiusCard");
            void Paint() => card.SetResourceReference(Border.BorderBrushProperty, box.IsChecked == true ? "DsAccentBrush" : "DsStrokeBrush");
            box.Checked += (_, _) => Paint();
            box.Unchecked += (_, _) => Paint();
            Paint();
            list.Children.Add(card);
            _starters.Add((box, item));
        }
        if (_starters.Count == 0) list.Children.Add(Ui.Text(Strings.Get("Welcome2_NoStarters"), secondary: true, size: TypeRamp.Readable));
        panel.Children.Add(list);
        return Step(WelcomeFlow.StartersStep, panel, Strings.Get("WelcomeDialog_PickAFewTo"));
    }

    private UserControl BuildTryIt()
    {
        var panel = new StackPanel { MaxWidth = 600 };
        panel.Children.Add(Heading(Strings.Get("Welcome2_TryTitle")));
        panel.Children.Add(Ui.Text(Strings.Get("Welcome2_TryBody"), secondary: true, size: TypeRamp.Body));

        var card = new Border { Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(16), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "DsCardBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "DsStrokeBrush");
        card.SetResourceReference(Border.CornerRadiusProperty, "DsRadiusCard");
        var inner = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var chip = Ui.Chip("TextCaseTitle24", "violet", 40, 22);
        chip.Margin = new Thickness(0, 0, 12, 0);
        head.Children.Add(chip);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Ui.Text(Strings.Get("Welcome2_TrySampleName"), size: TypeRamp.PanelTitle, weight: FontWeights.SemiBold));
        names.Children.Add(Ui.Text(Strings.Get("Welcome2_TrySampleHelp"), secondary: true, size: TypeRamp.Readable));
        Grid.SetColumn(names, 1);
        head.Children.Add(names);
        inner.Children.Add(head);

        var input = new Wpf.Ui.Controls.TextBox { Text = TrySample.DefaultText, MaxLength = TrySample.MaxLength, Margin = new Thickness(0, 14, 0, 0) };
        AutomationProperties.SetName(input, Strings.Get("Welcome2_TryInputName"));
        AutomationProperties.SetAutomationId(input, "WelcomeTryText");
        _tryInput = input;
        inner.Children.Add(input);

        var run = new Wpf.Ui.Controls.Button { Content = Strings.Get("Welcome2_TryRun"), Icon = new SymbolIcon(SymbolRegular.Play24), Margin = new Thickness(0, 12, 0, 0) };
        AutomationProperties.SetAutomationId(run, "WelcomeTryIt");
        inner.Children.Add(run);

        var result = new Border { Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(12, 10, 12, 10), BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed };
        result.SetResourceReference(Border.BorderBrushProperty, "DsAccentBrush");
        result.SetResourceReference(Border.CornerRadiusProperty, "DsRadiusCard");
        var resultRow = new Grid();
        resultRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        resultRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tick = new SymbolIcon(SymbolRegular.CheckmarkCircle24) { FontSize = 24, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
        tick.SetResourceReference(IconElement.ForegroundProperty, "DsAccentBrush");
        var resultText = Ui.Text("", size: TypeRamp.PanelTitle, weight: FontWeights.SemiBold);
        resultText.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(resultText, Strings.Get("Welcome2_TryResultName"));
        AutomationProperties.SetAutomationId(resultText, "WelcomeTryResult");
        AutomationProperties.SetLiveSetting(resultText, AutomationLiveSetting.Polite);
        Grid.SetColumn(resultText, 1);
        resultRow.Children.Add(tick);
        resultRow.Children.Add(resultText);
        result.Child = resultRow;
        inner.Children.Add(result);
        card.Child = inner;
        panel.Children.Add(card);

        async Task RunAsync()
        {
            run.IsEnabled = false;
            result.Visibility = Visibility.Visible;
            resultText.Text = Strings.Get("Welcome2_TryRunning");
            tick.Visibility = Visibility.Collapsed;
            try
            {
                // The runner directly, with no history entry: it only changes the case of the typed text, in memory.
                var r = await _host.Runner.RunAsync(TrySample.Create(input.Text), ContentValue.Empty, new RunOptions { Source = RunSource.Library, RecordHistory = false });
                if (r.Status == RunStatus.Succeeded)
                {
                    resultText.Text = r.Output.AsText();
                    tick.Visibility = Visibility.Visible;
                    if (tick.RenderTransform is ScaleTransform pop)
                    {
                        Motion.Animate(pop, ScaleTransform.ScaleXProperty, 0.5, 1, MotionRole.Emphasis, Motion.EaseOut);
                        Motion.Animate(pop, ScaleTransform.ScaleYProperty, 0.5, 1, MotionRole.Emphasis, Motion.EaseOut);
                    }
                }
                else resultText.Text = Strings.Get("Welcome2_TryFailed") + (r.ErrorMessage is { Length: > 0 } m ? " " + m : "");
            }
            catch (Exception ex) { resultText.Text = Strings.Get("Welcome2_TryFailed") + " " + ex.Message; }
            run.IsEnabled = true;
            run.Content = Strings.Get("Welcome2_TryAgain");
            AccessKeys();
        }
        run.Click += async (_, _) => await RunAsync();
        input.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await RunAsync(); } };
        return Step(WelcomeFlow.TryItStep, panel, Strings.Get("Welcome2_TryTitle"));
    }

    private (UserControl Step, System.Windows.Controls.CheckBox? Crash) BuildPrivacy()
    {
        var panel = new StackPanel { MaxWidth = 600 };
        panel.Children.Add(Heading(Strings.Get("Welcome2_PrivacyTitle")));
        panel.Children.Add(Ui.Text(Strings.Get("Welcome2_PrivacyBody"), secondary: true, size: TypeRamp.Body));
        System.Windows.Controls.CheckBox? crash = null;
        if (Startup.CrashReporting.IsAvailable)
        {
            var card = new Border { Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(16), BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, "DsCardBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "DsStrokeBrush");
            card.SetResourceReference(Border.CornerRadiusProperty, "DsRadiusCard");
            var inner = new StackPanel();
            crash = new System.Windows.Controls.CheckBox
            {
                Content = Ui.Text(Strings.Get("WelcomeDialog_CrashReports"), size: TypeRamp.Body),
                IsChecked = WelcomeFlow.CrashReportsDefault(_host.Settings.Current),
            };
            AutomationProperties.SetName(crash, Strings.Get("WelcomeDialog_CrashReports"));
            AutomationProperties.SetAutomationId(crash, "WelcomeCrashReports");
            crash.Checked += (_, _) => SaveCrashChoice();
            crash.Unchecked += (_, _) => SaveCrashChoice();
            inner.Children.Add(crash);
            var whatsSent = new Wpf.Ui.Controls.HyperlinkButton { Content = Strings.Get("WelcomeDialog_WhatsSent"), NavigateUri = WelcomeFlow.WhatsSentUrl, Padding = new Thickness(0), Margin = new Thickness(28, 6, 0, 0) };
            AutomationProperties.SetName(whatsSent, Strings.Get("WelcomeDialog_WhatsSent"));
            AutomationProperties.SetAutomationId(whatsSent, "WelcomeWhatsSent");
            inner.Children.Add(whatsSent);
            card.Child = inner;
            panel.Children.Add(card);
            var after = Ui.Text(Strings.Get("Welcome2_PrivacyLater"), secondary: true, size: TypeRamp.Readable);
            after.Margin = new Thickness(2, 12, 0, 0);
            panel.Children.Add(after);
        }
        var tray = Ui.Text(Strings.Get("Welcome2_TrayNote"), secondary: true, size: TypeRamp.Readable);
        tray.Margin = new Thickness(2, 12, 0, 0);
        panel.Children.Add(tray);
        return (Step(WelcomeFlow.PrivacyStep, panel, Strings.Get("Welcome2_PrivacyTitle")), crash);
    }

    // Frame ---------------------------------------------------------------------------------------------------------

    private void AccessKeys()
    {
        foreach (var b in new[] { _next, _back, _skip }) if (b.Content is string s) b.Content = s.Replace("_", "");
        AutomationPortal.Editor.UI.AccessKeys.Apply(new System.Windows.Controls.Primitives.ButtonBase[] { _next, _skip, _back });
    }

    private void ShowStep(bool? forward)
    {
        var i = _flow.Current;
        _stepHost.Content = _steps[i];
        _scroll.ScrollToTop();
        if (forward is bool dir) PageSlide.Play(_steps[i], dir);
        _back.Visibility = _flow.CanGoBack ? Visibility.Visible : Visibility.Collapsed;
        _next.Content = Strings.Get(i == 0 ? "Welcome2_LetsGo" : _flow.IsLast ? "Welcome2_GetStarted" : "Welcome2_Next");
        AccessKeys();
        for (var d = 0; d < _dots.Count; d++)
        {
            var active = d == i;
            _dots[d].SetResourceReference(Shape.FillProperty, active ? "DsAccentBrush" : "ControlStrongStrokeColorDefaultBrush");
            var width = active ? 24.0 : 8.0;
            if (forward is null) _dots[d].Width = width;
            else Motion.Animate(_dots[d], WidthProperty, _dots[d].ActualWidth > 0 ? _dots[d].ActualWidth : _dots[d].Width, width, MotionRole.Enter, Motion.EaseOut);
        }
        AutomationProperties.SetName(_dotRow, string.Format(Strings.Get("Welcome2_ProgressFormat"), i + 1, WelcomeFlow.StepCount));
        // Focus lands on the first thing to do in the step, so keyboard and Narrator users start in the right place.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            UIElement target = i switch
            {
                WelcomeFlow.StartersStep => (UIElement?)_starters.FirstOrDefault().Box ?? _next,
                WelcomeFlow.TryItStep => _tryInput ?? _next,
                _ => _next,
            };
            target.Focus();
        });
    }
}
