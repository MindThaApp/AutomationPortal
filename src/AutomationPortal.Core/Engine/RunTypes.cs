namespace AutomationPortal.Core;

/// <summary>Live feedback from a run (editor highlights, variable inspector). Callbacks may arrive on any thread.</summary>
public interface IRunObserver
{
    void OnStepStarted(string uid);
    void OnStepFinished(string uid, ContentValue output, TimeSpan duration, string? error);
    void OnVariableSet(string name, ContentValue value);
    void OnLog(string message);
    /// <summary>Progress text from <see cref="ActionContext.ReportProgress"/>.</summary>
    void OnProgress(string uid, string message) { }
}

public sealed class RunOptions
{
    public RunSource Source { get; set; } = RunSource.Library;
    /// <summary>Nesting depth: 0 for a top-level run. Set by Run Shortcut; hosts leave it alone.</summary>
    public int Depth { get; set; }
    public CancellationToken CancellationToken { get; set; }
    public IRunObserver? Observer { get; set; }
    /// <summary>Add a RunRecord to <see cref="IRunHistory"/> when the run ends (never for nested runs). Default true.</summary>
    public bool RecordHistory { get; set; } = true;
    /// <summary>
    /// "Run from this step": skip everything before this action. If the uid is a top-level action the run starts there;
    /// if it is inside an If/Menu/Repeat block the run starts at the beginning of that whole top-level block.
    /// The step before it gets its output from <see cref="SeedOutputs"/> (else the shortcut input).
    /// </summary>
    public string? StartAtUid { get; set; }
    /// <summary>Outputs from a previous run (uid to value) so later steps can still use earlier magic variables.</summary>
    public Dictionary<string, ContentValue>? SeedOutputs { get; set; }
    /// <summary>Use a specific run id (default: random).</summary>
    public string? RunId { get; set; }
    /// <summary>Named variables to start with ("Run from here" restores the ones set above the start point).</summary>
    public Dictionary<string, ContentValue>? SeedVariables { get; set; }
    /// <summary>Nested runs: the parent run's temp folder. Files made by the nested run stay usable by the parent; only the top-level run deletes it.</summary>
    public string? SharedTempFolder { get; set; }
    /// <summary>Leave the run's temp folder in place (editor runs whose outputs are reused by "Run from here"). Old folders are swept at startup.</summary>
    public bool KeepTemp { get; set; }
    /// <summary>Optional: awaited before each step that will run (not for turned-off steps). Only the editor sets it; null changes nothing. Not passed on to nested runs.</summary>
    public Func<BeforeStepInfo, Task>? BeforeStep { get; set; }
}

/// <summary>Which step is about to run, passed to <see cref="RunOptions.BeforeStep"/>.</summary>
public sealed class BeforeStepInfo
{
    public BeforeStepInfo(string uid, string actionId, string title) { Uid = uid; ActionId = actionId; Title = title; }
    public string Uid { get; }
    public string ActionId { get; }
    public string Title { get; }
}

public sealed class RunResult
{
    public string RunId { get; init; } = "";
    /// <summary>The run's temp folder when <see cref="RunOptions.KeepTemp"/> was set and the run created one (the caller deletes it).</summary>
    public string? TempFolder { get; init; }
    public Guid ShortcutId { get; init; }
    public string ShortcutName { get; init; } = "";
    public RunSource Source { get; init; }
    public RunStatus Status { get; init; }
    public ContentValue Output { get; init; } = ContentValue.Empty;
    /// <summary>Friendly message when Failed.</summary>
    public string? ErrorMessage { get; init; }
    public string? FailedStepUid { get; init; }
    public DateTimeOffset Started { get; init; }
    public TimeSpan Duration { get; init; }
    public IReadOnlyList<StepLog> Steps { get; init; } = Array.Empty<StepLog>();
    /// <summary>True when more than 500 steps ran and the log was cut short.</summary>
    public bool StepsTruncated { get; init; }

    public bool Succeeded => Status == RunStatus.Succeeded;

    public RunRecord ToRecord() => new()
    {
        RunId = RunId, ShortcutId = ShortcutId, ShortcutName = ShortcutName, Source = Source, Started = Started,
        DurationMs = Duration.TotalMilliseconds, Status = Status, ErrorMessage = ErrorMessage,
        FailedStepUid = FailedStepUid, OutputPreview = ContentFormat.Truncate(Output.AsText(), 200), Steps = Steps.ToList(),
    };
}
