using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using AutomationPortal.App.Services;
using AutomationPortal.Editor;
using AutomationPortal.App.ViewModels;

namespace AutomationPortal.App.Controls;

public partial class ShortcutTile : UserControl
{
    public ShortcutTile()
    {
        InitializeComponent();
        Loaded += (_, _) => { if (!ShowMoreButton) MoreButton.Visibility = Visibility.Collapsed; };
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => AutomationPortal.Editor.HighContrast.Changed += OnHighContrastChanged;
        Unloaded += (_, _) => AutomationPortal.Editor.HighContrast.Changed -= OnHighContrastChanged;
        Root.MouseEnter += (_, _) => { ShowMore(true); Hover(true); };
        Root.MouseLeave += (_, _) => { if (!MoreButton.IsKeyboardFocused) ShowMore(false); Hover(false); };
        MoreButton.GotKeyboardFocus += (_, _) => ShowMore(true);
        TileButton.PreviewMouseLeftButtonDown += (_, _) => Press(true);
        TileButton.PreviewMouseLeftButtonUp += (_, _) => Press(false);
        TileButton.LostMouseCapture += (_, _) => Press(false);
        MoreButton.LostKeyboardFocus += (_, _) => { if (!Root.IsMouseOver) ShowMore(false); };
        TileButton.SelectAction = () => { if (Tile is { } st) SelectRequested?.Invoke(this, st); };
        TileButton.GotKeyboardFocus += (_, _) => { ShowMore(true); if (Tile is { ExplicitActions: true } ft) SelectRequested?.Invoke(this, ft); };
        TileButton.LostKeyboardFocus += (_, _) => { if (!Root.IsMouseOver && !MoreButton.IsKeyboardFocused) ShowMore(false); };
    }

    public ShortcutTileViewModel? Tile => DataContext as ShortcutTileViewModel;

    /// <summary>False in the Quick Panel, which has no per-tile menu.</summary>
    public bool ShowMoreButton { get; set; } = true;

    public event EventHandler<ShortcutTileViewModel>? RunRequested;
    /// <summary>Raised when a tile is clicked or focused in the library, where a click only selects.</summary>
    public event EventHandler<ShortcutTileViewModel>? SelectRequested;
    /// <summary>Raised for the "..." button and for right-click / the Menu key. The element is the anchor for the menu.</summary>
    public event EventHandler<(ShortcutTileViewModel Tile, FrameworkElement Anchor, bool AtPointer)>? MenuRequested;

    /// <summary>Raised for Ctrl+E on the tile.</summary>
    public event EventHandler<ShortcutTileViewModel>? EditRequested;

    /// <summary>Raised for Space on the selected tile: "Show what it would do".</summary>
    public event EventHandler<ShortcutTileViewModel>? PreviewRequested;

    public Button Button => TileButton;

    private const double RestOpacity = 0.7;

    private void ShowMore(bool show)
    {
        AutomationPortal.Editor.Motion.Animate(MoreButton, OpacityProperty, null, show ? 1 : RestOpacity, show ? MotionRole.Leave : MotionRole.Enter);
    }

    // Tile motion is coded rather than in XAML storyboards so it follows the Windows animation setting while the app runs.
    private const double HoverScale = 1.025, PressScale = 0.98;
    private bool _pressed;

    private void ScaleTo(double to, MotionRole role)
    {
        Motion.Animate(Scale, ScaleTransform.ScaleXProperty, Scale.ScaleX, to, role);
        Motion.Animate(Scale, ScaleTransform.ScaleYProperty, Scale.ScaleY, to, role);
    }

    private void Hover(bool over)
    {
        if (!_pressed) ScaleTo(over ? HoverScale : 1, over ? MotionRole.Fast : MotionRole.EnterLong);
        if (TileButton.Template?.FindName("Hover", TileButton) is Border veil)
            Motion.Animate(veil, OpacityProperty, null, over ? 1 : 0, over ? MotionRole.Fast : MotionRole.EnterLong);
        // Elevation 1 on hover only; null in high contrast, where shadows are off.
        Lift.Effect = over ? AutomationPortal.Editor.HighContrast.Shadow(4, 2, Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme() == Wpf.Ui.Appearance.ApplicationTheme.Dark ? 0.10 : 0.04) : null;
    }

    private void Press(bool down)
    {
        if (_pressed == down) return;
        _pressed = down;
        ScaleTo(down ? PressScale : Root.IsMouseOver ? HoverScale : 1, MotionRole.Press);
    }

    // ---- run state badge -------------------------------------------------------------------

    private int _badgeToken;
    private bool _stopShown;

    private void ApplyBadge(TileRunState state, bool animate)
    {
        var token = ++_badgeToken;
        if (state != TileRunState.Running) ShowRunAsStop(false);
        switch (state)
        {
            case TileRunState.Running:
                HideBadge(animate);
                if (!animate) { ShowSpinner(false); ShowRunAsStop(true); return; }
                _ = ShowSpinnerAfterDelayAsync(token);
                break;
            case TileRunState.Succeeded:
                HideSpinner();
                ShowMark(SuccessMark, FailMark, animate, pop: true);
                break;
            case TileRunState.Failed:
                HideSpinner();
                ShowMark(FailMark, SuccessMark, animate, pop: false);
                break;
            default:
                HideSpinner();
                HideBadge(animate);
                break;
        }
    }

    private async Task ShowSpinnerAfterDelayAsync(int token)
    {
        await Task.Delay(RunFeedback.SpinnerDelayMs);
        if (RunFeedback.ShouldShowSpinner(Tile?.State ?? TileRunState.Idle, token, _badgeToken)) { ShowSpinner(true); ShowRunAsStop(true); }
    }

    /// <summary>While a run is still going after the short pause, the Run button is a Stop button; Toggle already stops a running shortcut.</summary>
    private void ShowRunAsStop(bool stop)
    {
        _stopShown = stop;
        RunButton.Content = Strings.Get(stop ? "ShortcutTile_Stop" : "ShortcutTile_Run");
        RunButton.Icon = new Wpf.Ui.Controls.SymbolIcon(stop ? Wpf.Ui.Controls.SymbolRegular.Stop16 : Wpf.Ui.Controls.SymbolRegular.Play16);
        RunButton.ToolTip = Strings.Get(stop ? "ShortcutTile_StopTip" : "ShortcutTile_RunTip");
        System.Windows.Automation.AutomationProperties.SetName(RunButton, string.Format(Strings.Get(stop ? "ShortcutTile_StopNamed" : "ShortcutTile_RunNamed"), Tile?.Name));
    }

    private void ShowSpinner(bool animate)
    {
        SpinnerHost.Visibility = Visibility.Visible;
        if (animate) Motion.Animate(SpinnerHost, OpacityProperty, 0, 1, MotionRole.Fast);
        else { SpinnerHost.BeginAnimation(OpacityProperty, null); SpinnerHost.Opacity = 1; }
    }

    private void HideSpinner()
    {
        SpinnerHost.BeginAnimation(OpacityProperty, null);
        SpinnerHost.Visibility = Visibility.Collapsed;
    }

    private void ShowMark(Border show, Border other, bool animate, bool pop)
    {
        other.Visibility = Visibility.Collapsed;
        show.Visibility = Visibility.Visible;
        Badge.Visibility = Visibility.Visible;
        BadgeScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        BadgeScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        BadgeScale.ScaleX = BadgeScale.ScaleY = 1;
        if (!animate) { Badge.BeginAnimation(OpacityProperty, null); Badge.Opacity = 1; return; }
        Motion.Animate(Badge, OpacityProperty, 0, 1, MotionRole.Enter);
        if (!pop) return;
        // The one bouncy moment: the success tick overshoots; the failure mark never does.
        var bounce = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut };
        Motion.Animate(BadgeScale, ScaleTransform.ScaleXProperty, 0.3, 1, MotionRole.Emphasis, bounce);
        Motion.Animate(BadgeScale, ScaleTransform.ScaleYProperty, 0.3, 1, MotionRole.Emphasis, bounce);
    }

    private void HideBadge(bool animate)
    {
        if (Badge.Visibility != Visibility.Visible) return;
        var token = _badgeToken;
        if (!animate || Motion.Reduced)
        {
            Badge.BeginAnimation(OpacityProperty, null);
            Badge.Visibility = Visibility.Collapsed;
            return;
        }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(Motion.Nominal(MotionRole.LeaveLong))) { EasingFunction = Motion.EaseIn };
        fade.Completed += (_, _) =>
        {
            if (token != _badgeToken) return;
            Badge.BeginAnimation(OpacityProperty, null);
            Badge.Visibility = Visibility.Collapsed;
        };
        Motion.Begin(Badge, OpacityProperty, fade);
    }

    private void OnTileClick(object sender, RoutedEventArgs e)
    {
        if (Tile is not { } t) return;
        if (t.ExplicitActions) SelectRequested?.Invoke(this, t);
        else RunRequested?.Invoke(this, t);
    }

    private void OnTileDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Tile is { ExplicitActions: true } t) { RunRequested?.Invoke(this, t); e.Handled = true; }
    }

    private void OnRunClick(object sender, RoutedEventArgs e)
    {
        if (Tile is { } t) { SelectRequested?.Invoke(this, t); RunRequested?.Invoke(this, t); }
    }

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (Tile is { } t) { SelectRequested?.Invoke(this, t); EditRequested?.Invoke(this, t); }
    }

    /// <summary>A neutral card with a solid category square and a white glyph. In high contrast the square uses the system highlight.</summary>
    private void ApplyAppearance()
    {
        if (Tile is not { } t) return;
        var explicitActions = t.ExplicitActions;
        Actions.Visibility = explicitActions ? Visibility.Visible : Visibility.Collapsed;
        NameStack.Margin = new Thickness(14, 0, 14, explicitActions ? 46 : 12);
        if (AutomationPortal.Editor.HighContrast.Active)
        {
            Chip.Background = SystemColors.HighlightBrush;
            GlyphIcon.Foreground = SystemColors.HighlightTextBrush;
        }
        else
        {
            Chip.SetResourceReference(Border.BackgroundProperty, t.CategoryKey);
            GlyphIcon.Foreground = Brushes.White;
        }
        SelectRing.Visibility = t.IsSelected ? Visibility.Visible : Visibility.Collapsed;
        // High contrast has no tint: the thicker accent border carries the selection.
        SelectRing.Background = AutomationPortal.Editor.HighContrast.Active ? Brushes.Transparent : (Brush)FindResource("DsSelectionFillBrush");
        SelectRing.BorderThickness = new Thickness(AutomationPortal.Editor.HighContrast.Active ? 3 : 2);
        TileButton.IsTileSelected = t.IsSelected;
        var name = t.Name;
        System.Windows.Automation.AutomationProperties.SetName(EditButton, string.Format(Strings.Get("ShortcutTile_EditNamed"), name));
        System.Windows.Automation.AutomationProperties.SetName(MoreButton, string.Format(Strings.Get("ShortcutTile_MoreNamed"), name));
        ShowRunAsStop(_stopShown);
    }
    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (Tile is { } t) MenuRequested?.Invoke(this, (t, MoreButton, false));
    }

    protected override void OnPreviewMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseRightButtonUp(e);
        if (Tile is { } t) { MenuRequested?.Invoke(this, (t, TileButton, true)); e.Handled = true; }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Enter && e.OriginalSource == TileButton && Tile is { ExplicitActions: true } rt)
        {
            RunRequested?.Invoke(this, rt);
            e.Handled = true;
        }
        else if ((e.Key == Key.Apps || (e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift)) && Tile is { } t)
        {
            MenuRequested?.Invoke(this, (t, TileButton, false));
            e.Handled = true;
        }
        // Space is the Quick Look key: on the focused, selected library tile it previews instead of pressing the button (which only selects).
        else if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None && e.OriginalSource == TileButton && Tile is { ExplicitActions: true, IsSelected: true } pt)
        {
            PreviewRequested?.Invoke(this, pt);
            e.Handled = true;
        }
        else if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control && ShowMoreButton && Tile is { } et)
        {
            EditRequested?.Invoke(this, et);
            e.Handled = true;
        }
    }

    private void OnHighContrastChanged()
    {
        if (Tile is { } t) t.Update(t.Shortcut);
        ApplyAppearance();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged o) o.PropertyChanged -= OnVmChanged;
        if (e.NewValue is INotifyPropertyChanged n) n.PropertyChanged += OnVmChanged;
        UpdateDropMarker();
        UpdateProgressLine();
        ApplyAppearance();
        ApplyBadge(Tile?.State ?? TileRunState.Idle, animate: false);
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShortcutTileViewModel.IsDropTarget)) UpdateDropMarker();
        else if (e.PropertyName == nameof(ShortcutTileViewModel.State) && Tile is { } st) ApplyBadge(st.State, animate: true);
        else if (e.PropertyName == nameof(ShortcutTileViewModel.ProgressText)) UpdateProgressLine();
        else if (e.PropertyName is nameof(ShortcutTileViewModel.IsSelected) or nameof(ShortcutTileViewModel.CategoryKey) or nameof(ShortcutTileViewModel.Name)) ApplyAppearance();
    }

    private void UpdateProgressLine() =>
        ProgressLine.Visibility = string.IsNullOrEmpty(Tile?.ProgressText) ? Visibility.Collapsed : Visibility.Visible;

    private void UpdateDropMarker() =>
        DropMarker.Visibility = Tile?.IsDropTarget == true ? Visibility.Visible : Visibility.Collapsed;
}
