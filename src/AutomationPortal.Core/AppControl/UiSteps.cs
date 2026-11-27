namespace AutomationPortal.Core.AppControl;

/// <summary>
/// Creates and reads the steps that work other apps (<see cref="AppControlIds"/>), and says them in plain words:
/// "Click 'Save' in Notepad", "Type 'hello' into 'Search'".
/// Parameters: <c>app</c> (the program, by process name), <c>window</c> (title pattern), <c>control</c> (<see cref="UiPathText"/>),
/// <c>label</c> (the control's name for people), <c>timeout</c> (seconds to wait for it), and for typing <c>text</c> (or Ask each
/// time) and <c>secret</c> (a password box: what is typed is never stored and the question hides the answer).
/// </summary>
public static class UiSteps
{
    public const double DefaultTimeoutSeconds = 10;
    public const double MinTimeoutSeconds = 1;
    public const double MaxTimeoutSeconds = 300;
    private const int QuoteMax = 40;

    public static ActionInstance Click(UiSelector s, string appName, string label) =>
        Base(AppControlIds.Click, s, appName, label);

    public static ActionInstance Type(UiSelector s, string appName, string label, string text) =>
        Base(AppControlIds.Type, s, appName, label).WithParam("text", ParamValue.Text(text)).WithParam("secret", ParamValue.Bool(false));

    /// <summary>A password box: nothing typed is kept; each run asks for it with the answer hidden.</summary>
    public static ActionInstance TypeSecret(UiSelector s, string appName, string label) =>
        Base(AppControlIds.Type, s, appName, label).WithParam("text", ParamValue.Ask()).WithParam("secret", ParamValue.Bool(true));

    /// <summary>Enter pressed in a text box with no default button to stand in for it: kept in the list, but it can't replay yet.</summary>
    public static ActionInstance PressEnter(UiSelector s, string appName, string label) =>
        Base(AppControlIds.PressEnter, s, appName, label);

    public static ActionInstance WaitWindow(string process, string windowTitle, string appName) =>
        ActionInstance.Create(AppControlIds.WaitWindow,
            ("app", ParamValue.App(process, appName)), ("window", ParamValue.Text(windowTitle)),
            ("timeout", ParamValue.Duration(DefaultTimeoutSeconds)));

    private static ActionInstance Base(string id, UiSelector s, string appName, string label) =>
        ActionInstance.Create(id,
            ("app", ParamValue.App(s.Process, appName)), ("window", ParamValue.Text(s.WindowTitle)),
            ("control", ParamValue.Text(s.PathText)), ("label", ParamValue.Text(label)),
            ("timeout", ParamValue.Duration(DefaultTimeoutSeconds)));

    /// <summary>The name people know a control by: its name, else its id, else "the button".</summary>
    public static string LabelOf(UiNode? node) =>
        node is null ? Strings.Get("UiSteps_Label_Window")
        : node.Name.Trim().Length > 0 ? node.Name.Trim()
        : node.AutomationId.Trim().Length > 0 ? node.AutomationId.Trim()
        : Strings.Format("UiSteps_Label_The", UiControlTypes.Friendly(node.ControlType));

    // ---- reading a step -------------------------------------------------------------------------

    public static string AppNameOf(ActionInstance a) =>
        a.Parameters.TryGetValue("app", out var v) && v is AppValue app
            ? (string.IsNullOrWhiteSpace(app.Name) ? app.Id : app.Name!)
            : Strings.Get("UiSteps_Label_App");

    public static string TextParam(ActionInstance a, string key) =>
        a.Parameters.TryGetValue(key, out var v) && v is TextValue t ? t.ToLiteralText() : "";

    public static string LabelOf(ActionInstance a)
    {
        var label = TextParam(a, "label").Trim();
        if (label.Length > 0) return label;
        return LabelOf(UiPathText.Parse(TextParam(a, "control")) is { Count: > 0 } path ? path[^1] : null);
    }

    public static bool IsSecret(ActionInstance a) => a.Parameters.TryGetValue("secret", out var v) && v is BoolValue { Value: true };

    public static bool AsksEachTime(ActionInstance a) => a.Parameters.TryGetValue("text", out var v) && v is AskValue;

    public static double TimeoutOf(ActionInstance a) =>
        a.Parameters.TryGetValue("timeout", out var v) && v is DurationValue d ? d.Seconds
        : a.Parameters.TryGetValue("timeout", out var n) && n is NumberValue num ? num.Value
        : DefaultTimeoutSeconds;

    /// <summary>The control's kind in plain words ("button", "text box"), or "" when unknown.</summary>
    public static string KindOf(ActionInstance a) =>
        UiPathText.Parse(TextParam(a, "control")) is { Count: > 0 } path ? UiControlTypes.Friendly(path[^1].ControlType) : "";

    /// <summary>True when the control is found only by its place in the window (it has no id or name).</summary>
    public static bool IsWeak(ActionInstance a) =>
        UiPathText.Parse(TextParam(a, "control")) is { Count: > 0 } path && path[^1] is { AutomationId.Length: 0, Name.Length: 0 };

    // ---- plain words ----------------------------------------------------------------------------

    /// <summary>The step as a sentence: "Click 'Save' in Notepad", "Type 'hello' into 'Search'".</summary>
    public static string Describe(ActionInstance a)
    {
        var app = AppNameOf(a);
        switch (a.ActionId)
        {
            case AppControlIds.Click:
                return Strings.Format("UiSteps_Step_Click", Quote(LabelOf(a)), app);
            case AppControlIds.Type:
                var into = Quote(LabelOf(a));
                if (IsSecret(a)) return Strings.Format("UiSteps_Step_TypePassword", into);
                if (AsksEachTime(a)) return Strings.Format("UiSteps_Step_TypeAsk", into);
                return Strings.Format("UiSteps_Step_Type", Quote(TextParam(a, "text")), into);
            case AppControlIds.PressEnter:
                return Strings.Format("UiSteps_Step_PressEnter", Quote(LabelOf(a)));
            case AppControlIds.WaitWindow:
                var title = TextParam(a, "window").Trim();
                return title.Length == 0 || title == "*" ? Strings.Format("UiSteps_Step_WaitAny", app) : Strings.Format("UiSteps_Step_WaitTitled", app, Quote(title));
            default:
                return a.ActionId;
        }
    }

    /// <summary>The failure shown in Run History when a recorded Enter that can't be replayed is reached.</summary>
    public static string PressEnterFailure => Strings.Get("UiSteps_PressEnterFailure");

    public static string PressEnterExplanation => Strings.Get("UiSteps_PressEnterExplanation");

    /// <summary>'text' in single quotes, shortened to 40 characters; a name like "the button" stays unquoted.</summary>
    public static string Quote(string text)
    {
        var t = (text ?? "").Replace('\r', ' ').Replace('\n', ' ');
        if (t.StartsWith("the ", StringComparison.Ordinal) && !t.Contains('\'')) return t;
        if (t.Length > QuoteMax) t = t[..(QuoteMax - 1)] + "…";
        return "'" + t + "'";
    }
}
