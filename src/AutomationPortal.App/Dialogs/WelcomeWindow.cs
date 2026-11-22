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
    /// <summary>The one left/right inset shared by the stepper, the content and the footer.</summary>
    private const double Inset = 40;
    private readonly StackPanel _dotRow = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(Inset, 4, Inset, 8) };
    private readonly Wpf.Ui.Controls.Button _next = new(), _back = new(), _skip = new(), _recommended = new();
    private readonly ScrollViewer _scroll = new();
    private System.Windows.Controls.Primitives.ToggleButton? _crash;
    private UIElement? _tryInput;
    private bool _finished, _accepted;

    public WelcomeWindow(AppHost host)
    {
        _host = host;
        Title = Strings.Get("WelcomeDialog_WelcomeToAutomationPortal");
        var work = SystemParameters.WorkArea;
        // The window grows with the text size so large text still shows a whole step, up to the work area.
        var grow = Math.Max(1, _host.Settings.Current.TextScale / 100.0);
        Width = Math.Min(720 * grow, work.Width * 0.95);
        Height = Math.Min(620 * grow, work.Height * 0.95);
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
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var bar = new TitleBar { Title = Title, ShowMinimize = false, ShowMaximize = false, CanMaximize = false };
        bar.Icon = new ImageIcon { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/automationportal.png")) };   // same bare icon as the main window, so the title bar sizes and places it
        root.Children.Add(bar);

        for (var i = 0; i < WelcomeFlow.StepCount; i++)
        {
            var dot = new Rectangle { Height = 8, Width = 8, RadiusX = 4, RadiusY = 4, Margin = new Thickness(0, 0, 6, 0) };
            _dots.Add(dot);
            _dotRow.Children.Add(dot);
        }
        AutomationProperties.SetName(_dotRow, "Progress");
        AutomationProperties.SetAutomationId(_dotRow, "WelcomeProgress");
        Grid.SetRow(_dotRow, 1);
        root.Children.Add(_dotRow);

        _stepHost.Margin = new Thickness(Inset, 8, Inset, 16);
        _stepHost.IsTabStop = false;
        _stepHost.VerticalContentAlignment = VerticalAlignment.Stretch;
        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scroll.Content = _stepHost;
        // A 16 px fade at the bottom edge, so scrolled content never ends mid-glyph against the footer.
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fade.GradientStops.Add(new GradientStop(Colors.Black, 0));
        fade.GradientStops.Add(new GradientStop(Colors.Black, 0.95));
        fade.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        _scroll.OpacityMask = fade;
        _scroll.SizeChanged += (_, e) =>
        {
            var at = e.NewSize.Height > 32 ? 1 - 16 / e.NewSize.Height : 0.95;
            fade.GradientStops[1].Offset = at;
        };
        Grid.SetRow(_scroll, 2);
        root.Children.Add(_scroll);

        var divider = new Border { Height = 1 };
        divider.SetResourceReference(Border.BackgroundProperty, "DsStrokeBrush");
        Grid.SetRow(divider, 3);
        root.Children.Add(divider);

        // Left: ways out as text-style buttons (they wrap or stack when space runs out). Right: Back, then the one filled main button, 8 px apart.
        var row = new Grid { Margin = new Thickness(Inset, 14, Inset, 20) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        TextStyle(_skip);
        _skip.IsCancel = true;
        _skip.Click += (_, _) => Finish(false);
        AutomationProperties.SetAutomationId(_skip, "WelcomeSkip");
        TextStyle(_recommended);
        _recommended.Click += (_, _) => StartRecommended();
        AutomationProperties.SetAutomationId(_recommended, "WelcomeRecommended");
        left.Children.Add(_skip);
        left.Children.Add(_recommended);
        row.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
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
        Grid.SetRow(row, 4);
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
        if (secondary && !HighContrast.Active) t.Foreground = SecondaryBrush(IsDarkTheme);
        return t;
    }

    /// <summary>Secondary text takes its colour from the token values directly (#FFF at 79% in dark, #000 at 61% in light, alpha in the colour, element opacity 1), so no style or lookup order can turn it white or fade it further.</summary>
    public static Color SecondaryColor(bool dark) => dark ? Color.FromArgb(0xC9, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x9C, 0, 0, 0);

    private static Brush SecondaryBrush(bool dark) => Frozen(SecondaryColor(dark));

    /// <summary>A secondary-colour text block, the same one the welcome steps use for body copy, subtitles and card descriptions.</summary>
    public static System.Windows.Controls.TextBlock SecondaryText(string text, double size, bool dark)
    {
        var t = Ui.Text(text, true, size);
        t.Foreground = SecondaryBrush(dark);
        return t;
    }

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    /// <summary>Gives a footer button the accent text look: no border or fill, accent-coloured label.</summary>
    private static void TextStyle(Wpf.Ui.Controls.Button b)
    {
        b.Appearance = ControlAppearance.Transparent;
        b.BorderThickness = new Thickness(0);
        b.Padding = new Thickness(0, 6, 14, 6);
        b.HorizontalContentAlignment = HorizontalAlignment.Left;
        b.SetResourceReference(Control.ForegroundProperty, "DsAccentTextBrush");
    }

    /// <summary>Turns a footer label into wrapping text at the in-app text size (a bare string label does not follow it), keeping the access key mark.</summary>
    private void Wrap(Wpf.Ui.Controls.Button b)
    {
        if (b.Content is string s) b.Content = new AccessText { Text = s, TextWrapping = TextWrapping.Wrap, FontSize = Fs(TypeRamp.Body) };
    }

    private static bool IsDarkTheme => Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme() == Wpf.Ui.Appearance.ApplicationTheme.Dark;

    private System.Windows.Controls.TextBlock Heading(string text, double size = TypeRamp.Title)
    {
        var t = T(text, size: size, weight: FontWeights.SemiBold);
        t.Margin = new Thickness(0, 0, 0, 4);
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
        // Centred in the space the scroll area offers (it only scrolls when the content is taller than that).
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 520 };
        var hero = BuildHero();
        hero.Margin = new Thickness(0, 0, 0, 12);
        panel.Children.Add(hero);
        var title = Heading(Strings.Get("Welcome2_HelloTitle"), TypeRamp.Title);
        title.TextAlignment = TextAlignment.Center;
        panel.Children.Add(title);
        var body = T(Strings.Get("Welcome2_HelloBody"), secondary: true);
        body.TextAlignment = TextAlignment.Center;
        body.MaxWidth = 480;
        body.LineHeight = Fs(20);
        panel.Children.Add(body);
        var hint = T(Strings.Get("Welcome2_HelloHint"), secondary: true, size: TypeRamp.Readable);
        hint.TextAlignment = TextAlignment.Center;
        hint.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(hint);
        return Step(WelcomeFlow.HelloStep, panel, Strings.Get("Welcome2_HelloTitle"));
    }

    /// <summary>The lightning bolt on a disc, ringed by six category squares. They pop in and bob gently; with reduced motion or high contrast everything stands still.</summary>
    private static FrameworkElement BuildHero()
    {
        const double size = 240, orbit = 96, chipSize = 44;
        var hc = HighContrast.Active;
        var grid = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetAutomationId(grid, "WelcomeHero");
        AutomationProperties.SetName(grid, Strings.Get("Welcome2_HeroName"));

        var disc = new Ellipse { Width = 136, Height = 136, StrokeThickness = 1 };
        if (hc) { disc.Fill = SystemColors.WindowBrush; disc.Stroke = SystemColors.WindowTextBrush; }
        else { disc.SetResourceReference(Shape.FillProperty, "DsCardBrush"); disc.SetResourceReference(Shape.StrokeProperty, "DsStrokeLipBrush"); }   // the stronger control stroke keeps the disc visible on the light surface
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
            var chip = Ui.Chip(icon, colour, chipSize, 22);
            chip.CornerRadius = new CornerRadius(10);
            chip.RenderTransformOrigin = new Point(0.5, 0.5);
            var scale = new ScaleTransform(1, 1);
            var bob = new TranslateTransform();
            chip.RenderTransform = new TransformGroup { Children = { scale, bob } };
            Canvas.SetLeft(chip, size / 2 + Math.Cos(angle) * orbit - chipSize / 2);
            Canvas.SetTop(chip, size / 2 + Math.Sin(angle) * orbit - chipSize / 2);
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
            Stretch = Stretch.Uniform, Width = 58, Height = 70, StrokeLineJoin = PenLineJoin.Round,
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
        var panel = new StackPanel { MaxWidth = 640 };
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
                Margin = new Thickness(12, 8, 12, 8), Padding = new Thickness(8, 0, 0, 0), FontSize = Fs(TypeRamp.Body),
            };
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var chip = Ui.Chip(item.Shortcut.Icon, item.Shortcut.Color, 36, 20);
            chip.Margin = new Thickness(0, 0, 12, 0);
            row.Children.Add(chip);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(T(item.Shortcut.Name, size: TypeRamp.Body, weight: FontWeights.SemiBold));
            var blurb = Blurb(item);
            if (blurb.Length > 0)
            {
                // One line only: longer descriptions end in an ellipsis and show in full as a tooltip.
                var d = T(blurb, secondary: true, size: TypeRamp.Readable);
                d.TextWrapping = TextWrapping.NoWrap;
                d.TextTrimming = TextTrimming.CharacterEllipsis;
                d.ToolTip = blurb;
                text.Children.Add(d);
            }
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            if (recommended.Contains(n))
            {
                var pill = RecommendedPill();
                Grid.SetColumn(pill, 2);
                row.Children.Add(pill);
            }
            box.Content = row;
            AutomationProperties.SetName(box, item.Shortcut.Name);
            AutomationProperties.SetAutomationId(box, "WelcomeStarter" + ++n);
            var tint = Tint();
            var face = new Grid();
            face.Children.Add(tint);
            face.Children.Add(box);
            var card = new Border { Margin = new Thickness(0, 0, 0, 6), BorderThickness = new Thickness(1), Child = face };
            card.SetResourceReference(Border.BackgroundProperty, "DsCardBrush");
            card.SetResourceReference(Border.CornerRadiusProperty, "DsRadiusCard");
            void Paint()
            {
                var on = box.IsChecked == true;
                // The normal card stroke stays; the tint marks the choice. High contrast has no tint, so its border carries it.
                var hcOn = on && HighContrast.Active;
                card.SetResourceReference(Border.BorderBrushProperty, hcOn ? "DsAccentBrush" : "DsStrokeBrush");
                card.BorderThickness = new Thickness(hcOn ? 2 : 1);
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

    /// <summary>A small accent pill for the starters that are ticked to begin with.</summary>
    private System.Windows.Controls.Border RecommendedPill()
    {
        var label = T(Strings.Get("Welcome2_Recommended"), size: TypeRamp.Caption, weight: FontWeights.SemiBold);
        label.TextWrapping = TextWrapping.NoWrap;
        label.Margin = new Thickness(8, 2, 8, 3);
        label.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "DsAccentTextBrush");
        var face = new Grid();
        var wash = Tint();
        wash.CornerRadius = new CornerRadius(20);
        face.Children.Add(wash);
        face.Children.Add(label);
        var pill = new Border { CornerRadius = new CornerRadius(20), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = face };
        if (HighContrast.Active)
        {
            pill.BorderThickness = new Thickness(1);
            pill.BorderBrush = SystemColors.WindowTextBrush;
        }
        return pill;
    }

    private Border Card(UIElement child, Thickness margin)
    {
        var card = new Border { Margin = margin, Padding = new Thickness(14), BorderThickness = new Thickness(1), Child = child };
        card.SetResourceReference(Border.BackgroundProperty, "DsCardBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "DsStrokeBrush");
        card.SetResourceReference(Border.CornerRadiusProperty, "DsRadiusCard");
        return card;
    }

    private UserControl BuildTryIt()
    {
        var panel = new StackPanel { MaxWidth = 640 };
        panel.Children.Add(Heading(Strings.Get("Welcome2_TryTitle")));
        panel.Children.Add(T(Strings.Get("Welcome2_TryBody"), secondary: true));

        var inner = new StackPanel();
        var card = Card(inner, new Thickness(0, 16, 0, 0));
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var chip = Ui.Chip("TextCaseTitle24", "purple", 36, 20);
        chip.Margin = new Thickness(0, 0, 12, 0);
        head.Children.Add(chip);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(T(Strings.Get("Welcome2_TrySampleName"), size: TypeRamp.PanelTitle, weight: FontWeights.SemiBold));
        names.Children.Add(T(Strings.Get("Welcome2_TrySampleHelp"), secondary: true, size: TypeRamp.Readable));
        Grid.SetColumn(names, 1);
        head.Children.Add(names);
        inner.Children.Add(head);

        // The input and its button share one row, so the button stays beside what it runs.
        var inputRow = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var input = new Wpf.Ui.Controls.TextBox { Text = TrySample.DefaultText, MaxLength = TrySample.MaxLength, FontSize = Fs(TypeRamp.Body), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(input, Strings.Get("Welcome2_TryInputName"));
        AutomationProperties.SetAutomationId(input, "WelcomeTryText");
        _tryInput = input;
        inputRow.Children.Add(input);
        var run = new Wpf.Ui.Controls.Button { Content = Strings.Get("Welcome2_TryRun"), Icon = new SymbolIcon(SymbolRegular.Play24), Margin = new Thickness(8, 0, 0, 0), MinWidth = Fs(130), FontSize = Fs(TypeRamp.Body), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(run, "WelcomeTryIt");
        Grid.SetColumn(run, 1);
        inputRow.Children.Add(run);
        inner.Children.Add(inputRow);

        var result = new Border { Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(10, 6, 10, 6), BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed };
        result.SetResourceReference(Border.BorderBrushProperty, "DsStrokeBrush");
        result.SetResourceReference(Border.CornerRadiusProperty, "DsRadiusCard");
        var resultRow = new Grid();
        resultRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        resultRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tick = new SymbolIcon(SymbolRegular.CheckmarkCircle24) { FontSize = Fs(20), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
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

        // The crash-report choice is a card row like the one above: icon, title and description on the left, one switch on the right.
        if (Startup.CrashReporting.IsAvailable)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new SymbolIcon(SymbolRegular.ShieldCheckmark24) { FontSize = 16, Margin = new Thickness(2, 3, 12, 0), VerticalAlignment = VerticalAlignment.Top };
            icon.SetResourceReference(IconElement.ForegroundProperty, "DsTextPrimaryBrush");
            row.Children.Add(icon);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(T(Strings.Get("Welcome2_CrashTitle"), size: TypeRamp.Body, weight: FontWeights.SemiBold));
            text.Children.Add(T(Strings.Get("Welcome2_CrashDesc"), secondary: true, size: TypeRamp.Readable));
            var whatsSent = new Wpf.Ui.Controls.HyperlinkButton { Content = Strings.Get("WelcomeDialog_WhatsSent"), NavigateUri = WelcomeFlow.WhatsSentUrl, Padding = new Thickness(0), Margin = new Thickness(0, 4, 0, 0), FontSize = Fs(TypeRamp.Readable) };
            AutomationProperties.SetName(whatsSent, Strings.Get("WelcomeDialog_WhatsSent"));
            AutomationProperties.SetAutomationId(whatsSent, "WelcomeWhatsSent");
            text.Children.Add(whatsSent);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            _crash = new Wpf.Ui.Controls.ToggleSwitch { IsChecked = WelcomeFlow.CrashReportsDefault(_host.Settings.Current), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            AutomationProperties.SetName(_crash, Strings.Get("Welcome2_CrashTitle"));
            AutomationProperties.SetAutomationId(_crash, "WelcomeCrashReports");
            _crash.Checked += (_, _) => SaveCrashChoice();
            _crash.Unchecked += (_, _) => SaveCrashChoice();
            Grid.SetColumn(_crash, 2);
            row.Children.Add(_crash);
            panel.Children.Add(Card(row, new Thickness(0, 12, 0, 0)));
        }

        // Plain footnotes: where the app lives, and that nothing here is final.
        var notes = new StackPanel { Margin = new Thickness(2, 16, 0, 0) };
        notes.Children.Add(T(Strings.Get("Welcome2_TrayNote"), secondary: true, size: TypeRamp.Readable));
        var later = T(Strings.Get("Welcome2_ChangeLater"), secondary: true, size: TypeRamp.Readable);
        later.Margin = new Thickness(0, 4, 0, 0);
        notes.Children.Add(later);
        panel.Children.Add(notes);
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
        _next.Content = Strings.Get(i == 0 ? "Welcome2_LetsGo" : _flow.IsLast ? "Welcome2_StartUsing" : "Welcome2_Next");
        _back.Content = Strings.Get("Welcome2_Back");
        _skip.Content = Strings.Get("Welcome2_SkipForNow");
        _recommended.Content = Strings.Get("Welcome2_StartRecommended");
        _recommended.Visibility = _flow.IsLast ? Visibility.Collapsed : Visibility.Visible;
        _dotRow.Visibility = i == WelcomeFlow.HelloStep ? Visibility.Collapsed : Visibility.Visible;
        AccessKeys();
        foreach (var b in new[] { _next, _skip, _recommended, _back }) Wrap(b);
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
