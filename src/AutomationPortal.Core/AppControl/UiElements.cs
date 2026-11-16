namespace AutomationPortal.Core.AppControl;

/// <summary>The ways a control can be worked through UI Automation, without moving the mouse or pressing keys.</summary>
public enum UiPattern { Invoke, Value, Toggle, SelectionItem, ExpandCollapse }

/// <summary>
/// One control or window as UI Automation sees it. The real one wraps the Windows UI Automation client; tests use a fake tree.
/// Every member may be slow (it asks the other app), so call them off the UI thread.
/// </summary>
public interface IUiElement
{
    string AutomationId { get; }
    string Name { get; }
    /// <summary>The programmatic control type: "Button", "Edit", "ComboBox" …</summary>
    string ControlType { get; }
    string ClassName { get; }
    int ProcessId { get; }
    bool IsPassword { get; }
    bool IsEnabled { get; }
    /// <summary>Null for a top-level window (its parent is the desktop).</summary>
    IUiElement? Parent { get; }
    IReadOnlyList<IUiElement> Children { get; }
    bool SameAs(IUiElement other);
    /// <summary>True when the pattern is available (for <see cref="UiPattern.Value"/>: available and not read-only).</summary>
    bool Supports(UiPattern pattern);
    void Invoke();
    void SetValue(string text);
    void Toggle();
    void Select();
    /// <summary>Expands when collapsed, collapses when expanded.</summary>
    void ExpandOrCollapse();
    /// <summary>True for a dialog's default button (the one Enter presses): a button with id "1" (IDOK) or named "OK". Elements that can't tell say false.</summary>
    bool IsDefaultButton => ControlType == "Button" && (AutomationId == "1" || Name == "OK");
    /// <summary>Moves keyboard focus to the element (for a window: brings it forward).</summary>
    void Focus();
}

/// <summary>The desktop as UI Automation sees it.</summary>
public interface IUiDesktop
{
    IReadOnlyList<IUiElement> TopWindows();
    IUiElement? ElementAt(int x, int y);
    IUiElement? Focused();
    /// <summary>"notepad" for notepad.exe; "" when unknown.</summary>
    string ProcessName(int processId);
}

/// <summary>The steps that work other apps through UI Automation.</summary>
public static class AppControlIds
{
    public const string Click = "uia.click";
    public const string Type = "uia.type";
    public const string WaitWindow = "uia.waitWindow";
    public const string PressEnter = "uia.pressEnter";

    public static bool IsAppControl(string actionId) => actionId is Click or Type or WaitWindow or PressEnter;
}

/// <summary>Plain names for UI Automation control types.</summary>
public static class UiControlTypes
{
    private static readonly string[] Names =
    {
        "Button", "Calendar", "CheckBox", "ComboBox", "Edit", "Hyperlink", "Image", "ListItem", "List", "Menu", "MenuBar", "MenuItem",
        "ProgressBar", "RadioButton", "ScrollBar", "Slider", "Spinner", "StatusBar", "Tab", "TabItem", "Text", "ToolBar", "ToolTip", "Tree",
        "TreeItem", "Custom", "Group", "Thumb", "DataGrid", "DataItem", "Document", "SplitButton", "Window", "Pane", "Header", "HeaderItem",
        "Table", "TitleBar", "Separator", "SemanticZoom", "AppBar",
    };

    /// <summary>"Button" for 50000 (UIA_ButtonControlTypeId) and so on; "Custom" for anything unknown.</summary>
    public static string FromId(int id) => id >= 50000 && id - 50000 < Names.Length ? Names[id - 50000] : "Custom";

    /// <summary>What a person calls it: "button", "text box", "drop-down list" …</summary>
    public static string Friendly(string controlType) => controlType switch
    {
        "Button" or "SplitButton" => "button",
        "Edit" => "text box",
        "Document" => "document",
        "ComboBox" => "drop-down list",
        "CheckBox" => "check box",
        "RadioButton" => "option",
        "ListItem" or "DataItem" => "list item",
        "MenuItem" => "menu item",
        "TabItem" => "tab",
        "Hyperlink" => "link",
        "TreeItem" => "tree item",
        "Window" => "window",
        "" => "control",
        _ => controlType.ToLowerInvariant(),
    };
}
