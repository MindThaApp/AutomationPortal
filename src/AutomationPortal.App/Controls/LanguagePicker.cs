using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AutomationPortal.App.Logic;
using AutomationPortal.Editor;

namespace AutomationPortal.App.Controls;

/// <summary>
/// Picks the app's display language: Windows default plus every shipped language, each with its flag, its own name and the
/// English name. The choice is saved at once and shown after a restart, which is only ever offered, never forced.
/// </summary>
public sealed class LanguagePicker : UserControl
{
    private readonly AppHost _host;
    private readonly ListBox _list = new();
    private readonly TextBlock _welcome = new() { FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 12, 0, 0), Opacity = 0 };
    private readonly Border _restartBar = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(12, 8, 8, 8) };
    private readonly List<Row> _rows = new();
    private bool _loading;

    private sealed record Row(string Code, ListBoxItem Item, Border Pill, ScaleTransform FlagScale, TranslateTransform Lift);

    public LanguagePicker(AppHost host, int columns = 2)
    {
        _host = host;
        Focusable = false;
        var stack = new StackPanel();

        // No scroll viewer of its own: the page or wizard around it scrolls, so the mouse wheel never gets stuck here.
        var template = new ControlTemplate(typeof(ListBox)) { VisualTree = new FrameworkElementFactory(typeof(ItemsPresenter)) };
        _list.Template = template;
        _list.Background = Brushes.Transparent;
        _list.BorderThickness = new Thickness(0);
        _list.SelectionMode = SelectionMode.Single;
        var panel = new FrameworkElementFactory(typeof(UniformGrid));
        panel.SetValue(UniformGrid.ColumnsProperty, Math.Max(1, columns));
        _list.ItemsPanel = new ItemsPanelTemplate(panel);
        AutomationProperties.SetName(_list, Strings.Get("Language_ListName"));
        AutomationProperties.SetAutomationId(_list, "LanguageList");
        KeyboardNavigation.SetDirectionalNavigation(_list, KeyboardNavigationMode.Contained);

        _list.Items.Add(BuildRow("", WindowsName(), WindowsSecondary(), null));
        foreach (var l in UiLanguage.All) _list.Items.Add(BuildRow(l.Code, l.NativeName, l.EnglishName, l));
        _list.SelectionChanged += OnSelectionChanged;
        stack.Children.Add(_list);

        _welcome.FontSize = TypeRamp.Subtitle;
        _welcome.SetResourceReference(TextBlock.ForegroundProperty, "DsAccentTextBrush");
        AutomationProperties.SetLiveSetting(_welcome, AutomationLiveSetting.Polite);
        stack.Children.Add(_welcome);

        stack.Children.Add(BuildRestartBar());
        Content = stack;
        Loaded += (_, _) => Select(_host.Settings.Current.Language ?? "", animate: false);
    }

    private static string WindowsName() => Strings.Get("Language_WindowsDefault");

    private static string WindowsSecondary() => UiLanguage.Resolve("").EnglishName;

    private ListBoxItem BuildRow(string code, string nativeName, string englishName, UiLanguageInfo? info)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // The selection pill grows in from the middle of the row's leading edge.
        var pill = new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 0) };
        pill.SetResourceReference(Border.BackgroundProperty, "DsAccentBrush");
        grid.Children.Add(pill);

        var flagScale = new ScaleTransform(1, 1);
        var flag = BuildFlag(info);
        flag.RenderTransformOrigin = new Point(0.5, 0.5);
        flag.RenderTransform = flagScale;
        flag.Margin = new Thickness(0, 0, 10, 0);
        Grid.SetColumn(flag, 1);
        grid.Children.Add(flag);

        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var main = new TextBlock { Text = nativeName, FontSize = TypeRamp.Body, TextTrimming = TextTrimming.CharacterEllipsis };
        var secondary = new TextBlock { Text = englishName, FontSize = TypeRamp.Caption, TextTrimming = TextTrimming.CharacterEllipsis };
        secondary.SetResourceReference(TextBlock.ForegroundProperty, "DsTextSecondaryBrush");
        if (info?.IsRightToLeft == true) main.FlowDirection = FlowDirection.RightToLeft;
        names.Children.Add(main);
        if (!string.Equals(nativeName, englishName, StringComparison.Ordinal)) names.Children.Add(secondary);
        Grid.SetColumn(names, 2);
        grid.Children.Add(names);

        var lift = new TranslateTransform();
        var item = new ListBoxItem { Content = grid, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 6, 4), RenderTransform = lift, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        item.ToolTip = string.IsNullOrEmpty(code) ? Strings.Get("Language_WindowsDefaultTip") : englishName;
        AutomationProperties.SetName(item, string.Equals(nativeName, englishName, StringComparison.Ordinal) ? nativeName : nativeName + ", " + englishName);
        AutomationProperties.SetAutomationId(item, "Language_" + (string.IsNullOrEmpty(code) ? "Windows" : code));
        // Hover lifts the row a little; reduced motion makes it instant.
        item.MouseEnter += (_, _) => Motion.Animate(lift, TranslateTransform.YProperty, lift.Y, -1.5, MotionRole.Fast, Motion.Settle);
        item.MouseLeave += (_, _) => Motion.Animate(lift, TranslateTransform.YProperty, lift.Y, 0, MotionRole.Fast, Motion.Settle);
        _rows.Add(new Row(code, item, pill, flagScale, lift));
        return item;
    }

    /// <summary>A 20 x 15 flag with a 1 px theme border, a text badge, or the globe for Windows default. Decorative: the row name is the label.</summary>
    private static FrameworkElement BuildFlag(UiLanguageInfo? info)
    {
        var frame = new DecorativeBorder { Width = FlagArt.W + 2, Height = FlagArt.H + 2, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        frame.SetResourceReference(Border.BorderBrushProperty, "DsStrokeBrush");
        if (info is null)
        {
            frame.BorderThickness = new Thickness(0);
            var globe = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            globe.SetResourceReference(TextBlock.ForegroundProperty, "DsAccentTextBrush");
            frame.Child = globe;
        }
        else if (FlagArt.For(info.FlagRegion) is { } image)
        {
            frame.Child = new Image { Source = image, Width = FlagArt.W, Height = FlagArt.H, Stretch = Stretch.Fill, SnapsToDevicePixels = true };
        }
        else
        {
            frame.SetResourceReference(Border.BackgroundProperty, "DsSelectionFillBrush");
            var badge = new TextBlock { Text = info.Badge ?? info.Code[..Math.Min(2, info.Code.Length)].ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            badge.SetResourceReference(TextBlock.ForegroundProperty, "DsAccentTextBrush");
            frame.Child = badge;
        }
        return frame;
    }

    private FrameworkElement BuildRestartBar()
    {
        _restartBar.SetResourceReference(Border.BackgroundProperty, "DsSelectionFillBrush");
        _restartBar.SetResourceReference(Border.CornerRadiusProperty, "DsRadiusCard");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBlock { Text = Strings.Get("Language_RestartToSwitch"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) };
        grid.Children.Add(text);
        var restart = new Wpf.Ui.Controls.Button { Content = Strings.Get("Language_RestartNow"), Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, ToolTip = Strings.Get("Language_RestartNowTip") };
        AutomationProperties.SetHelpText(restart, Strings.Get("Language_RestartNowTip"));
        AutomationProperties.SetAutomationId(restart, "LanguageRestart");
        restart.Click += (_, _) => RestartIfSafe();
        Grid.SetColumn(restart, 1);
        grid.Children.Add(restart);
        _restartBar.Child = grid;
        AutomationProperties.SetLiveSetting(_restartBar, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(_restartBar, Strings.Get("Language_RestartToSwitch"));
        return _restartBar;
    }

    private void Select(string code, bool animate)
    {
        _loading = true;
        try
        {
            var row = _rows.FirstOrDefault(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase)) ?? _rows[0];
            _list.SelectedItem = row.Item;
            Show(row, animate);
        }
        finally { _loading = false; }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var row = _rows.FirstOrDefault(r => ReferenceEquals(r.Item, _list.SelectedItem));
        if (row is null) return;
        // Saved through the normal settings store; the culture itself changes on the next start.
        if (!string.Equals(_host.Settings.Current.Language ?? "", row.Code, StringComparison.Ordinal))
            _host.Settings.Update(s => s.Language = row.Code);
        Show(row, animate: true);
    }

    private void Show(Row selected, bool animate)
    {
        foreach (var r in _rows)
        {
            var on = ReferenceEquals(r, selected);
            var pillScale = (ScaleTransform)r.Pill.RenderTransform;
            if (!animate) { pillScale.ScaleY = on ? 1 : 0; continue; }
            Motion.Animate(pillScale, ScaleTransform.ScaleYProperty, pillScale.ScaleY, on ? 1 : 0, on ? MotionRole.Enter : MotionRole.Leave, on ? Motion.Settle : Motion.Leave);
            if (on)
            {
                // The flag settles in from slightly small: a gentle pop without overshoot.
                Motion.Animate(r.FlagScale, ScaleTransform.ScaleXProperty, 0.82, 1, MotionRole.EnterLong, Motion.Settle);
                Motion.Animate(r.FlagScale, ScaleTransform.ScaleYProperty, 0.82, 1, MotionRole.EnterLong, Motion.Settle);
            }
        }

        var language = UiLanguage.Resolve(selected.Code);
        _welcome.Text = language.Welcome;
        _welcome.FlowDirection = language.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        AutomationProperties.SetName(_welcome, language.Welcome);
        var fadeMs = Motion.Reduced ? Math.Min(Motion.ReducedFadeMs, 100) : Motion.Nominal(MotionRole.EnterLong);
        if (animate && fadeMs > 0) Motion.Animate(_welcome, OpacityProperty, 0, 1, fadeMs, Motion.Settle);
        else _welcome.Opacity = 1;

        _restartBar.Visibility = UiLanguage.NeedsRestart(selected.Code) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Closes the other open windows first, the normal way, so each can ask about unsaved work; if any stays open the restart
    /// is called off. Only then does the app restart with the same arguments.
    /// </summary>
    private void RestartIfSafe()
    {
        var here = Window.GetWindow(this);
        var main = Application.Current.MainWindow;
        foreach (var w in Application.Current.Windows.OfType<Window>().ToList())
        {
            if (ReferenceEquals(w, here) || ReferenceEquals(w, main) || !w.IsVisible) continue;
            try { w.Close(); } catch (InvalidOperationException) { }
        }
        var stillOpen = Application.Current.Windows.OfType<Window>().Any(w => !ReferenceEquals(w, here) && !ReferenceEquals(w, main) && w.IsVisible);
        if (stillOpen) return;
        _host.Restart();
    }
}

/// <summary>A border that screen readers skip, with everything inside it: for pictures whose meaning is already in a nearby name.</summary>
public sealed class DecorativeBorder : Border
{
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(DecorativeBorder owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override bool IsControlElementCore() => false;
        protected override bool IsContentElementCore() => false;
        protected override List<AutomationPeer>? GetChildrenCore() => null;
    }
}
