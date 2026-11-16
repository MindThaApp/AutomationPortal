using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using AutomationPortal.Core;
using AutomationPortal.Editor.ViewModels;

namespace AutomationPortal.Editor.UI;

/// <summary>Search, suggestions, favourites, recents and categories. Click adds an action, dragging inserts it where you drop.</summary>
internal sealed partial class ActionPanelView : UserControl
{
    public static readonly StyleSelector StyleSelectorInstance = new PanelStyleSelector();
    public const string DragFormat = "autoportal.action";

    private Point _dragStart;

    public ActionPanelView()
    {
        EditorResources.Ensure();
        InitializeComponent();
        PanelList.PreviewMouseLeftButtonDown += OnDown;
        PanelList.PreviewMouseMove += OnMove;
        PanelList.PreviewMouseLeftButtonUp += OnUp;
        PanelList.KeyDown += OnKeyDown;
        PanelList.PreviewMouseRightButtonDown += (_, e) => { if (ItemAt((DependencyObject)e.OriginalSource) is PanelAction a) PanelList.SelectedItem = a; };
        PanelList.ContextMenuOpening += OnContextMenuOpening;
    }

    private ActionPanelViewModel? Vm => DataContext as ActionPanelViewModel;

    public void FocusSearch() { SearchBox.Focus(); SearchBox.SelectAll(); }

    private static T? Find<T>(DependencyObject? d) where T : class
    {
        while (d is not null)
        {
            if (d is T t) return t;
            d = d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    private object? ItemAt(DependencyObject source) => Find<ListBoxItem>(source)?.DataContext;

    private object? _pressed;
    private bool _pressedStar;

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(PanelList);
        var source = (DependencyObject)e.OriginalSource;
        _pressed = ItemAt(source);
        _pressedStar = Find<ToggleButton>(source) is { Tag: "star" };
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressed is not PanelAction item || _pressedStar) return;
        var delta = e.GetPosition(PanelList) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _pressed = null;
        DragDrop.DoDragDrop(PanelList, new DataObject(DragFormat, item.Descriptor.Id), DragDropEffects.Copy);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        var pressed = _pressed;
        _pressed = null;
        if (Vm is null || pressed is null) return;
        var released = ItemAt((DependencyObject)e.OriginalSource);
        if (!ReferenceEquals(released, pressed)) return;
        switch (pressed)
        {
            case PanelCategory c: Vm.ToggleCategory(c); break;
            case PanelAction a:
                if (_pressedStar) { Vm.ToggleFavourite(a); e.Handled = true; }
                else Vm.RequestAdd(a.Descriptor);
                break;
        }
    }
    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (Vm is null || PanelList.SelectedItem is not PanelAction a) { e.Handled = true; return; }
        var item = new MenuItem { Header = a.IsFavourite ? "Remove from favourites" : "Add to favourites", InputGestureText = "Ctrl+D" };
        item.Click += (_, _) => Vm.ToggleFavourite(a);
        PanelList.ContextMenu = new ContextMenu { Items = { item } };
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is null) return;
        if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control && PanelList.SelectedItem is PanelAction fav)
        {
            e.Handled = true;
            Vm.ToggleFavourite(fav);
            return;
        }
        if (e.Key is Key.Enter or Key.Space && PanelList.SelectedItem is { } sel)
        {
            e.Handled = true;
            switch (sel)
            {
                case PanelCategory c: Vm.ToggleCategory(c); break;
                case PanelAction a: Vm.RequestAdd(a.Descriptor); break;
            }
        }
    }
}
