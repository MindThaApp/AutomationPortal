namespace AutomationPortal.Triggers;

public enum ActivityKind { Fired, Ran, Skipped }

/// <summary>One line in an automation's activity: its trigger fired, the shortcut ran (with the result), or it was skipped (with the reason).</summary>
public sealed record ActivityEntry(DateTimeOffset Time, Guid AutomationId, ActivityKind Kind, string Text);

/// <summary>
/// The recent trigger events of every automation, kept in memory only (nothing is written to disk, so it starts empty when the app starts).
/// Each automation keeps its last <see cref="PerAutomationLimit"/> events, and all automations together keep at most <see cref="TotalLimit"/>;
/// when that is exceeded the oldest event of any automation goes first.
/// </summary>
public sealed class AutomationActivityLog
{
    public const int PerAutomationLimit = 200;
    public const int TotalLimit = 2000;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, LinkedList<(long Seq, ActivityEntry Entry)>> _byAutomation = new();
    private readonly int _perAutomation, _total;
    private long _seq;
    private int _count;

    public AutomationActivityLog(int perAutomationLimit = PerAutomationLimit, int totalLimit = TotalLimit)
    {
        _perAutomation = Math.Max(1, perAutomationLimit);
        _total = Math.Max(1, totalLimit);
    }

    /// <summary>Raised after an event is added. May come from a worker thread.</summary>
    public event Action<Guid>? Added;

    public void Add(ActivityEntry entry)
    {
        lock (_gate)
        {
            if (!_byAutomation.TryGetValue(entry.AutomationId, out var list)) _byAutomation[entry.AutomationId] = list = new();
            list.AddLast((++_seq, entry));
            _count++;
            if (list.Count > _perAutomation) { list.RemoveFirst(); _count--; }
            while (_count > _total) EvictOldest();
        }
        try { Added?.Invoke(entry.AutomationId); } catch { /* a listener must never break the engine */ }
    }

    private void EvictOldest()
    {
        Guid? oldest = null;
        var best = long.MaxValue;
        foreach (var (id, list) in _byAutomation)
            if (list.First is { } f && f.Value.Seq < best) { best = f.Value.Seq; oldest = id; }
        if (oldest is not { } victim) { _count = 0; return; }
        var l = _byAutomation[victim];
        l.RemoveFirst();
        _count--;
        if (l.Count == 0) _byAutomation.Remove(victim);
    }

    /// <summary>The automation's events, newest first.</summary>
    public IReadOnlyList<ActivityEntry> Get(Guid automationId)
    {
        lock (_gate)
        {
            if (!_byAutomation.TryGetValue(automationId, out var list)) return Array.Empty<ActivityEntry>();
            return list.Reverse().Select(x => x.Entry).ToList();
        }
    }

    public int TotalCount { get { lock (_gate) return _count; } }
}
