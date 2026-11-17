using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

/// <summary>
/// Follows a running shortcut from the observer callbacks and says where it is: "Step 3 of 7: Resize pictures", and when a step or a
/// loop reports how many items it has done, "12 of 40". Only real numbers are used; nothing is estimated. Reads the run, never changes it.
/// </summary>
public sealed class RunProgressTracker : IRunObserver
{
    private readonly List<ActionInstance> _actions;
    private readonly Func<string, string?> _titleOf;
    private readonly object _gate = new();
    private string? _stepUid;
    private (int Done, int? Total)? _stepCount;
    private (int Done, int? Total, string GroupId)? _loopCount;

    public RunProgressTracker(IReadOnlyList<ActionInstance> actions, Func<string, string?> titleOf)
    {
        _actions = actions.ToList();
        _titleOf = titleOf;
    }

    public void OnStepStarted(string uid)
    {
        lock (_gate)
        {
            _stepUid = uid;
            _stepCount = null;
            if (_loopCount is { } loop && !IsInsideGroup(uid, loop.GroupId)) _loopCount = null;
        }
    }

    public void OnCount(string uid, int done, int? total)
    {
        lock (_gate)
        {
            var action = _actions.FirstOrDefault(a => a.Uid == uid);
            if (action?.Flow is { Role: FlowRole.Start } flow) _loopCount = (done, total, flow.GroupId);
            else if (uid == _stepUid) _stepCount = (done, total);
        }
    }

    public void OnStepFinished(string uid, ContentValue output, TimeSpan duration, string? error) { }
    public void OnVariableSet(string name, ContentValue value) { }
    public void OnLog(string message) { }

    private bool IsInsideGroup(string uid, string groupId)
    {
        var at = _actions.FindIndex(a => a.Uid == uid);
        if (at < 0) return false;
        var start = _actions.FindIndex(a => a.Flow is { Role: FlowRole.Start } f && f.GroupId == groupId);
        var end = _actions.FindIndex(a => a.Flow is { Role: FlowRole.End } f && f.GroupId == groupId);
        return start >= 0 && end >= 0 && at >= start && at <= end;
    }

    private bool CountsAsStep(ActionInstance a) => !a.Disabled && a.Flow is not { Role: FlowRole.Middle or FlowRole.End };

    /// <summary>The lines to show under the shortcut's name, or null before the first step has started.</summary>
    public string? Text()
    {
        lock (_gate)
        {
            if (_stepUid is null || _actions.FirstOrDefault(a => a.Uid == _stepUid) is not { } current) return null;
            var title = _titleOf(current.ActionId);
            if (string.IsNullOrWhiteSpace(title)) return null;
            var steps = _actions.Where(CountsAsStep).ToList();
            var number = steps.IndexOf(current) + 1;
            return RunProgressText.Format(number, steps.Count, title, _stepCount ?? (_loopCount is { } l ? (l.Done, l.Total) : null));
        }
    }
}

public static class RunProgressText
{
    /// <summary>The step line, and under it the count when there is one. The step number is left out when there is only one step.</summary>
    public static string Format(int number, int steps, string title, (int Done, int? Total)? count)
    {
        var line = steps > 1 && number > 0 ? Strings.Format("RunProgress_Step", number, steps, title) : title;
        if (count is not { } c) return line;
        var counted = c.Total is { } total ? Strings.Format("RunProgress_Count", c.Done, total) : Strings.Format("RunProgress_CountOnly", c.Done);
        return line + "\n" + counted;
    }
}
