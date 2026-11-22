using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Logic;
using AutomationPortal.App.Services;
using AutomationPortal.Core;
using AutomationPortal.Editor;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Dialogs;

/// <summary>
/// The first-run welcome window: three short steps (hello, starters, try it with the crash-report choice), each with one idea and one main button.
/// Skip, "Start with recommended settings" and Esc finish at any time. Choices that matter (crash reports) are saved the moment they are made.
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
    private readonly StackPanel _dotRow = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 20) };
    private readonly Wpf.Ui.Controls.Button _next = new(), _back = new(), _skip = new(), _recommended = new();
    private readonly ScrollViewer _scroll = new();
    private System.Windows.Controls.CheckBox? _crash;
    private UIElement? _tryInput;
    private bool _finished, _accepted;

    public WelcomeWindow(AppHost host)
    {
        _host = host;
        Title = Strings.Get("WelcomeDialog_WelcomeToAutomationPortal");
        var work = SystemParameters.WorkArea;
        Width = Math.Min(720, work.Width * 0.95);
        Height = Math.Min(620, work.Height * 0.92);
        MinWidth = Math.Min(420, Width);
        MinHeight = Math.Min(380, Height);
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        // One flat surface on every step: no backdrop material, so nothing shifts between steps.
        WindowBackdropType = WindowBackdropType.None;
        ExtendsContentIntoTitleBar = true;
        UseLayoutRounding = true;
        FontSize = Fs(TypeRamp.Body);
        SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
        SetResourceReference(ForegroundProperty, "DsTextPrimaryBrush");
        FontFamily = (FontFamily)Application.Current.FindResource("FlowFont");
        AutomationProperties.SetAutomationId(this, "WelcomeWindow");

        _steps.Add(BuildHello());
        _steps.Add(BuildStarters());
        _steps.Add(BuildTryIt());

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var bar = new TitleBar { Title = Title, ShowMinimize = false, ShowMaximize = false, CanMaximize = false };
        bar.Icon = new ImageIcon { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/automationportal.png")) };   // same bare icon as the main window, so the title bar sizes and places it
        root.Children.Add(bar);

        _stepHost.Margin = new Thickness(32, 8, 32, 16);
        _stepHost.IsTabStop = false;
        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scroll.Content = _stepHost;
        Grid.SetRow(_scroll, 1);
        root.Children.Add(_scroll);

        for (var i = 0; i < WelcomeFlow.StepCount; i++)
        {
            var dot = new Rectangle { Height = 8, Width = 8, RadiusX = 4, RadiusY = 4, Margin = new Thickness(4, 0, 4, 0) };
            _dots.Add(dot);
            _dotRow.Children.Add(dot);
        }
        AutomationProperties.SetName(_dotRow, "Progress");
        AutomationProperties.SetAutomationId(_dotRow, "WelcomeProgress");
        Grid.SetRow(_dotRow, 2);
        root.Children.Add(_dotRow);

        // Left: ways out. Right: Back, then the main button, 8 px apart.
        var row = new Grid { Margin = new Thickness(24, 0, 24, 20) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        _skip.Content = Strings.Get("Welcome2_SkipForNow");
        _skip.IsCancel = true;
        _skip.Margin = new Thickness(0, 0, 8, 0);
        _skip.Click += (_, _) => Finish(false);
        AutomationProperties.SetAutomationId(_skip, "WelcomeSkip");
        _recommended.Content = Strings.Get("Welcome2_StartRecommended");
        _recommended.Margin = new Thickness(0, 0, 8, 0);
        _recommended.Click += (_, _) => StartRecommended();
        AutomationProperties.SetAutomationId(_recommended, "WelcomeRecommended");
        left.Children.Add(_skip);
        left.Children.Add(_recommended);
        row.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        _back.Content = Strings.Get("Welcome2_Back");
        _back.MinWidth = 88;
        _back.Margin = new Thickness(0, 0, 8, 0);
        _back.Click += (_, _) => { if (_flow.Back()) ShowStep(false); };
        AutomationProperties.SetAutomationId(_back, "WelcomeBack");
        _next.Appearance = ControlAppearance.Primary;
        _next.IsDefault = true;
        _next.MinWidth = 110;
        _next.Click += (_, _) => { if (!_flow.Next()) Finish(true); else ShowStep(true); };
        AutomationProperties.SetAutomationId(_next, "WelcomeNext");
        right.Children.Add(_back);
        right.Children.Add(_next);
        Grid.SetColumn(right, 1);
        row.Children.Add(right);
        foreach (var b in new[] { _skip, _recommended, _back, _next }) b.FontSize = Fs(TypeRamp.Body);
        Grid.SetRow(row, 3);
        root.Children.Add(row);
        Content = root;

        // Esc always means Skip, even while a text box has the focus.
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Finish(false); e.Handled = true; } };
        Closed += (_, _) => { SaveCrashChoice(); _closed.TrySetResult(_finished && _accepted); };
        Loaded += (_, _) => ShowStep(null);
        DialogMotion.Attach(this);
    }

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

    /// <summary>Ticks the recommended starters, turns crash reports on and finishes.</summary>
    private void StartRecommended()
    {
        var picks = FirstRun.RecommendedIndexes(_starters.Select(s => s.Item.FilePath).ToList());
        for (var i = 0; i < _starters.Count; i++) _starters[i].Box.IsChecked = picks.Contains(i);
        if (_crash is not null) _crash.IsChecked = true;
        Finish(true);
    }

    private void SaveCrashChoice()
    {
        if (_crash is null) return;
        var on = _crash.IsChecked == true;
        WelcomeFlow.SaveCrashChoice(on, v => _host.Settings.Update(x => x.SendCrashReports = v), v => Startup.CrashReporting.Current?.Update(v));
    }

    // Text sizes follow the in-app text size setting, on top of Windows text scaling ---------------------------------

    private double Fs(double size) => AppearanceMath.FontSize(size, _host.Settings.Current.TextScale);

    private System.Windows.Controls.TextBlock T(string text, bool secondary = false, double size = TypeRamp.Body, FontWeight? weight = null)
    {
        var t = Ui.Text(text, secondary, Fs(size), weight);
        // Secondary text gets its colour from the token values directly (#FFF at 79% in dark, #000 at 61% in light), so no style or lookup order can turn it white.
        if (secondary && !HighContrast.Active) t.Foreground = SecondaryBrush;
        return t;
    }

    private static Brush SecondaryBrush => Frozen(IsDarkTheme ? Color.FromArgb(0xC9, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x9C, 0, 0, 0));

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private static bool IsDarkTheme => Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme() == Wpf.Ui.Appearance.ApplicationTheme.Dark;

    private System.Windows.Controls.TextBlock Heading(string text, double size = TypeRamp.Subtitle)
    {
        var t = T(text, size: size, weight: FontWeights.SemiBold);
        t.Margin = new Thickness(0, 0, 0, 6);
        AutomationProperties.SetHeadingLevel(t, AutomationHeadingLevel.Level1);
        return t;
    }

    private static bool Dark => IsDarkTheme;

    private static double TintOpacity => Dark ? 0.22 : 0.14;

    /// <summary>An accent wash over a card or square: 14% in light, 22% in dark, none in high contrast (the border carries it).</summary>
    private static Border Tint()
    {
        var tint = new Border { IsHitTestVisible = false, Opacity = TintOpacity, Visibility = HighContrast.Active ? Visibility.Collapsed : Visibility.Visible };
        tint.SetResourceReference(Border.BackgroundProperty, "DsAccentBrush");
        tint.SetResourceReference(Border.CornerRadiusProperty, "DsRadiusCard");
        return tint;
    }

    private static UserControl Step(int index, FrameworkElement content, string name)
    {
        var step = new UserControl { Content = content };
        AutomationProperties.SetAutomationId(step, WelcomeFlow.StepAutomationId(index));
        AutomationProperties.SetName(step, name);
        return step;
    }

    // Steps ---------------------------------------------------------------------------------------------------------

    private UserControl BuildHello()
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 520, Margin = new Thickness(0, 24, 0, 0) };
        var hero = BuildHero();
        hero.Margin = new Thickness(0, 4, 0, 16);
        panel.Children.Add(hero);
        var title = Heading(Strings.Get("Welcome2_HelloTitle"), TypeRamp.Title);
        title.TextAlignment = TextAlignment.Center;
        panel.Children.Add(title);
        var body = T(Strings.Get("Welcome2_HelloBody"), secondary: true);
        body.TextAlignment = TextAlignment.Center;
        body.MaxWidth = 480;
        body.LineHeight = Fs(20);
        panel.Children.Add(body);
        var hint = T(Strings.Get("Welcome2_HelloHint"), secondary: true, size: TypeRamp.Caption);
        hint.TextAlignment = TextAlignment.Center;
        hint.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(hint);
        return Step(WelcomeFlow.HelloStep, panel, Strings.Get("Welcome2_HelloTitle"));
    }

    /// <summary>The lightning bolt on a disc, ringed by six category squares. They pop in and bob gently; with reduced motion or high contrast everything stands still.</summary>
    private static FrameworkElement BuildHero()
    {
        const double size = 200;
        var hc = HighContrast.Active;
        var grid = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetAutomationId(grid, "WelcomeHero");
        AutomationProperties.SetName(grid, Strings.Get("Welcome2_HeroName"));

        var disc = new Ellipse { Width = 112, Height = 112, StrokeThickness = 1 };
        if (hc) { disc.Fill = SystemColors.WindowBrush; disc.Stroke = SystemColors.WindowTextBrush; }
        else { disc.SetResourceReference(Shape.FillProperty, "DsCardBrush"); disc.SetResourceReference(Shape.StrokeProperty, "DsStrokeBrush"); }
        grid.Children.Add(disc);

        var ring = new Canvas { Width = size, Height = size };
        var squares = new (string Icon, string Colour)[]
        {
            ("Document24", WelcomeFlow.HeroColours[0]), ("ArrowSync24", WelcomeFlow.HeroColours[1]), ("Link24", WelcomeFlow.HeroColours[2]),
            ("TextCaseTitle24", WelcomeFlow.HeroColours[3]), ("Folder24", WelcomeFlow.HeroColours[4]), ("WeatherSunny24", WelcomeFlow.HeroColours[5]),
        };
        var index = 0;
        foreach (var (icon, colour) in squares)
        {
            var angle = index * 2 * Math.PI / squares.Length - Math.PI / 2;
            var chip = Ui.Chip(icon, colour, 34, 18);
            chip.SetResourceReference(Border.CornerRadiusProperty, "DsRadiusControl");
            chip.CornerRadius = new CornerRadius(8);
            chip.RenderTransformOrigin = new Point(0.5, 0.5);
            var scale = new ScaleTransform(1, 1);
            var bob = new TranslateTransform();
            chip.RenderTransform = new TransformGroup { Children = { scale, bob } };
            Canvas.SetLeft(chip, size / 2 + Math.Cos(angle) * 80 - 17);
            Canvas.SetTop(chip, size / 2 + Math.Sin(angle) * 80 - 17);
            ring.Children.Add(chip);
            var seconds = 2.2 + 0.25 * index++;
            if (!hc)
                grid.Loaded += (_, _) =>
                {
                    Motion.Animate(scale, ScaleTransform.ScaleXProperty, 0.4, 1, MotionRole.Emphasis, Motion.EaseOut);
                    Motion.Animate(scale, ScaleTransform.ScaleYProperty, 0.4, 1, MotionRole.Emphasis, Motion.EaseOut);
                    var drift = new DoubleAnimation(-3, 3, TimeSpan.FromSeconds(seconds)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
                    Motion.Begin(bob, TranslateTransform.YProperty, drift);   // an idle loop: only runs when motion is full
                };
        }
        grid.Children.Add(ring);

        var bolt = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 36,0 L 10,34 L 26,34 L 20,60 L 50,22 L 32,22 Z"),
            Stretch = Stretch.Uniform, Width = 46, Height = 56, StrokeLineJoin = PenLineJoin.Round,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        if (hc) bolt.Fill = SystemColors.HighlightBrush;
        else bolt.SetResourceReference(Shape.FillProperty, "DsAccentBrush");
        var pop = new ScaleTransform(1, 1);
        bolt.RenderTransform = pop;
        grid.Children.Add(bolt);
        if (!hc)
            grid.Loaded += (_, _) =>
            {
                Motion.Animate(pop, ScaleTransform.ScaleXProperty, 0.6, 1, MotionRole.Emphasis, Motion.EaseOut);
                Motion.Animate(pop, ScaleTransform.ScaleYProperty, 0.6, 1, MotionRole.Emphasis, Motion.EaseOut);
            };
        return grid;
    }

    private static string Blurb(GalleryItem item)
    {
        var key = "Welcome2_Blurb_" + System.IO.Path.GetFileNameWithoutExtension(item.FilePath).Replace('-', '_');
        var text = Strings.Get(key);
        if (text != key) return text;
        var d = item.Shortcut.Description?.Trim() ?? "";
        var end = d.IndexOf(". ", StringComparison.Ordinal);
        return end > 0 ? d[..(end + 1)] : d;
    }

    private UserControl BuildStarters()
    {
        var panel = new StackPanel { MaxWidth = 600 };
        panel.Children.Add(Heading(Strings.Get("WelcomeDialog_PickAFewTo")));
        panel.Children.Add(T(Strings.Get("Welcome2_StartersBody"), secondary: true));
        var offered = new List<(string FileName, GalleryItem Item)>();
        foreach (var section in _host.GallerySections())
            foreach (var item in section.Items.Where(i => i.Shortcut.ImportQuestions.Count == 0))
                offered.Add((item.FilePath, item));
        var picked = FirstRun.PickStarters(offered);
        var recommended = FirstRun.RecommendedIndexes(picked.Select(i => i.FilePath).ToList());
        var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var n = 0;
        foreach (var item in picked)
        {
            var box = new System.Windows.Controls.CheckBox
            {
                IsChecked = recommended.Contains(n), HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(12, 10, 12, 10), Padding = new Thickness(8, 0, 0, 0), FontSize = Fs(TypeRamp.Body),
            };
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var chip = Ui.Chip(item.Shortcut.Icon, item.Shortcut.Color, 36, 20);
            chip.Margin = new Thickness(0, 0, 12, 0);
            row.Children.Add(chip);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(T(item.Shortcut.Name, size: TypeRamp.Body, weight: FontWeights.SemiBold));
            var blurb = Blurb(item);
            if (blurb.Length > 0)
            {
                var d = T(blurb, secondary: true, size: TypeRamp.Readable);
                d.MaxHeight = Fs(TypeRamp.Readable) * 1.4 * 2;
                d.TextTrimming = TextTrimming.CharacterEllipsis;
                text.Children.Add(d);
            }
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            box.Content = row;
            AutomationProperties.SetName(box, item.Shortcut.Name);
            AutomationProperties.SetAutomationId(box, "WelcomeStarter" + ++n);
            var tint = Tint();
            var face = new Grid();
            face.Children.Add(tint);
            face.Children.Add(box);
            var card = new Border { Margin = new Thickness(0, 0, 0, 8), BorderThickness = new Thickness(1), Child = face };
            card.SetResourceReference(Border.BackgroundProperty, "DsCardBrush");
            card.SetResourceReference(Border.CornerRadiusProperty, "DsRadiusCard");
            void Paint()
            {
                var on = box.IsChecked == true;
                card.SetResourceReference(Border.BorderBrushProperty, on ? "DsAccentBrush" : "DsStrokeBrush");
                card.BorderThickness = new Thickness(on && HighContrast.Active ? 2 : 1);
                tint.Opacity = on ? TintOpacity : 0;
            }
            box.Checked += (_, _) => Paint();
            box.Unchecked += (_, _) => Paint();
            Paint();
            list.Children.Add(card);
            _starters.Add((box, item));
        }
        if (_starters.Count == 0) list.Children.Add(T(Strings.Get("Welcome2_NoStarters"), secondary: true, size: TypeRamp.Readable));
        panel.Children.Add(list);
        return Step(WelcomeFlow.StartersStep, panel, Strings.Get("WelcomeDialog_PickAFewTo"));
    }

    private UserControl BuildTryIt()
    {
        var panel = new StackPanel { MaxWidth = 600 };
        panel.Children.Add(Heading(Strings.Get("Welcome2_TryTitle")));
        panel.Children.Add(T(Strings.Get("Welcome2_TryBody"), secondary: true));

        var card = new Border { Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(16), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "DsCardBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "DsStrokeBrush");
        card.SetResourceReference(Border.CornerRadiusProperty, "DsRadiusCard");
        var inner = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var chip = Ui.Chip("TextCaseTitle24", "purple", 40, 22);
        chip.Margin = new Thickness(0, 0, 12, 0);
        head.Children.Add(chip);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(T(Strings.Get("Welcome2_TrySampleName"), size: TypeRamp.PanelTitle, weight: FontWeights.SemiBold));
        names.Children.Add(T(Strings.Get("Welcome2_TrySampleHelp"), secondary: true, size: TypeRamp.Readable));
        Grid.SetColumn(names, 1);
        head.Children.Add(names);
        inner.Children.Add(head);

        var input = new Wpf.Ui.Controls.TextBox { Text = TrySample.DefaultText, MaxLength = TrySample.MaxLength, Margin = new Thickness(0, 14, 0, 0), FontSize = Fs(TypeRamp.Body) };
        AutomationProperties.SetName(input, Strings.Get("Welcome2_TryInputName"));
        AutomationProperties.SetAutomationId(input, "WelcomeTryText");
        _tryInput = input;
        inner.Children.Add(input);

        var run = new Wpf.Ui.Controls.Button { Content = Strings.Get("Welcome2_TryRun"), Icon = new SymbolIcon(SymbolRegular.Play24), Margin = new Thickness(0, 12, 0, 0), FontSize = Fs(TypeRamp.Body) };
        AutomationProperties.SetAutomationId(run, "WelcomeTryIt");
        inner.Children.Add(run);

        var result = new Border { Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(12, 10, 12, 10), BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed };
        result.SetResourceReference(Border.BorderBrushProperty, "DsStrokeBrush");
        result.SetResourceReference(Border.CornerRadiusProperty, "DsRadiusCard");
        var resultRow = new Grid();
        resultRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        resultRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tick = new SymbolIcon(SymbolRegular.CheckmarkCircle24) { FontSize = Fs(24), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
        tick.SetResourceReference(IconElement.ForegroundProperty, "DsSuccessBrush");
        var resultText = T("", size: TypeRamp.PanelTitle, weight: FontWeights.SemiBold);
        var done = T(Strings.Get("Welcome2_TryDone"), size: TypeRamp.Readable, weight: FontWeights.SemiBold);
        done.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "DsSuccessBrush");
        var resultStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        resultStack.Children.Add(done);
        resultStack.Children.Add(resultText);
        AutomationProperties.SetName(resultText, Strings.Get("Welcome2_TryResultName"));
        AutomationProperties.SetAutomationId(resultText, "WelcomeTryResult");
        AutomationProperties.SetLiveSetting(resultText, AutomationLiveSetting.Polite);
        Grid.SetColumn(resultStack, 1);
        resultRow.Children.Add(tick);
        resultRow.Children.Add(resultStack);
        result.Child = resultRow;
        inner.Children.Add(result);
        card.Child = inner;
        panel.Children.Add(card);

        async Task RunAsync()
        {
            run.IsEnabled = false;
            result.Visibility = Visibility.Visible;
            resultText.Text = Strings.Get("Welcome2_TryRunning");
            tick.Visibility = done.Visibility = Visibility.Collapsed;
            try
            {
                // The runner directly, with no history entry: it only changes the case of the typed text, in memory.
                var r = await _host.Runner.RunAsync(TrySample.Create(input.Text), ContentValue.Empty, new RunOptions { Source = RunSource.Library, RecordHistory = false });
                if (r.Status == RunStatus.Succeeded)
                {
                    resultText.Text = r.Output.AsText();
                    tick.Visibility = done.Visibility = Visibility.Visible;
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
        }
        run.Click += async (_, _) => await RunAsync();
        input.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await RunAsync(); } };

        // The crash-report choice sits under the card as one checkbox row.
        if (Startup.CrashReporting.IsAvailable)
        {
            var crashRow = new StackPanel { Margin = new Thickness(2, 18, 0, 0) };
            _crash = new System.Windows.Controls.CheckBox
            {
                Content = T(Strings.Get("WelcomeDialog_CrashReports")),
                IsChecked = WelcomeFlow.CrashReportsDefault(_host.Settings.Current), FontSize = Fs(TypeRamp.Body),
            };
            AutomationProperties.SetName(_crash, Strings.Get("WelcomeDialog_CrashReports"));
            AutomationProperties.SetAutomationId(_crash, "WelcomeCrashReports");
            _crash.Checked += (_, _) => SaveCrashChoice();
            _crash.Unchecked += (_, _) => SaveCrashChoice();
            crashRow.Children.Add(_crash);
            var whatsSent = new Wpf.Ui.Controls.HyperlinkButton { Content = Strings.Get("WelcomeDialog_WhatsSent"), NavigateUri = WelcomeFlow.WhatsSentUrl, Padding = new Thickness(0), Margin = new Thickness(28, 4, 0, 0), FontSize = Fs(TypeRamp.Readable) };
            AutomationProperties.SetName(whatsSent, Strings.Get("WelcomeDialog_WhatsSent"));
            AutomationProperties.SetAutomationId(whatsSent, "WelcomeWhatsSent");
            crashRow.Children.Add(whatsSent);
            panel.Children.Add(crashRow);
        }

        // Where the app lives: a 34 px tinted square with the tray hint beside it.
        var tray = new Grid { Margin = new Thickness(2, 18, 0, 0) };
        tray.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tray.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var square = new Grid { Width = 34, Height = 34, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        var wash = Tint();
        wash.CornerRadius = new CornerRadius(8);
        square.Children.Add(wash);
        var glyph = new SymbolIcon(SymbolRegular.Alert24) { FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(IconElement.ForegroundProperty, "DsAccentBrush");
        square.Children.Add(glyph);
        tray.Children.Add(square);
        var trayText = T(Strings.Get("Welcome2_TrayNote"), secondary: true, size: TypeRamp.Readable);
        trayText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(trayText, 1);
        tray.Children.Add(trayText);
        panel.Children.Add(tray);
        return Step(WelcomeFlow.TryItStep, panel, Strings.Get("Welcome2_TryTitle"));
    }

    // Frame ---------------------------------------------------------------------------------------------------------

    private void AccessKeys()
    {
        var buttons = new[] { _next, _skip, _recommended, _back };
        foreach (var b in buttons) if (b.Content is string s) b.Content = s.Replace("_", "");
        AutomationPortal.Editor.UI.AccessKeys.Apply(buttons);
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
            _dots[d].SetResourceReference(Shape.FillProperty, active ? "DsAccentBrush" : "DsTextTertiaryBrush");
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
