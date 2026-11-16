using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using AutomationPortal.Core.AppControl;

namespace AutomationPortal.Interop;

// The desktop and its controls through the UI Automation COM client, for recording and replaying steps in other apps.
// Compiled into both AutomationPortal.Actions.Windows and AutomationPortal.Triggers. It only reads the tree and calls UI Automation
// patterns (Invoke, Value, Toggle, SelectionItem, ExpandCollapse, SetFocus); it never moves the mouse or sends keystrokes.
// Use it from an MTA thread (a thread-pool thread or the recorder's worker), never from a UI thread.
internal sealed class UiaDesktop : IUiDesktop
{
    private const int MaxChildren = 2000;
    private readonly ConcurrentDictionary<int, string> _processNames = new();

    public UiaDesktop()
    {
        Automation = Uia.Create();
        Walker = Automation.GetControlViewWalker();
        Root = Automation.GetRootElement();
    }

    internal IUIAutomation Automation { get; }
    internal IUIAutomationTreeWalker Walker { get; }
    internal IUIAutomationElement Root { get; }

    public IReadOnlyList<IUiElement> TopWindows() => ChildrenOf(Root, top: true);

    public IUiElement? ElementAt(int x, int y)
    {
        try { return Automation.ElementFromPoint(new UiaPoint(x, y)) is { } e ? new UiaElement(this, e) : null; }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { return null; }
    }

    public IUiElement? Focused()
    {
        try { return Automation.GetFocusedElement() is { } e ? new UiaElement(this, e) : null; }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { return null; }
    }

    public string ProcessName(int processId) => processId <= 0 ? "" : _processNames.GetOrAdd(processId, id =>
    {
        try { using var p = System.Diagnostics.Process.GetProcessById(id); return p.ProcessName; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return ""; }
    });

    internal IReadOnlyList<IUiElement> ChildrenOf(IUIAutomationElement parent, bool top)
    {
        var list = new List<IUiElement>();
        try
        {
            var child = Walker.GetFirstChildElement(parent);
            while (child is not null && list.Count < MaxChildren)
            {
                list.Add(new UiaElement(this, child, top));
                child = Walker.GetNextSiblingElement(child);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { }
        return list;
    }

    internal bool IsRoot(IUIAutomationElement e)
    {
        try { return Automation.CompareElements(e, Root) != 0; }
        catch (COMException) { return false; }
    }
}

internal sealed class UiaElement : IUiElement
{
    private readonly UiaDesktop _desktop;
    private readonly bool _knownTop;
    private IUiElement? _parent;
    private bool _parentRead;

    public UiaElement(UiaDesktop desktop, IUIAutomationElement raw, bool knownTop = false)
    {
        _desktop = desktop;
        Raw = raw;
        _knownTop = knownTop;
    }

    internal IUIAutomationElement Raw { get; }

    public string AutomationId => Text(Uia.AutomationIdProperty);
    public string Name => Text(Uia.NameProperty);
    public string ClassName => Text(Uia.ClassNameProperty);
    public string ControlType => UiControlTypes.FromId(Int(Uia.ControlTypeProperty));
    public int ProcessId => Int(Uia.ProcessIdProperty);
    // When Windows can't say, the control counts as a password box, so what is typed into it is not recorded.
    public bool IsPassword => Bool(Uia.IsPasswordProperty, whenUnknown: true);
    public bool IsEnabled => Bool(Uia.IsEnabledProperty, whenUnknown: true);

    public IUiElement? Parent
    {
        get
        {
            if (_knownTop) return null;
            if (_parentRead) return _parent;
            _parentRead = true;
            try
            {
                var p = _desktop.Walker.GetParentElement(Raw);
                _parent = p is null || _desktop.IsRoot(p) ? null : new UiaElement(_desktop, p);
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException) { _parent = null; }
            return _parent;
        }
    }

    public IReadOnlyList<IUiElement> Children => _desktop.ChildrenOf(Raw, top: false);

    public bool SameAs(IUiElement other)
    {
        if (other is not UiaElement o) return false;
        try { return _desktop.Automation.CompareElements(Raw, o.Raw) != 0; }
        catch (COMException) { return false; }
    }

    public bool Supports(UiPattern pattern) => pattern switch
    {
        UiPattern.Invoke => Pattern<IUIAutomationInvokePattern>(Uia.InvokePatternId) is not null,
        UiPattern.Value => Pattern<IUIAutomationValuePattern>(Uia.ValuePatternId) is { } v && !ReadOnly(v),
        UiPattern.Toggle => Pattern<IUIAutomationTogglePattern>(Uia.TogglePatternId) is not null,
        UiPattern.SelectionItem => Pattern<IUIAutomationSelectionItemPattern>(Uia.SelectionItemPatternId) is not null,
        UiPattern.ExpandCollapse => Pattern<IUIAutomationExpandCollapsePattern>(Uia.ExpandCollapsePatternId) is { } ec && SafeState(ec) != 3, // 3 = leaf node
        _ => false,
    };

    public void Invoke() => Need<IUIAutomationInvokePattern>(Uia.InvokePatternId).Invoke();
    public void SetValue(string text) => Need<IUIAutomationValuePattern>(Uia.ValuePatternId).SetValue(text);
    public void Toggle() => Need<IUIAutomationTogglePattern>(Uia.TogglePatternId).Toggle();
    public void Select() => Need<IUIAutomationSelectionItemPattern>(Uia.SelectionItemPatternId).Select();

    public void ExpandOrCollapse()
    {
        var p = Need<IUIAutomationExpandCollapsePattern>(Uia.ExpandCollapsePatternId);
        if (p.GetCurrentExpandCollapseState() == 1) p.Collapse(); // 1 = expanded
        else p.Expand();
    }

    public void Focus() => Raw.SetFocus();

    private T? Pattern<T>(int id) where T : class
    {
        try { return Raw.GetCurrentPattern(id) as T; }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { return null; }
    }

    private T Need<T>(int id) where T : class =>
        Pattern<T>(id) ?? throw new InvalidOperationException("The control no longer offers that action.");

    private static bool ReadOnly(IUIAutomationValuePattern v)
    {
        try { return v.GetCurrentIsReadOnly() != 0; }
        catch (COMException) { return true; }
    }

    private static int SafeState(IUIAutomationExpandCollapsePattern p)
    {
        try { return p.GetCurrentExpandCollapseState(); }
        catch (COMException) { return 3; }
    }

    private object? Value(int property)
    {
        try { return Raw.GetCurrentPropertyValue(property); }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { return null; }
    }

    private string Text(int property) => Value(property) as string ?? "";
    private int Int(int property) => Value(property) is int i ? i : 0;
    private bool Bool(int property, bool whenUnknown) => Value(property) is bool b ? b : whenUnknown;
}
