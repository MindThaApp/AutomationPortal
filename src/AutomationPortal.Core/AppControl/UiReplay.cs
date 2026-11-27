namespace AutomationPortal.Core.AppControl;

/// <summary>
/// Plays back the steps that work other apps, through UI Automation patterns only (Invoke, Value, Toggle, SelectionItem,
/// ExpandCollapse and focus). It never moves the mouse or sends keystrokes. A control that can't be found in time, or can't be
/// worked through a pattern, stops the step with a message that names it; there is no fallback to screen positions.
/// </summary>
public static class UiReplay
{
    public static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(250);

    /// <summary>Waits up to <paramref name="timeout"/> for the control (or, for an empty path, the window) to appear.</summary>
    public static async Task<IUiElement> WaitForAsync(IUiDesktop desktop, UiSelector selector, string appName, string label, TimeSpan timeout,
        CancellationToken ct, Func<TimeSpan, CancellationToken, Task>? delay = null, Func<DateTime>? now = null)
    {
        delay ??= Task.Delay;
        now ??= () => DateTime.UtcNow;
        var started = now();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            UiFindResult result;
            try { result = await Task.Run(() => UiSelectorMatcher.Find(desktop, selector, appName), ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException) { result = new UiFindResult(null, Strings.Get("UiReplay_Problem_CantRead")); }
            if (result.Element is { } found) return found;
            var left = timeout - (now() - started);
            if (left <= TimeSpan.Zero) throw new ActionException(NotFoundMessage(selector, appName, label, timeout, result.Problem));
            await delay(left < PollEvery ? left : PollEvery, ct).ConfigureAwait(false);
        }
    }

    public static string NotFoundMessage(UiSelector selector, string appName, string label, TimeSpan timeout, string problem)
    {
        var app = string.IsNullOrWhiteSpace(appName) ? selector.Process : appName;
        var seconds = Math.Max(0, Math.Round(timeout.TotalSeconds));
        var waited = seconds == 1 ? Strings.Get("UiReplay_Waited_One") : Strings.Format("UiReplay_Waited_Many", seconds);
        var what = selector.Path.Count == 0
            ? (string.IsNullOrWhiteSpace(selector.WindowTitle) ? Strings.Format("UiReplay_What_Window", app) : Strings.Format("UiReplay_What_TitledWindow", app, UiSteps.Quote(selector.WindowTitle)))
            : Strings.Format("UiReplay_What_Control", UiSteps.Quote(label), app);
        var why = string.IsNullOrWhiteSpace(problem) ? "" : " " + problem.Trim();
        return Strings.Format("UiReplay_Error_NotFound", what, waited, why);
    }

    /// <summary>Clicks through the first pattern the control has: Invoke, Toggle, SelectionItem (select), ExpandCollapse. Returns what it did.</summary>
    public static string Click(IUiElement e, string label)
    {
        if (!e.IsEnabled) throw new ActionException(Strings.Format("UiReplay_Error_ClickDisabled", UiSteps.Quote(label)));
        if (e.Supports(UiPattern.Invoke)) { e.Invoke(); return Strings.Get("UiReplay_Did_Pressed"); }
        if (e.Supports(UiPattern.Toggle)) { e.Toggle(); return Strings.Get("UiReplay_Did_Switched"); }
        if (e.Supports(UiPattern.SelectionItem)) { e.Select(); return Strings.Get("UiReplay_Did_Selected"); }
        if (e.Supports(UiPattern.ExpandCollapse)) { e.ExpandOrCollapse(); return Strings.Get("UiReplay_Did_Toggled"); }
        throw new ActionException(Strings.Format("UiReplay_Error_NeedsMouse", UiSteps.Quote(label)));
    }

    /// <summary>Sets the control's text through the Value pattern (it replaces what was there).</summary>
    public static void Type(IUiElement e, string text, string label)
    {
        if (!e.IsEnabled) throw new ActionException(Strings.Format("UiReplay_Error_TypeDisabled", UiSteps.Quote(label)));
        if (!e.Supports(UiPattern.Value)) throw new ActionException(Strings.Format("UiReplay_Error_NoValue", UiSteps.Quote(label)));
        e.SetValue(text);
    }

    /// <summary>Brings the control's window forward through UI Automation. Failure is not an error: the patterns work on background windows too.</summary>
    public static void FocusWindow(IUiElement e)
    {
        try { UiSelectorBuilder.WindowOf(e).Focus(); } catch { }
    }
}
