using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using AutomationPortal.App.ViewModels;

namespace AutomationPortal.App.Controls;

public partial class ShortcutTile : UserControl
{
    public ShortcutTile()
    {
        InitializeComponent();
        Loaded += (_, _) => { if (!ShowMoreButton) MoreButton.Visibility = Visibility.Collapsed; };
        DataContextChanged += OnDataContextChanged;
        Root.MouseEnter += (_, _) => ShowMore(true);
        Root.MouseLeave += (_, _) => { if (!MoreButton.IsKeyboardFocused) ShowMore(false); };
        MoreButton.LostKeyboardFocus += (_, _) => { if (!Root.IsMouseOver) ShowMore(false); };
        TileButton.GotKeyboardFocus += (_, _) => ShowMore(true);
        TileButton.LostKeyboardFocus += (_, _) => { if (!Root.IsMouseOver && !MoreButton.IsKeyboardFocused) ShowMore(false); };
    }

    public ShortcutTileViewModel? Tile => DataContext as ShortcutTileViewModel;

    /// <summary>False in the Quick Panel, which has no per-tile menu.</summary>
    public bool ShowMoreButton { get; set; } = true;

    public event EventHandler<ShortcutTileViewModel>? RunRequested;
    /// <summary>Raised for the "..." button and for right-click / the Menu key. The element is the anchor for the menu.</summary>
    public event EventHandler<(ShortcutTileViewModel Tile, FrameworkElement Anchor, bool AtPointer)>? MenuRequested;

    public Button Button => TileButton;

    private void ShowMore(bool show)
    {
        var anim = new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(show ? 100 : 160));
        MoreButton.BeginAnimation(OpacityProperty, anim);
    }

    private void OnTileClick(object sender, RoutedEventArgs e)
    {
        if (Tile is { } t) RunRequested?.Invoke(this, t);
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
        if ((e.Key == Key.Apps || (e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift)) && Tile is { } t)
        {
            MenuRequested?.Invoke(this, (t, TileButton, false));
            e.Handled = true;
        }
        else if (e.Key == Key.F2 && Tile is not null) { /* rename is handled by the page via the menu */ }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged o) o.PropertyChanged -= OnVmChanged;
        if (e.NewValue is INotifyPropertyChanged n) n.PropertyChanged += OnVmChanged;
        UpdateDropMarker();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShortcutTileViewModel.IsDropTarget)) UpdateDropMarker();
    }

    private void UpdateDropMarker() =>
        DropMarker.Visibility = Tile?.IsDropTarget == true ? Visibility.Visible : Visibility.Collapsed;
}
