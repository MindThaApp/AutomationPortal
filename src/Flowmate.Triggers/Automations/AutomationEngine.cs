using Flowmate.Core;

namespace Flowmate.Triggers;

/// <summary>What the automations page shows next to an automation.</summary>
/// <param name="IsRunning">The trigger is being watched right now.</param>
/// <param name="Error">A plain-English problem to show ("The shortcut was deleted", "Another app already uses Ctrl+Alt+M") or null.</param>
/// <param name="LastSkipReason">Why the last trigger didn't run the shortcut (cooldown, conditions, skipped by the user) or null.</param>
public sealed record AutomationStatus(bool IsRunning, string? Error, string? LastSkipReason);

public sealed class AutomationRanEventArgs : EventArgs
{
    public required Guid AutomationId { get; init; }
    public required Guid ShortcutId { get; init; }
    public string ShortcutName { get; init; } = "";
    /// <summary>What happened, e.g. "Spotify opened".</summary>
    public string? Description { get; init; }
    public RunResult? Result { get; init; }
    public bool IsTest { get; init; }
}

/// <summary>
/// Offered by the engine's sinks: whether the automation's shortcut is running right now or finished within <c>grace</c>.
/// The folder trigger uses it to ignore the changes the automation's own run made, so it can't trigger itself in a loop.
/// </summary>
internal interface ITriggerSinkRunWindow
{
    bool IsOwnRunRecent(TimeSpan grace);
    /// <summary>True when the automation's last run handled or produced this path (its input, or a file a step gave back).</summary>
    bool WasTouchedByOwnRun(string path) => false;
    /// <summary>How long until the automation may run again (its cooldown), or zero.</summary>
    TimeSpan CooldownRemaining() => TimeSpan.Zero;
    /// <summary>True while the engine handles a fire for this automation (checking its conditions, asking first, running).</summary>
    bool IsBusy() => false;
    /// <summary>Says whether the next fire holds only files that appeared while the automation's own run was going on.</summary>
    void NoteSelfFed(bool selfFed) { }
}

/// <summary>Marker for triggers that watch files. The engine gives their automations a minimum cooldown so a run's own output can't start a loop.</summary>
internal interface IFileWatchTrigger { }

/// <summary>The result of one trigger firing.</summary>
internal sealed record AutomationOutcome(bool Ran, RunResult? Result, string? SkipReason)
{
    public ContentValue? Output => Ran && Result is { Status: RunStatus.Succeeded } r ? r.Output : null;
}

/// <summary>
/// Loads automations from the library, starts their trigger providers, and when a trigger fires checks pause, cooldown and
/// conditions, optionally asks first, then runs the shortcut with the trigger's output as input.
/// One broken provider never affects the others: problems are caught, logged and shown as an error on that automation.
/// </summary>
public sealed class AutomationEngine : IDisposable
{
    private sealed class Active
    {
        public required string Signature { get; init; }
        public IDisposable? Handle { get; set; }
    }

    private readonly FlowmateServices _services;
    private readonly TriggerRegistry _triggers;
    private readonly ConditionRegistry _conditions;
    private readonly TriggerEnvironment? _env;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Active> _active = new();
    private readonly Dictionary<Guid, string> _errors = new();
    private readonly Dictionary<Guid, string> _skips = new();
    private readonly Dictionary<Guid, DateTimeOffset> _lastRun = new();
    private readonly HashSet<Guid> _busy = new();
    private readonly HashSet<Guid> _running = new();
    private readonly Dictionary<Guid, DateTimeOffset> _runEnded = new();
    private readonly Dictionary<Guid, HashSet<string>> _touched = new();
    private readonly Dictionary<Guid, Queue<DateTimeOffset>> _recentRuns = new();
    private readonly Dictionary<Guid, int> _selfFedStreak = new();
    private readonly CancellationTokenSource _cts = new();
    private Timer? _reload;
    private bool _started, _disposed, _firstLoad = true;

    /// <summary>Automations with a file-watching trigger never run more often than this, even when their cooldown is 0.</summary>
    internal const int MinimumFileWatchCooldownSeconds = 5;

    /// <summary>
    /// Runaway breaker, for automations that watch files only (their own runs can feed them): one that runs more than this many
    /// times within <see cref="RunawayWindow"/>, or <see cref="RunawaySelfFedRuns"/> times in a row only on files that appeared
    /// while it was running, is paused and the user is told. That stops loops the per-run checks can't see: two automations
    /// triggering each other, or a command or program a run started that writes new files into the watched folder.
    /// </summary>
    internal const int RunawayRuns = 30;
    internal static readonly TimeSpan RunawayWindow = TimeSpan.FromMinutes(2);
    internal const int RunawaySelfFedRuns = 20;
    /// <summary>What <see cref="Automation.PausedReason"/> says after the breaker paused an automation.</summary>
    public const string RanTooOften = "ran too often";

    public AutomationEngine(FlowmateServices services, TriggerRegistry triggers, ConditionRegistry conditions, Func<DateTimeOffset>? clock = null)
        : this(services, triggers, conditions, null, clock) { }

    internal AutomationEngine(FlowmateServices services, TriggerRegistry triggers, ConditionRegistry conditions, TriggerEnvironment? env, Func<DateTimeOffset>? clock = null)
    {
        _services = services;
        _triggers = triggers;
        _conditions = conditions;
        _env = env;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    /// <summary>An automation ran (or was tested). Raised on a worker thread.</summary>
    public event EventHandler<AutomationRanEventArgs>? AutomationRan;
    /// <summary>An automation's status (running, error, last skip) changed. Raised on a worker thread.</summary>
    public event EventHandler<Guid>? StatusChanged;

    public bool IsPaused => _services.Settings.Current.AutomationsPaused;

    public AutomationStatus GetStatus(Guid automationId)
    {
        lock (_gate)
            return new AutomationStatus(_active.TryGetValue(automationId, out var a) && a.Handle is not null, _errors.GetValueOrDefault(automationId), _skips.GetValueOrDefault(automationId));
    }

    // ---- lifecycle ---------------------------------------------------------------------------

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
        }
        _services.Library.Changed += OnLibraryChanged;
        _services.Settings.Changed += OnSettingsChanged;
        Reload();
    }

    private void OnLibraryChanged(object? sender, LibraryChangedEventArgs e)
    {
        if ((e.What & (LibraryChange.Automations | LibraryChange.Shortcuts)) == 0) return;
        ScheduleReload();
    }

    private void OnSettingsChanged(object? sender, AppSettings e) => ScheduleReload();

    private void ScheduleReload()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _reload?.Dispose();
            _reload = new Timer(_ => TriggerLog.Guard(Reload, "Automation reload"), null, 250, Timeout.Infinite);
        }
    }

    /// <summary>Re-reads the library: starts new automations, stops removed or changed ones, honours the global pause.</summary>
    public void Reload()
    {
        var changed = new List<Guid>();
        lock (_gate)
        {
            if (_disposed) return;
            var paused = IsPaused;
            var wanted = paused ? new List<Automation>() : _services.Library.Automations.Where(a => a.Enabled).ToList();
            var wantedIds = wanted.Select(a => a.Id).ToHashSet();

            foreach (var id in _active.Keys.Where(id => !wantedIds.Contains(id)).ToList())
            {
                StopLocked(id);
                changed.Add(id);
            }
            if (_env is not null) _env.IsInitialLoad = _firstLoad;
            foreach (var a in wanted)
            {
                var signature = Signature(a);
                if (_active.TryGetValue(a.Id, out var current))
                {
                    if (current.Signature == signature) continue;
                    StopLocked(a.Id);
                }
                StartLocked(a, signature);
                changed.Add(a.Id);
            }
            foreach (var id in _errors.Keys.Where(id => !_active.ContainsKey(id)).ToList()) _errors.Remove(id);
            _firstLoad = false;
            if (_env is not null) _env.IsInitialLoad = false;
        }
        foreach (var id in changed) RaiseStatus(id);
    }

    private string Signature(Automation a) =>
        FlowmateJson.Serialize(a.Trigger) + "|" + (_services.Library.GetById(a.ShortcutId) is null ? "missing" : "ok");

    private void StartLocked(Automation a, string signature)
    {
        _errors.Remove(a.Id);
        var entry = new Active { Signature = signature };
        _active[a.Id] = entry;

        if (_services.Library.GetById(a.ShortcutId) is null)
        {
            _errors[a.Id] = "The shortcut this automation runs was deleted. Choose another shortcut for it.";
            return;
        }
        var provider = _triggers.Get(a.Trigger.TriggerId);
        if (provider is null)
        {
            _errors[a.Id] = "This trigger isn't available in this version of Flowmate.";
            return;
        }
        try
        {
            var config = TriggerConfig.From(a.Trigger, provider.Descriptor.Parameters);
            entry.Handle = provider.Start(config, new AutomationSink(this, a.Id));
        }
        catch (Exception ex)
        {
            TriggerLog.Write($"Trigger '{a.Trigger.TriggerId}' couldn't start: {ex}");
            _errors[a.Id] = ex is InvalidOperationException or ActionException or LibraryException
                ? ex.Message
                : $"Flowmate couldn't start this trigger. {ex.Message}";
        }
    }

    private void StopLocked(Guid id)
    {
        if (!_active.Remove(id, out var entry)) return;
        try { entry.Handle?.Dispose(); }
        catch (Exception ex) { TriggerLog.Write("A trigger didn't stop cleanly: " + ex.Message); }
    }

    private void RaiseStatus(Guid id)
    {
        try { StatusChanged?.Invoke(this, id); } catch (Exception ex) { TriggerLog.Write("StatusChanged handler failed: " + ex.Message); }
    }

    internal void ReportProblem(Guid id, string? message)
    {
        lock (_gate)
        {
            if (!_active.ContainsKey(id)) return;
            if (message is null) { if (!_errors.Remove(id)) return; }
            else if (_errors.TryGetValue(id, out var old) && old == message) return;
            else _errors[id] = message;
        }
        RaiseStatus(id);
    }

    private void SetSkip(Guid id, string? reason)
    {
        lock (_gate)
        {
            if (reason is null) _skips.Remove(id); else _skips[id] = $"{_clock().LocalDateTime:t}: {reason}";
        }
        RaiseStatus(id);
    }

    // ---- firing ------------------------------------------------------------------------------

    private sealed class AutomationSink : ITriggerSink, ITriggerSinkExtras, ITriggerSinkRunWindow
    {
        private readonly AutomationEngine _engine;
        private readonly Guid _id;
        public AutomationSink(AutomationEngine engine, Guid id) { _engine = engine; _id = id; }
        // Always hop to the thread pool: providers fire from hook and window threads that must never wait on the engine.
        public void Fire(ContentValue output, string? description = null) =>
            _ = Task.Run(() => _engine.HandleFireAsync(_id, output, description));
        public void ReportProblem(string? message) => _engine.ReportProblem(_id, message);
        public bool IsOwnRunRecent(TimeSpan grace) => _engine.IsOwnRunRecent(_id, grace);
        public bool WasTouchedByOwnRun(string path) => _engine.WasTouchedByOwnRun(_id, path);
        public TimeSpan CooldownRemaining() => _engine.CooldownRemaining(_id);
        public bool IsBusy() { lock (_engine._gate) return _engine._busy.Contains(_id); }
        public void NoteSelfFed(bool selfFed) => _engine.NoteSelfFed(_id, selfFed);
        public async Task<ContentValue?> FireAndWaitAsync(ContentValue output, string? description)
        {
            var outcome = await Task.Run(() => _engine.HandleFireAsync(_id, output, description));
            return outcome.Output;
        }
    }

    /// <summary>The whole pipeline for one trigger firing. Never throws.</summary>
    internal async Task<AutomationOutcome> HandleFireAsync(Guid automationId, ContentValue output, string? description)
    {
        try
        {
            if (_disposed) return Skip("Flow Mate is closing.");
            if (IsPaused) return Skip(null, "Automations are paused.");
            var automation = _services.Library.Automations.FirstOrDefault(a => a.Id == automationId);
            if (automation is null || !automation.Enabled) return Skip(null, "This automation is off.");

            var shortcut = _services.Library.GetById(automation.ShortcutId);
            if (shortcut is null)
            {
                ReportProblem(automationId, "The shortcut this automation runs was deleted. Choose another shortcut for it.");
                return Skip("The shortcut was deleted.");
            }

            lock (_gate)
            {
                if (!_busy.Add(automationId)) return Skip("It was still busy with the last trigger.");
            }
            try
            {
                var now = _clock();
                if (CooldownLeft(automation, now) > TimeSpan.Zero)
                    return Skip($"It ran less than {DescribeSeconds(EffectiveCooldown(automation))} ago.");

                if (!await ConditionsMetAsync(automation))
                    return Skip("The \"Only if…\" conditions weren't met.");

                if (automation.RunMode == RunMode.AskFirst)
                {
                    var yes = await _services.Notifications.AskToRunAsync(shortcut.Name, description ?? "something you set up happened");
                    if (!yes) return Skip("You chose to skip it.");
                }
                else if (TripsRunawayBreaker(automation, now))
                {
                    SwitchOffRunaway(automation, shortcut.Name);
                    return Skip("It ran too often, which looks like a loop, so Flow Mate paused it.");
                }

                SetSkip(automationId, null);
                RecordRun(automation, now);
                RunResult result;
                var touched = new HashSet<string>(output.OfType<PathItem>().Select(p => NormalizePath(p.Path)), StringComparer.OrdinalIgnoreCase);
                lock (_gate)
                {
                    _running.Add(automationId);
                    _touched[automationId] = touched;
                }
                try { result = await RunAsync(shortcut, output, automation, description, isTest: false, touched); }
                finally
                {
                    lock (_gate)
                    {
                        _running.Remove(automationId);
                        _runEnded[automationId] = _clock();
                    }
                }
                if (_services.Library.Automations.FirstOrDefault(a => a.Id == automationId) is { } latest
                    && _triggers.Get(latest.Trigger.TriggerId) is IOneShotTrigger)
                    DisableAfterOneShot(latest);
                return new AutomationOutcome(true, result, null);
            }
            finally { lock (_gate) _busy.Remove(automationId); }
        }
        catch (Exception ex)
        {
            TriggerLog.Write($"Automation {automationId} failed to run: {ex}");
            return new AutomationOutcome(false, null, ex.Message);
        }

        AutomationOutcome Skip(string? reason, string? silent = null)
        {
            if (reason is not null) SetSkip(automationId, reason);
            TriggerLog.Write($"Automation {automationId} skipped: {reason ?? silent}");
            return new AutomationOutcome(false, null, reason ?? silent);
        }
    }

    /// <summary>True while the automation's shortcut is running, and for <paramref name="grace"/> after it finished.</summary>
    internal bool IsOwnRunRecent(Guid automationId, TimeSpan grace)
    {
        lock (_gate)
            return _running.Contains(automationId)
                || _runEnded.TryGetValue(automationId, out var end) && _clock() - end < grace;
    }

    internal bool WasTouchedByOwnRun(Guid automationId, string path)
    {
        lock (_gate)
            return _touched.TryGetValue(automationId, out var set) && set.Contains(NormalizePath(path));
    }

    internal TimeSpan CooldownRemaining(Guid automationId)
    {
        var automation = _services.Library.Automations.FirstOrDefault(a => a.Id == automationId);
        return automation is null ? TimeSpan.Zero : CooldownLeft(automation, _clock());
    }

    private int EffectiveCooldown(Automation automation) =>
        _triggers.Get(automation.Trigger.TriggerId) is IFileWatchTrigger
            ? Math.Max(automation.CooldownSeconds, MinimumFileWatchCooldownSeconds)
            : automation.CooldownSeconds;

    private TimeSpan CooldownLeft(Automation automation, DateTimeOffset now)
    {
        var cooldown = EffectiveCooldown(automation);
        if (cooldown <= 0) return TimeSpan.Zero;
        var last = LastRunOf(automation);
        if (_triggers.Get(automation.Trigger.TriggerId) is IFileWatchTrigger)
        {
            // Count from the end of a long run too, so files the run itself wrote can't start the next one.
            DateTimeOffset? ended;
            lock (_gate) ended = _runEnded.TryGetValue(automation.Id, out var e) ? e : null;
            if (ended is { } en && (last is null || en > last)) last = en;
        }
        if (last is not { } l) return TimeSpan.Zero;
        var left = TimeSpan.FromSeconds(cooldown) - (now - l);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    internal void NoteSelfFed(Guid automationId, bool selfFed)
    {
        lock (_gate)
        {
            if (selfFed) _selfFedStreak[automationId] = _selfFedStreak.GetValueOrDefault(automationId) + 1;
            else _selfFedStreak.Remove(automationId);
        }
    }

    /// <summary>Counts this run; true when it would be one run too many inside the runaway window, or one self-fed run too many in a row.</summary>
    private bool TripsRunawayBreaker(Automation automation, DateTimeOffset now)
    {
        if (_triggers.Get(automation.Trigger.TriggerId) is not IFileWatchTrigger) return false;
        lock (_gate)
        {
            if (!_recentRuns.TryGetValue(automation.Id, out var runs)) _recentRuns[automation.Id] = runs = new Queue<DateTimeOffset>();
            while (runs.Count > 0 && now - runs.Peek() >= RunawayWindow) runs.Dequeue();
            if (runs.Count >= RunawayRuns || _selfFedStreak.GetValueOrDefault(automation.Id) > RunawaySelfFedRuns)
            {
                // A fresh start once the user resumes it.
                runs.Clear();
                _selfFedStreak.Remove(automation.Id);
                return true;
            }
            runs.Enqueue(now);
            return false;
        }
    }

    private void SwitchOffRunaway(Automation a, string shortcutName)
    {
        TriggerLog.Write($"Automation {a.Id} ran too often; paused.");
        try
        {
            var copy = (_services.Library.Automations.FirstOrDefault(x => x.Id == a.Id) ?? a).Clone();
            copy.Enabled = false;
            copy.PausedReason = RanTooOften;
            _services.Library.SaveAutomation(copy);
        }
        catch (Exception ex) { TriggerLog.Write("Couldn't pause a runaway automation: " + ex.Message); }
        try
        {
            _services.Notifications.Show($"Flow Mate paused an automation that runs \"{shortcutName}\"",
                "It ran too often, which looks like a loop (for example two automations setting each other off, or a command that keeps "
                + "adding files to the watched folder). Check what it changes, then press Resume in Automations.");
        }
        catch (Exception ex) { TriggerLog.Write("Notification failed: " + ex.Message); }
    }

    private static string NormalizePath(string path) => path.Trim().TrimEnd('\\', '/');

    /// <summary>Collects the files each step gives back, so the folder trigger can tell the run's own output from new files.</summary>
    private sealed class TouchedPathsObserver : IRunObserver
    {
        private readonly HashSet<string> _set;
        private readonly object _gate;
        public TouchedPathsObserver(HashSet<string> set, object gate) { _set = set; _gate = gate; }
        public void Add(ContentValue value)
        {
            var paths = value.OfType<PathItem>().Select(p => NormalizePath(p.Path)).ToList();
            if (paths.Count == 0) return;
            lock (_gate) foreach (var p in paths) _set.Add(p);
        }
        public void OnStepStarted(string uid) { }
        public void OnStepFinished(string uid, ContentValue output, TimeSpan duration, string? error) => Add(output);
        public void OnVariableSet(string name, ContentValue value) => Add(value);
        public void OnLog(string message) { }
    }

    private DateTimeOffset? LastRunOf(Automation a)
    {
        DateTimeOffset? mem;
        lock (_gate) mem = _lastRun.TryGetValue(a.Id, out var m) ? m : null;
        return mem is null ? a.LastRun : a.LastRun is { } l && l > mem ? l : mem;
    }

    private void RecordRun(Automation a, DateTimeOffset now)
    {
        lock (_gate) _lastRun[a.Id] = now;
        try
        {
            var copy = (_services.Library.Automations.FirstOrDefault(x => x.Id == a.Id) ?? a).Clone();
            copy.LastRun = now;
            _services.Library.SaveAutomation(copy);
        }
        catch (Exception ex) { TriggerLog.Write("Couldn't save the last run time: " + ex.Message); }
    }

    private void DisableAfterOneShot(Automation a)
    {
        try
        {
            var copy = a.Clone();
            copy.Enabled = false;
            _services.Library.SaveAutomation(copy);
        }
        catch (Exception ex) { TriggerLog.Write("Couldn't switch off a one-time automation: " + ex.Message); }
    }

    private async Task<bool> ConditionsMetAsync(Automation automation)
    {
        foreach (var c in automation.Conditions)
        {
            var provider = _conditions.Get(c.ConditionId);
            if (provider is null)
            {
                TriggerLog.Write($"Unknown condition '{c.ConditionId}'");
                return false;
            }
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                if (!await provider.IsMetAsync(TriggerConfig.From(c, provider.Descriptor.Parameters), timeout.Token)) return false;
            }
            catch (Exception ex)
            {
                TriggerLog.Write($"Condition '{c.ConditionId}' failed: {ex.Message}");
                return false;
            }
        }
        return true;
    }

    private async Task<RunResult> RunAsync(Shortcut shortcut, ContentValue input, Automation automation, string? description, bool isTest,
        HashSet<string>? touched = null)
    {
        RunResult result;
        var observer = touched is null ? null : new TouchedPathsObserver(touched, _gate);
        try
        {
            result = await _services.Runner.RunAsync(shortcut.Id, input, new RunOptions { Source = RunSource.Automation, CancellationToken = _cts.Token, Observer = observer });
            observer?.Add(result.Output);
        }
        catch (Exception ex)
        {
            TriggerLog.Write($"Running '{shortcut.Name}' threw: {ex}");
            result = new RunResult { ShortcutId = shortcut.Id, ShortcutName = shortcut.Name, Source = RunSource.Automation, Status = RunStatus.Failed, ErrorMessage = ex.Message, Started = _clock() };
        }

        try
        {
            if (result.Status == RunStatus.Failed)
                _services.Notifications.Show($"{shortcut.Name} didn't finish", result.ErrorMessage ?? "Something went wrong. Open Run History for details.");
            else if (automation.NotifyWhenRun && !isTest && result.Status != RunStatus.Cancelled)
                _services.Notifications.Show(shortcut.Name, description is null ? "It ran automatically." : $"It ran because {Lower(description)}.");
        }
        catch (Exception ex) { TriggerLog.Write("Notification failed: " + ex.Message); }

        try
        {
            AutomationRan?.Invoke(this, new AutomationRanEventArgs
            {
                AutomationId = automation.Id, ShortcutId = shortcut.Id, ShortcutName = shortcut.Name, Description = description, Result = result, IsTest = isTest,
            });
        }
        catch (Exception ex) { TriggerLog.Write("AutomationRan handler failed: " + ex.Message); }
        return result;
    }

    private static string Lower(string s) => s.Length > 0 && char.IsUpper(s[0]) && (s.Length == 1 || !char.IsUpper(s[1])) ? char.ToLowerInvariant(s[0]) + s[1..] : s;

    private static string DescribeSeconds(int s) => s % 60 == 0 && s >= 60 ? (s == 60 ? "1 minute" : $"{s / 60} minutes") : PluralAgreement.Fix($"{s} seconds");

    // ---- test now ----------------------------------------------------------------------------

    /// <summary>Runs the automation's shortcut right now with sample trigger input. Ignores pause, cooldown, conditions and "ask first".</summary>
    public async Task<RunResult> TestNowAsync(Guid automationId, CancellationToken ct = default)
    {
        var automation = _services.Library.Automations.FirstOrDefault(a => a.Id == automationId)
            ?? throw new LibraryException("That automation doesn't exist any more.");
        var shortcut = _services.Library.GetById(automation.ShortcutId);
        if (shortcut is null)
            return new RunResult
            {
                ShortcutId = automation.ShortcutId, Source = RunSource.Automation, Status = RunStatus.Failed, Started = _clock(),
                ErrorMessage = "The shortcut this automation runs was deleted. Choose another shortcut for it.",
            };
        // Sample files live in a fresh temp folder that is removed afterwards; real folders and drives are never used.
        using var sandbox = SampleSandbox.Create(_services.Paths);
        var input = SampleInputs.For(automation, _triggers, sandbox);
        return await RunAsync(shortcut, input, automation, "you tested it", isTest: true);
    }

    public void Dispose()
    {
        List<Guid> ids;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _reload?.Dispose();
            ids = _active.Keys.ToList();
            foreach (var id in ids) StopLocked(id);
        }
        if (_started)
        {
            _services.Library.Changed -= OnLibraryChanged;
            _services.Settings.Changed -= OnSettingsChanged;
        }
        _cts.Cancel();
    }
}
