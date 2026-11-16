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
            catch (Exception ex) when (ex is not OperationCanceledException) { result = new UiFindResult(null, "Windows couldn't read the app's controls."); }
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
        var waited = seconds == 1 ? "1 second" : $"{seconds:0} seconds";
        var what = selector.Path.Count == 0
            ? (string.IsNullOrWhiteSpace(selector.WindowTitle) ? $"a {app} window" : $"the {app} window {UiSteps.Quote(selector.WindowTitle)}")
            : $"{UiSteps.Quote(label)} in {app}";
        var why = string.IsNullOrWhiteSpace(problem) ? "" : " " + problem.Trim();
        return $"Couldn't find {what} within {waited}.{why} Automation Portal stopped instead of guessing. Check the app is open and showing it, or record this step again.";
    }

    /// <summary>Clicks through the first pattern the control has: Invoke, Toggle, SelectionItem (select), ExpandCollapse. Returns what it did.</summary>
    public static string Click(IUiElement e, string label)
    {
        if (!e.IsEnabled) throw new ActionException($"{UiSteps.Quote(label)} is turned off (greyed out) right now, so it wasn't clicked.");
        if (e.Supports(UiPattern.Invoke)) { e.Invoke(); return "pressed"; }
        if (e.Supports(UiPattern.Toggle)) { e.Toggle(); return "switched"; }
        if (e.Supports(UiPattern.SelectionItem)) { e.Select(); return "selected"; }
        if (e.Supports(UiPattern.ExpandCollapse)) { e.ExpandOrCollapse(); return "opened or closed"; }
        throw new ActionException($"{UiSteps.Quote(label)} can't be clicked without moving the mouse, so Automation Portal didn't click it.");
    }

    /// <summary>Sets the control's text through the Value pattern (it replaces what was there).</summary>
    public static void Type(IUiElement e, string text, string label)
    {
        if (!e.IsEnabled) throw new ActionException($"{UiSteps.Quote(label)} is turned off (greyed out) right now, so nothing was typed into it.");
        if (!e.Supports(UiPattern.Value)) throw new ActionException($"{UiSteps.Quote(label)} doesn't take text without the keyboard, so nothing was typed into it.");
        e.SetValue(text);
    }

    /// <summary>Brings the control's window forward through UI Automation. Failure is not an error: the patterns work on background windows too.</summary>
    public static void FocusWindow(IUiElement e)
    {
        try { UiSelectorBuilder.WindowOf(e).Focus(); } catch { }
    }
}
