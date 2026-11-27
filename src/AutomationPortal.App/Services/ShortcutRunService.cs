using System.Collections.Concurrent;
using System.Diagnostics;
using AutomationPortal.App.Logic;
using AutomationPortal.Core;

namespace AutomationPortal.App.Services;

public enum TileRunState { Idle, Running, Succeeded, Failed }

public sealed class RunStateChangedEventArgs : EventArgs
{
    public RunStateChangedEventArgs(Guid shortcutId, string name, TileRunState state, RunResult? result, RunSource source)
    { ShortcutId = shortcutId; ShortcutName = name; State = state; Result = result; Source = source; }

    public Guid ShortcutId { get; }
    public string ShortcutName { get; }
    public TileRunState State { get; }
    public RunResult? Result { get; }
    public RunSource Source { get; }
}

public sealed class RunProgressEventArgs : EventArgs
{
    public RunProgressEventArgs(Guid shortcutId, string text, (int Done, int? Total)? count = null) { ShortcutId = shortcutId; Text = text; Count = count; }
    public Guid ShortcutId { get; }
    /// <summary>The numbers behind the count line, when the step or loop has reported any.</summary>
    public (int Done, int? Total)? Count { get; }
    /// <summary>Just the count line, such as "12 of 40", or null.</summary>
    public string? CountText => RunProgressText.CountLine(Count);
    /// <summary>One or two lines: the step, then the count when there is one.</summary>
    public string Text { get; }
}

/// <summary>
/// Runs shortcuts on behalf of the UI (tiles, Quick Panel, Quick Run) and tells everyone how it went, so a tile
/// can show a spinner and then a tick or a cross. The runner itself writes run history.
/// </summary>
public sealed class ShortcutRunService
{
    private readonly IShortcutRunner _runner;
    private readonly IShortcutLibrary _library;
    private readonly ISettingsStore _settings;
    private readonly Func<INotificationService> _notifications;
    private readonly IUiThread _ui;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    private readonly Func<string, string?> _stepTitle;

    public ShortcutRunService(IShortcutRunner runner, IShortcutLibrary library, ISettingsStore settings, Func<INotificationService> notifications, IUiThread ui,
        Func<string, string?>? stepTitle = null)
    {
        _runner = runner; _library = library; _settings = settings; _notifications = notifications; _ui = ui;
        _stepTitle = stepTitle ?? (_ => null);
    }

    /// <summary>Raised on the UI thread.</summary>
    public event EventHandler<RunStateChangedEventArgs>? StateChanged;

    /// <summary>Raised on the UI thread while a run lasts longer than <see cref="RunFeedback.ProgressDelayMs"/>: where it is now, such as "Step 3 of 7: Resize pictures" and "12 of 40".</summary>
    public event EventHandler<RunProgressEventArgs>? ProgressChanged;

    /// <summary>True when the app reports this finished run itself (it changed files: a result bar or a toast with Undo), so the plain "finished" toast is left out.</summary>
    public Func<RunResult, bool>? ReportsOwnResult { get; set; }

    private readonly ConcurrentDictionary<Guid, RunProgressEventArgs> _latest = new();

    /// <summary>Where a long run is now, for a window opened while it runs (Quick Run); null for a run that is short or not running.</summary>
    public RunProgressEventArgs? LatestProgress(Guid id) => _latest.TryGetValue(id, out var p) ? p : null;

    public bool IsRunning(Guid id) => _running.ContainsKey(id);

    public int RunningCount => _running.Count;

    public void CancelAll()
    {
        foreach (var id in _running.Keys) Cancel(id);
    }

    public void Cancel(Guid id)
    {
        if (_running.TryGetValue(id, out var cts))
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }   // the run finished a moment ago
        }
    }

    /// <summary>Click on a tile: run it, or stop it if it is already running.</summary>
    public void Toggle(Shortcut shortcut, RunSource source = RunSource.Library)
    {
        if (IsRunning(shortcut.Id)) Cancel(shortcut.Id);
        else _ = RunAsync(shortcut, null, source);
    }

    /// <param name="previewPicks">The files and folders picked for a preview just before; the run uses them instead of asking again.</param>
    public async Task<RunResult?> RunAsync(Shortcut shortcut, ContentValue? input = null, RunSource source = RunSource.Library, CancellationToken external = default,
        PreviewPicks? previewPicks = null)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(external);
        if (!_running.TryAdd(shortcut.Id, cts)) return null;
        Raise(shortcut, TileRunState.Running, null, source);
        RunResult? result = null;
        var tracker = new RunProgressTracker(shortcut.Actions, _stepTitle);
        var reporting = new ProgressReporter(this, shortcut.Id, tracker);
        try
        {
            result = await Task.Run(() => _runner.RunAsync(shortcut, input ?? ContentValue.Empty,
                new RunOptions { Source = source, CancellationToken = cts.Token, PreviewPicks = previewPicks, Observer = reporting })).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            result = new RunResult
            {
                ShortcutId = shortcut.Id, ShortcutName = shortcut.Name, Source = source, Status = RunStatus.Failed, Started = DateTimeOffset.Now,
                ErrorMessage = Strings.Get("ShortcutRunService_SomethingWentWrongWhile") + ex.Message,
            };
        }
        finally
        {
            reporting.Stop();
            _running.TryRemove(shortcut.Id, out _);
            _latest.TryRemove(shortcut.Id, out _);
            cts.Dispose();
        }

        var state = result is null || result.Status == RunStatus.Cancelled ? TileRunState.Idle
                  : result.Status == RunStatus.Failed ? TileRunState.Failed : TileRunState.Succeeded;
        Raise(shortcut, state, result, source);

        if (source == RunSource.Hotkey && _settings.Current.NotifyOnHotkeyRuns && result is not null && state != TileRunState.Idle
            && !(state == TileRunState.Succeeded && ReportsOwnResult?.Invoke(result) == true))
        {
            try
            {
                _notifications().Show(state == TileRunState.Failed ? $"\"{shortcut.Name}\" didn't finish" : $"\"{shortcut.Name}\" finished",
                    state == TileRunState.Failed ? result.ErrorMessage ?? Strings.Get("ShortcutRunService_SomethingWentWrong") : Strings.Get("ShortcutRunService_ItRanWithoutProblems"));
            }
            catch (ActionException) { }
        }
        return result;
    }

    public Task<RunResult?> RunAsync(Guid shortcutId, ContentValue? input = null, RunSource source = RunSource.Library) =>
        _library.GetById(shortcutId) is { } s ? RunAsync(s, input, source) : Task.FromResult<RunResult?>(null);

    /// <summary>Passes the tracker's callbacks on, and tells the UI where the run is once it has lasted <see cref="RunFeedback.ProgressDelayMs"/>, at most about six times a second.</summary>
    private sealed class ProgressReporter : IRunObserver
    {
        private readonly ShortcutRunService _owner;
        private readonly Guid _id;
        private readonly RunProgressTracker _tracker;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _lastPublishedMs = -1000;
        private string? _lastText;
        private volatile bool _stopped;

        public ProgressReporter(ShortcutRunService owner, Guid id, RunProgressTracker tracker)
        {
            _owner = owner; _id = id; _tracker = tracker;
            _ = Task.Delay(RunFeedback.ProgressDelayMs).ContinueWith(_ => Publish(), TaskScheduler.Default);
        }

        public void Stop() => _stopped = true;

        public void OnStepStarted(string uid) { _tracker.OnStepStarted(uid); Publish(); }
        public void OnCount(string uid, int done, int? total) { _tracker.OnCount(uid, done, total); Publish(); }
        public void OnStepFinished(string uid, ContentValue output, TimeSpan duration, string? error) { }
        public void OnVariableSet(string name, ContentValue value) { }
        public void OnLog(string message) { }

        private void Publish()
        {
            var now = _clock.ElapsedMilliseconds;
            if (_stopped || now < RunFeedback.ProgressDelayMs) return;
            string? text;
            lock (this)
            {
                if (now - _lastPublishedMs < 150) return;
                text = _tracker.Text();
                if (text is null || text == _lastText) return;
                _lastText = text;
                _lastPublishedMs = now;
            }
            var args = new RunProgressEventArgs(_id, text, _tracker.Count());
            _ = _owner._ui.InvokeAsync(() =>
            {
                if (_stopped) return;
                _owner._latest[_id] = args;
                _owner.ProgressChanged?.Invoke(_owner, args);
            });
        }
    }

    private void Raise(Shortcut s, TileRunState state, RunResult? result, RunSource source)
    {
        var args = new RunStateChangedEventArgs(s.Id, s.Name, state, result, source);
        _ = _ui.InvokeAsync(() => StateChanged?.Invoke(this, args));
    }
}
