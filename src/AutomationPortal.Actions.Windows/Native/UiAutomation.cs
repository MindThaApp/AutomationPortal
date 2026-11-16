using System.Runtime.InteropServices;
using System.Text;

namespace AutomationPortal.Interop;

// Minimal COM client for UIAutomationCore (the same service System.Windows.Automation wraps, without WPF's assemblies).
// Compiled into both AutomationPortal.Actions.Windows and AutomationPortal.Triggers. Slots named ReservedN keep the vtable order and must never be called.
internal static class Uia
{
    public const int TreeScopeDescendants = 4;
    public const int ControlTypeProperty = 30003, NameProperty = 30005, AutomationIdProperty = 30011, ClassNameProperty = 30012,
        IsPasswordProperty = 30019, IsValuePatternAvailableProperty = 30043, ValueValueProperty = 30045;
    public const int ProcessIdProperty = 30002, IsEnabledProperty = 30010;
    public const int EditControlType = 50004;
    public const int TextPatternId = 10014;
    public const int InvokePatternId = 10000, ValuePatternId = 10002, ExpandCollapsePatternId = 10005, SelectionItemPatternId = 10010, TogglePatternId = 10015;

    private static readonly Guid CUIAutomationClsid = new("ff48dba4-60ef-4201-aa87-54103eef594e");

    /// <summary>A fresh client per operation, so it always lives in the calling thread's apartment.</summary>
    public static IUIAutomation Create() =>
        (IUIAutomation)Activator.CreateInstance(Type.GetTypeFromCLSID(CUIAutomationClsid, throwOnError: true)!)!;

    public static string? GetString(IUIAutomationElement e, int property) => e.GetCurrentPropertyValue(property) as string;

    public static bool GetBool(IUIAutomationElement e, int property) => e.GetCurrentPropertyValue(property) is true;

    /// <summary>Concatenated text of every selected range of the element's text pattern; null when it has no text pattern.</summary>
    public static string? SelectedText(IUIAutomationElement e)
    {
        if (e.GetCurrentPattern(TextPatternId) is not IUIAutomationTextPattern pattern) return null;
        var ranges = pattern.GetSelection();
        if (ranges is null) return null;
        var sb = new StringBuilder();
        for (var i = 0; i < ranges.Length; i++) sb.Append(ranges.GetElement(i).GetText(-1));
        return sb.ToString();
    }

    /// <summary>True when the focused control is a password box. Any UIA failure counts as "not a password box".</summary>
    public static bool FocusedIsPassword()
    {
        try { return Create().GetFocusedElement() is { } e && GetBool(e, IsPasswordProperty); }
        catch (Exception ex) when (ex is COMException or InvalidCastException or TimeoutException) { return false; }
    }
}

[ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomation
{
    int CompareElements(IUIAutomationElement el1, IUIAutomationElement el2);
    void Reserved1(); // CompareRuntimeIds
    IUIAutomationElement GetRootElement();
    IUIAutomationElement ElementFromHandle(IntPtr hwnd);
    IUIAutomationElement? ElementFromPoint(UiaPoint pt);
    IUIAutomationElement? GetFocusedElement();
    void Reserved6(); // GetRootElementBuildCache
    void Reserved7(); // ElementFromHandleBuildCache
    void Reserved8(); // ElementFromPointBuildCache
    void Reserved9(); // GetFocusedElementBuildCache
    void Reserved10(); // CreateTreeWalker
    IUIAutomationTreeWalker GetControlViewWalker();
    void Reserved12(); // get_ContentViewWalker
    void Reserved13(); // get_RawViewWalker
    void Reserved14(); // get_RawViewCondition
    void Reserved15(); // get_ControlViewCondition
    void Reserved16(); // get_ContentViewCondition
    void Reserved17(); // CreateCacheRequest
    IUIAutomationCondition CreateTrueCondition();
    void Reserved19(); // CreateFalseCondition
    IUIAutomationCondition CreatePropertyCondition(int propertyId, [In, MarshalAs(UnmanagedType.Struct)] object value);
    void Reserved21(); // CreatePropertyConditionEx
    IUIAutomationCondition CreateAndCondition(IUIAutomationCondition condition1, IUIAutomationCondition condition2);
}

[ComImport, Guid("352ffba8-0973-437c-a61f-f64cafd81df9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationCondition { }

[ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationElement
{
    void SetFocus();
    void Reserved1(); // GetRuntimeId
    IUIAutomationElement? FindFirst(int scope, IUIAutomationCondition condition);
    IUIAutomationElementArray? FindAll(int scope, IUIAutomationCondition condition);
    void Reserved4(); // FindFirstBuildCache
    void Reserved5(); // FindAllBuildCache
    void Reserved6(); // BuildUpdatedCache
    [return: MarshalAs(UnmanagedType.Struct)] object? GetCurrentPropertyValue(int propertyId);
    void Reserved8(); // GetCurrentPropertyValueEx
    void Reserved9(); // GetCachedPropertyValue
    void Reserved10(); // GetCachedPropertyValueEx
    void Reserved11(); // GetCurrentPatternAs
    void Reserved12(); // GetCachedPatternAs
    [return: MarshalAs(UnmanagedType.IUnknown)] object? GetCurrentPattern(int patternId);
}

[ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationElementArray
{
    int Length { get; }
    IUIAutomationElement GetElement(int index);
}

[ComImport, Guid("32eba289-3583-42c9-9c59-3b6d9a1e9b6a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationTextPattern
{
    void Reserved0(); // RangeFromPoint
    void Reserved1(); // RangeFromChild
    IUIAutomationTextRangeArray? GetSelection();
}

[ComImport, Guid("ce4ae76a-e717-4c98-81ea-47371d028eb6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationTextRangeArray
{
    int Length { get; }
    IUIAutomationTextRange GetElement(int index);
}

[ComImport, Guid("a543cc6a-f4ae-494b-8239-c814481187a8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationTextRange
{
    void Reserved0(); // Clone
    void Reserved1(); // Compare
    void Reserved2(); // CompareEndpoints
    void Reserved3(); // ExpandToEnclosingUnit
    void Reserved4(); // FindAttribute
    void Reserved5(); // FindText
    void Reserved6(); // GetAttributeValue
    void Reserved7(); // GetBoundingRectangles
    void Reserved8(); // GetEnclosingElement
    [return: MarshalAs(UnmanagedType.BStr)] string GetText(int maxLength);
}

[StructLayout(LayoutKind.Sequential)]
internal struct UiaPoint
{
    public int X, Y;
    public UiaPoint(int x, int y) { X = x; Y = y; }
}

[ComImport, Guid("4042c624-389c-4afc-a630-9df854a541fc"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationTreeWalker
{
    IUIAutomationElement? GetParentElement(IUIAutomationElement element);
    IUIAutomationElement? GetFirstChildElement(IUIAutomationElement element);
    void Reserved2(); // GetLastChildElement
    IUIAutomationElement? GetNextSiblingElement(IUIAutomationElement element);
}

[ComImport, Guid("fb377fbe-8ea6-46d5-9c73-6499642d3059"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationInvokePattern
{
    void Invoke();
}

[ComImport, Guid("a94cd8b1-0844-4cd6-9d2d-640537ab39e9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationValuePattern
{
    void SetValue([MarshalAs(UnmanagedType.BStr)] string value);
    void Reserved1(); // get_CurrentValue: never read, so typed text in other apps is not collected
    int GetCurrentIsReadOnly();
}

[ComImport, Guid("94cf8058-9b8d-4ab9-8bfd-4cd0a33c8c70"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationTogglePattern
{
    void Toggle();
}

[ComImport, Guid("a8efa66a-0fda-421a-9194-38021f3578ea"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationSelectionItemPattern
{
    void Select();
}

[ComImport, Guid("619be086-1f4e-4ee4-bafa-210128738730"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationExpandCollapsePattern
{
    void Expand();
    void Collapse();
    int GetCurrentExpandCollapseState();
}
