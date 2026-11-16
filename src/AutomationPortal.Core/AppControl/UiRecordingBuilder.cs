using System.Text;

namespace AutomationPortal.Core.AppControl;

/// <summary>
/// Turns what the user did in one app (clicks on controls, typing into fields) into steps that repeat it through UI Automation.
/// Pure logic: the recorder resolves each click or key to the control involved and hands it here; tests use a fake tree.
/// <list type="bullet">
/// <item>A click becomes "Click a button in an app" on the control (or the nearest ancestor that can be clicked). A click on a
/// text box only moves focus and is left out: the typing that follows fills the box.</item>
/// <item>Typing into the same control becomes one "Type into a field" step; Backspace removes the last character.</item>
/// <item>Typing into a password box is never kept: the step asks for it each time instead.</item>
/// <item>A step in a different window first waits for that window.</item>
/// <item>Enter in a one-line text box presses the window's default button, or is kept as a "can't replay yet" step.</item>
/// <item>Other keys (Ctrl+S …) can't be repeated without sending keystrokes; they are listed in <see cref="LeftOut"/>.</item>
/// </list>
/// </summary>
public sealed class UiRecordingBuilder
{
    private readonly IUiDesktop _desktop;
    private readonly string _appName;
    private readonly List<ActionInstance> _steps = new();
    private readonly List<string> _leftOut = new();
    private (string Process, string Title)? _window;

    private UiSelector? _typingTarget;
    private string _typingLabel = "";
    private StringBuilder? _typing;
    private bool _typingSecret;

    public UiRecordingBuilder(IUiDesktop desktop, string appName)
    {
        _desktop = desktop;
        _appName = appName;
    }

    /// <summary>Raised when a step is finished (typing is finished when something else happens).</summary>
    public event Action<ActionInstance>? StepRecorded;

    /// <summary>Things the user did that the recording can't repeat, in plain words ("Pressing Enter").</summary>
    public IReadOnlyList<string> LeftOut => _leftOut.ToList();

    /// <summary>Everything recorded so far, including typing still in progress.</summary>
    public IReadOnlyList<ActionInstance> Steps
    {
        get
        {
            var list = _steps.ToList();
            if (BuildTyping() is { } pending) list.Add(pending);
            return list;
        }
    }

    public IReadOnlyList<ActionInstance> Finish()
    {
        Flush();
        return _steps.ToList();
    }

    /// <summary>Recording was paused: finish the typing in progress.</summary>
    public void Pause() => Flush();

    /// <summary>A click. <paramref name="element"/> is the control under the pointer, or null when Windows couldn't say.</summary>
    public void Click(IUiElement? element)
    {
        Flush();
        if (element is null) { LeaveOut("A click on something Windows couldn't name"); return; }
        var target = UiSelectorBuilder.ClickTarget(element);
        if (!UiSelectorBuilder.CanClick(target))
        {
            // Clicking into a text box only puts the cursor there; the typing that follows is recorded on the box itself.
            if (target.Supports(UiPattern.Value) || target.IsPassword || target.ControlType is "Edit" or "Document") return;
            // A click on a window's empty space or its title bar does nothing worth repeating.
            if (target.Parent is null || target.ControlType is "TitleBar" or "Pane" or "Window" or "Text" or "Image" or "Group") return;
            LeaveOut($"A click on {UiSteps.Quote(UiSteps.LabelOf(UiNode.Of(target)))}, which can't be clicked without moving the mouse");
            return;
        }
        var selector = UiSelectorBuilder.Build(target, _desktop);
        EnterWindow(selector);
        Commit(UiSteps.Click(selector, _appName, UiSteps.LabelOf(selector.Target)));
    }

    /// <summary>
    /// A key press in the focused control. <paramref name="text"/> is the character it typed ("" for Enter, arrows, shortcuts);
    /// <paramref name="keyName"/> names the key for the list of things left out ("Enter", "Ctrl+S").
    /// </summary>
    public void Key(IUiElement? focused, string text, string keyName, bool isBackspace = false, bool isTab = false)
    {
        if (focused is null)
        {
            Flush();
            if (text.Length > 0 || isBackspace) LeaveOut("Typing where Windows couldn't say which field had focus");
            return;
        }
        var secret = focused.IsPassword;
        if (text.Length == 0 && !isBackspace)
        {
            Flush();
            if (keyName == "Enter" && focused.ControlType == "Edit") { EnterInTextBox(focused); return; }
            if (!isTab && keyName.Length > 0) LeaveOut($"Pressing {keyName}");
            return;
        }

        var selector = UiSelectorBuilder.Build(focused, _desktop);
        var same = _typing is not null && _typingTarget is not null && _typingTarget.SameAs(selector);
        if (!same)
        {
            Flush();
            if (isBackspace && !secret) return; // deleting text that was already there: the Type step replaces the whole value anyway
            EnterWindow(selector);
            _typingTarget = selector;
            _typingLabel = UiSteps.LabelOf(selector.Target);
            _typing = new StringBuilder();
            _typingSecret = secret;
        }
        _typingSecret |= secret;
        if (_typingSecret) { _typing!.Clear(); return; } // a password box: what is typed is never kept, not even for a moment
        if (isBackspace) { if (_typing!.Length > 0) _typing.Length--; return; }
        _typing!.Append(text);
    }

    /// <summary>
    /// Enter in a one-line text box: becomes a click on the window's default button when it has one, else a step that is kept
    /// but can't replay yet. Never a keystroke.
    /// </summary>
    private void EnterInTextBox(IUiElement focused)
    {
        var button = UiDefaultButton.Find(UiSelectorBuilder.WindowOf(focused));
        if (button is not null && UiSelectorBuilder.CanClick(button))
        {
            var selector = UiSelectorBuilder.Build(button, _desktop);
            EnterWindow(selector);
            Commit(UiSteps.Click(selector, _appName, UiSteps.LabelOf(selector.Target)));
            return;
        }
        var field = UiSelectorBuilder.Build(focused, _desktop);
        EnterWindow(field);
        Commit(UiSteps.PressEnter(field, _appName, UiSteps.LabelOf(field.Target)));
    }

    private void EnterWindow(UiSelector selector)
    {
        var key = (selector.Process.ToLowerInvariant(), selector.WindowTitle);
        if (_window == key) return;
        _window = key;
        Commit(UiSteps.WaitWindow(selector.Process, selector.WindowTitle, _appName));
    }

    private ActionInstance? BuildTyping()
    {
        if (_typing is null || _typingTarget is null) return null;
        if (_typingSecret) return UiSteps.TypeSecret(_typingTarget, _appName, _typingLabel);
        return UiSteps.Type(_typingTarget, _appName, _typingLabel, _typing.ToString());
    }

    private void Flush()
    {
        if (BuildTyping() is { } step) Commit(step);
        _typing = null;
        _typingTarget = null;
        _typingSecret = false;
    }

    private void LeaveOut(string what)
    {
        if (!_leftOut.Contains(what)) _leftOut.Add(what);
    }

    private void Commit(ActionInstance step)
    {
        _steps.Add(step);
        try { StepRecorded?.Invoke(step); } catch { }
    }
}
