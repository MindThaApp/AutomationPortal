namespace Flowmate.Core;

public sealed class TriggerInstance
{
    public string TriggerId { get; set; } = "";
    /// <summary>Literal values only (no variables).</summary>
    public Dictionary<string, ParamValue> Parameters { get; set; } = new();
}

public sealed class ConditionInstance
{
    public string ConditionId { get; set; } = "";
    public Dictionary<string, ParamValue> Parameters { get; set; } = new();
}

/// <summary>"When this happens (and these conditions hold) run that shortcut."</summary>
public sealed class Automation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Enabled { get; set; } = true;
    public TriggerInstance Trigger { get; set; } = new();
    public List<ConditionInstance> Conditions { get; set; } = new();
    public Guid ShortcutId { get; set; }
    public RunMode RunMode { get; set; } = RunMode.Immediately;
    public bool NotifyWhenRun { get; set; }
    public int CooldownSeconds { get; set; }
    public DateTimeOffset? LastRun { get; set; }
    /// <summary>Why Flow Mate turned this automation off by itself (for example "ran too often"), shown until the user resumes it. Null otherwise.</summary>
    public string? PausedReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public Automation Clone() => FlowmateJson.Clone(this);
}
