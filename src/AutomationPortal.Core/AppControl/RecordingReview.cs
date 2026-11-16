namespace AutomationPortal.Core.AppControl;

/// <summary>
/// The review after a recording stops: the steps in plain words, with delete, reorder, change the typed text, "ask each time",
/// and how long each step waits for its control. Works on copies; <see cref="Result"/> hands back the steps to save. No UI.
/// </summary>
public sealed class RecordingReview
{
    private readonly List<ActionInstance> _steps;
    private readonly Dictionary<string, string> _textBeforeAsk = new();

    public RecordingReview(IEnumerable<ActionInstance> steps, IEnumerable<string>? leftOut = null)
    {
        _steps = steps.Select(s => s.Clone()).ToList();
        LeftOut = leftOut?.ToList() ?? new List<string>();
    }

    public IReadOnlyList<ActionInstance> Steps => _steps;

    /// <summary>What was done but can't be repeated ("Pressing Enter"), to show under the steps.</summary>
    public IReadOnlyList<string> LeftOut { get; }

    public event Action? Changed;

    public int Count => _steps.Count;

    public string Describe(int index) => UiSteps.Describe(_steps[index]);

    public void Delete(int index)
    {
        Check(index);
        _textBeforeAsk.Remove(_steps[index].Uid);
        _steps.RemoveAt(index);
        Changed?.Invoke();
    }

    /// <summary>Moves a step so it ends up at <paramref name="to"/>.</summary>
    public void Move(int from, int to)
    {
        Check(from);
        to = Math.Clamp(to, 0, _steps.Count - 1);
        if (from == to) return;
        var step = _steps[from];
        _steps.RemoveAt(from);
        _steps.Insert(to, step);
        Changed?.Invoke();
    }

    /// <summary>True for a typing step whose text can be changed (not a password box).</summary>
    public bool CanEditText(int index) => _steps[index].ActionId == AppControlIds.Type && !UiSteps.IsSecret(_steps[index]);

    public string TextOf(int index) =>
        UiSteps.AsksEachTime(_steps[index]) ? (_textBeforeAsk.TryGetValue(_steps[index].Uid, out var t) ? t : "") : UiSteps.TextParam(_steps[index], "text");

    /// <summary>Changes what is typed. It also turns "ask each time" off for that step.</summary>
    public void SetText(int index, string text)
    {
        Check(index);
        if (!CanEditText(index)) throw new InvalidOperationException("Only the text of a typing step that isn't a password box can be changed.");
        _steps[index].Parameters["text"] = ParamValue.Text(text ?? "");
        _textBeforeAsk.Remove(_steps[index].Uid);
        Changed?.Invoke();
    }

    public bool AsksEachTime(int index) => UiSteps.AsksEachTime(_steps[index]);

    /// <summary>Turns typed text into a value asked for each time it runs, or back. A password box always asks.</summary>
    public void SetAskEachTime(int index, bool ask)
    {
        Check(index);
        var step = _steps[index];
        if (step.ActionId != AppControlIds.Type) throw new InvalidOperationException("Only a typing step can ask each time.");
        if (UiSteps.IsSecret(step))
        {
            if (!ask) throw new InvalidOperationException("A password box always asks: what was typed into it was never recorded.");
            return;
        }
        if (ask == UiSteps.AsksEachTime(step)) return;
        if (ask)
        {
            _textBeforeAsk[step.Uid] = UiSteps.TextParam(step, "text");
            step.Parameters["text"] = ParamValue.Ask();
        }
        else
        {
            step.Parameters["text"] = ParamValue.Text(_textBeforeAsk.TryGetValue(step.Uid, out var before) ? before : "");
            _textBeforeAsk.Remove(step.Uid);
        }
        Changed?.Invoke();
    }

    public double TimeoutOf(int index) => UiSteps.TimeoutOf(_steps[index]);

    /// <summary>How long the step waits for its control or window, in seconds (1 to 300).</summary>
    public void SetTimeout(int index, double seconds)
    {
        Check(index);
        if (double.IsNaN(seconds)) return;
        var s = Math.Round(Math.Clamp(seconds, UiSteps.MinTimeoutSeconds, UiSteps.MaxTimeoutSeconds), 1);
        _steps[index].Parameters["timeout"] = ParamValue.Duration(s);
        Changed?.Invoke();
    }

    /// <summary>The steps to save, as fresh copies. A window wait left with nothing after it in that window is dropped.</summary>
    public IReadOnlyList<ActionInstance> Result()
    {
        var list = new List<ActionInstance>();
        for (var i = 0; i < _steps.Count; i++)
        {
            var s = _steps[i];
            if (s.ActionId == AppControlIds.WaitWindow && (i == _steps.Count - 1 || _steps[i + 1].ActionId == AppControlIds.WaitWindow)) continue;
            list.Add(s.Clone());
        }
        return list;
    }

    private void Check(int index)
    {
        if (index < 0 || index >= _steps.Count) throw new ArgumentOutOfRangeException(nameof(index));
    }
}
