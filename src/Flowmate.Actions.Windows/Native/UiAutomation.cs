using System.Runtime.InteropServices;
using System.Text;

namespace Flowmate.Interop;

// Minimal COM client for UIAutomationCore (the same service System.Windows.Automation wraps, without WPF's assemblies).
// Compiled into both Flowmate.Actions.Windows and Flowmate.Triggers. Slots named ReservedN keep the vtable order and must never be called.
internal static class Uia
{
    public const int TreeScopeDescendants = 4;
    public const int ControlTypeProperty = 30003, NameProperty = 30005, AutomationIdProperty = 30011, ClassNameProperty = 30012,
        IsPasswordProperty = 30019, IsValuePatternAvailableProperty = 30043, ValueValueProperty = 30045;
    public const int EditControlType = 50004;
    public const int TextPatternId = 10014;

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
    void Reserved0(); // CompareElements
    void Reserved1(); // CompareRuntimeIds
    IUIAutomationElement GetRootElement();
    IUIAutomationElement ElementFromHandle(IntPtr hwnd);
    void Reserved4(); // ElementFromPoint
    IUIAutomationElement? GetFocusedElement();
    void Reserved6(); // GetRootElementBuildCache
    void Reserved7(); // ElementFromHandleBuildCache
    void Reserved8(); // ElementFromPointBuildCache
    void Reserved9(); // GetFocusedElementBuildCache
    void Reserved10(); // CreateTreeWalker
    void Reserved11(); // get_ControlViewWalker
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
    void Reserved0(); // SetFocus
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
