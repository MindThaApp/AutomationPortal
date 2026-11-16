using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AutomationPortal.App.Logic;
using AutomationPortal.Core;
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
        Root.MouseEnter += (_, _) => ShowMore(true);
        Root.MouseLeave += (_, _) => { if (!MoreButton.IsKeyboardFocused) ShowMore(false); };
        MoreButton.LostKeyboardFocus += (_, _) => { if (!Root.IsMouseOver) ShowMore(false); };
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

    public Button Button => TileButton;

    private const double RestOpacity = 0.7;

    private void ShowMore(bool show)
    {
        AutomationPortal.Editor.Motion.Animate(MoreButton, OpacityProperty, null, show ? 1 : RestOpacity, show ? 100 : 160);
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

    private static Brush Solid(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    /// <summary>Neutral: a card with a coloured icon chip. Gradients: the full-colour tile, with dark or light text chosen for contrast.</summary>
    private void ApplyAppearance()
    {
        if (Tile is not { } t) return;
        var explicitActions = t.ExplicitActions;
        Actions.Visibility = explicitActions ? Visibility.Visible : Visibility.Collapsed;
        NameText.Margin = new Thickness(14, 0, 14, explicitActions ? 46 : 12);
        if (TileAppearance.Style == TileStyleSetting.Neutral && !AutomationPortal.Editor.HighContrast.Active)
        {
            TileButton.SetResourceReference(Control.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
            Chip.Background = t.Brush;
            GlyphIcon.Foreground = Solid(TileAppearance.TextFor(t.MidHex));
            NameText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            MoreIcon.SetResourceReference(Wpf.Ui.Controls.SymbolIcon.ForegroundProperty, "TextFillColorSecondaryBrush");
            PinIcon.SetResourceReference(Wpf.Ui.Controls.SymbolIcon.ForegroundProperty, "TextFillColorSecondaryBrush");
        }
        else
        {
            TileButton.Background = t.Brush;
            Chip.Background = Brushes.Transparent;
            GlyphIcon.Foreground = Solid(TileAppearance.IconFor(t.TopHex));
            var text = Solid(TileAppearance.TextFor(t.BottomHex));
            NameText.Foreground = text;
            MoreIcon.Foreground = Solid(TileAppearance.IconFor(t.TopHex));
            PinIcon.Foreground = Solid(TileAppearance.IconFor(t.TopHex));
        }
        SelectRing.Visibility = t.IsSelected ? Visibility.Visible : Visibility.Collapsed;
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
        ApplyAppearance();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShortcutTileViewModel.IsDropTarget)) UpdateDropMarker();
        else if (e.PropertyName is nameof(ShortcutTileViewModel.IsSelected) or nameof(ShortcutTileViewModel.Brush)) ApplyAppearance();
    }

    private void UpdateDropMarker() =>
        DropMarker.Visibility = Tile?.IsDropTarget == true ? Visibility.Visible : Visibility.Collapsed;
}
