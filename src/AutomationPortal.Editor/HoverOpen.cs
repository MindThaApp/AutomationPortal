using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace AutomationPortal.Editor;

/// <summary>
/// Lets the chevron of a split button open its menu when the pointer rests on it. The menu closes again if the pointer
/// leaves without ever entering the menu. Clicking and the keyboard keep working as they were.
/// </summary>
public sealed class HoverOpen
{
    /// <summary>How long the pointer must rest on the chevron before the menu opens.</summary>
    public const int DelayMs = 300;

    /// <summary>How long after the pointer leaves before the menu is closed (lets the pointer cross onto the menu).</summary>
    public const int CloseGraceMs = 250;

    private readonly FrameworkElement _trigger;
    private readonly Func<ContextMenu?> _currentMenu;
    private readonly Action _open;
    private readonly DispatcherTimer _openTimer;
    private readonly DispatcherTimer _closeTimer;
    private bool _openedByHover;
    private bool _menuEntered;
    private ContextMenu? _watched;

    private HoverOpen(FrameworkElement trigger, Func<ContextMenu?> currentMenu, Action open)
    {
        _trigger = trigger; _currentMenu = currentMenu; _open = open;
        _openTimer = new DispatcherTimer(DispatcherPriority.Input, trigger.Dispatcher) { Interval = TimeSpan.FromMilliseconds(DelayMs) };
        _closeTimer = new DispatcherTimer(DispatcherPriority.Input, trigger.Dispatcher) { Interval = TimeSpan.FromMilliseconds(CloseGraceMs) };
        _openTimer.Tick += (_, _) => { _openTimer.Stop(); OpenNow(); };
        _closeTimer.Tick += (_, _) => { _closeTimer.Stop(); CloseIfAbandoned(); };
        trigger.MouseEnter += (_, _) =>
        {
            _closeTimer.Stop();
            if (_currentMenu() is { IsOpen: true } || !trigger.IsEnabled) return;
            _openTimer.Start();
        };
        trigger.MouseLeave += (_, _) =>
        {
            _openTimer.Stop();
            if (_openedByHover && !_menuEntered) _closeTimer.Start();
        };
        trigger.PreviewKeyDown += (_, e) =>
        {
            // Alt+Down is the standard key for opening a drop-down; Enter and Space already click the button.
            if (e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.Alt || e.SystemKey == Key.Down && Keyboard.Modifiers == ModifierKeys.Alt)
            {
                e.Handled = true;
                _openedByHover = false;
                _open();
            }
        };
    }

    /// <summary>Wires hover-to-open onto a split button's chevron. <paramref name="open"/> opens the menu; <paramref name="currentMenu"/> returns it while it exists.</summary>
    public static HoverOpen Attach(FrameworkElement chevron, Func<ContextMenu?> currentMenu, Action open) => new(chevron, currentMenu, open);

    private void OpenNow()
    {
        if (!_trigger.IsMouseOver || _currentMenu() is { IsOpen: true }) return;
        _openedByHover = true;
        _menuEntered = false;
        _open();
        if (_currentMenu() is { } menu && !ReferenceEquals(menu, _watched))
        {
            _watched = menu;
            menu.MouseEnter += (_, _) => { _menuEntered = true; _closeTimer.Stop(); };
            menu.Closed += (_, _) => { _openedByHover = false; _menuEntered = false; _closeTimer.Stop(); };
        }
    }

    private void CloseIfAbandoned()
    {
        if (_currentMenu() is { IsOpen: true } menu && ShouldClose(_openedByHover, _menuEntered, _trigger.IsMouseOver, menu.IsMouseOver))
            menu.IsOpen = false;
    }

    /// <summary>The close decision on its own, so it can be tested: close only a menu opened by hover that the pointer never entered and has left.</summary>
    public static bool ShouldClose(bool openedByHover, bool menuEntered, bool pointerOnTrigger, bool pointerOnMenu) =>
        openedByHover && !menuEntered && !pointerOnTrigger && !pointerOnMenu;
}
