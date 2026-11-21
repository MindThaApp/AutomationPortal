using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace AutomationPortal.Editor.UI;

/// <summary>
/// A themed popup that closes when you click away. Pages can be pushed inside it ("Choose a variable" with a back button)
/// so editors never need a popup inside a popup.
/// </summary>
internal sealed class FloatingPanel
{
    private readonly Popup _popup;
    private readonly Border _frame;
    private readonly ContentControl _host = new() { Focusable = false };
    private readonly Stack<(FrameworkElement Content, Action? OnBack)> _pages = new();

    public static List<FloatingPanel> OpenPanels { get; } = new();
    public FrameworkElement Frame => _frame;
    public event Action? Closed;

    private FloatingPanel(FrameworkElement anchor, FrameworkElement content, double width, PlacementMode placement)
    {
        EditorResources.Ensure();
        _frame = new Border
        {
            CornerRadius = Radius.Card, BorderThickness = new Thickness(1), Padding = new Thickness(12),
            Margin = new Thickness(12), MinWidth = 220, Child = _host,
            Effect = HighContrast.Shadow(18, 3, 0.28),
            Tag = this,
        };
        _frame.SetResourceReference(Border.BackgroundProperty, Ui.PopupFill);
        _frame.SetResourceReference(Border.BorderBrushProperty, Ui.ControlStroke);
        TextElement.SetFontFamily(_frame, Ui.Font);
        TextElement.SetFontSize(_frame, 14);
        _frame.SetResourceReference(TextElement.ForegroundProperty, Ui.Primary);
        if (width > 0) _frame.Width = width + 24;
        _host.Content = content;
        _popup = new Popup
        {
            PlacementTarget = anchor, Placement = placement, StaysOpen = false, AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade, Child = _frame, VerticalOffset = -8,
        };
        OpenPanels.Add(this);
        _popup.Closed += (_, _) => { OpenPanels.Remove(this); Closed?.Invoke(); };
        _frame.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; if (_pages.Count > 0) Back(); else Close(); }
        };
        _popup.IsOpen = true;
        _popup.Dispatcher.BeginInvoke(new Action(() => FocusFirst(content)), System.Windows.Threading.DispatcherPriority.Input);
    }

    public bool IsOpen => _popup.IsOpen;

    public static FloatingPanel Open(FrameworkElement anchor, FrameworkElement content, double width = 0, PlacementMode placement = PlacementMode.Bottom) =>
        new(anchor, content, width, placement);

    /// <summary>The panel that contains <paramref name="source"/>, or null when it lives in a normal window.</summary>
    public static FloatingPanel? Containing(DependencyObject? source)
    {
        var d = source;
        while (d is not null)
        {
            if (d is Border { Tag: FloatingPanel p }) return p;
            d = d is Visual ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    public void Navigate(FrameworkElement page, Action? onBack = null)
    {
        _pages.Push((_host.Content as FrameworkElement ?? new Grid(), onBack));
        _host.Content = page;
        _popup.Dispatcher.BeginInvoke(new Action(() => FocusFirst(page)), System.Windows.Threading.DispatcherPriority.Input);
    }

    public void Back()
    {
        if (_pages.Count == 0) return;
        var (content, onBack) = _pages.Pop();
        _host.Content = content;
        onBack?.Invoke();
        _popup.Dispatcher.BeginInvoke(new Action(() => FocusFirst(content)), System.Windows.Threading.DispatcherPriority.Input);
    }

    public void Close() => _popup.IsOpen = false;

    private static void FocusFirst(DependencyObject root)
    {
        foreach (var c in Ui.Descendants<Control>(root))
            if (c.Focusable && c.IsVisible && (c is TextBoxBase or ListBox)) { c.Focus(); Keyboard.Focus(c); return; }
        if (root is FrameworkElement fe && fe.Focusable) fe.Focus();
    }
}
