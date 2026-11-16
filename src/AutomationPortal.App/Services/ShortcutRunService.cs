using System.Collections.Concurrent;
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

    public ShortcutRunService(IShortcutRunner runner, IShortcutLibrary library, ISettingsStore settings, Func<INotificationService> notifications, IUiThread ui)
    {
        _runner = runner; _library = library; _settings = settings; _notifications = notifications; _ui = ui;
    }

    /// <summary>Raised on the UI thread.</summary>
    public event EventHandler<RunStateChangedEventArgs>? StateChanged;

    /// <summary>True when the app reports this finished run itself (it changed files: a result bar or a toast with Undo), so the plain "finished" toast is left out.</summary>
    public Func<RunResult, bool>? ReportsOwnResult { get; set; }

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
        try
        {
            result = await Task.Run(() => _runner.RunAsync(shortcut, input ?? ContentValue.Empty,
                new RunOptions { Source = source, CancellationToken = cts.Token, PreviewPicks = previewPicks })).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            result = new RunResult
            {
                ShortcutId = shortcut.Id, ShortcutName = shortcut.Name, Source = source, Status = RunStatus.Failed, Started = DateTimeOffset.Now,
                ErrorMessage = "Something went wrong while running this shortcut. " + ex.Message,
            };
        }
        finally
        {
            _running.TryRemove(shortcut.Id, out _);
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
                    state == TileRunState.Failed ? result.ErrorMessage ?? "Something went wrong." : "It ran without problems.");
            }
            catch (ActionException) { }
        }
        return result;
    }

    public Task<RunResult?> RunAsync(Guid shortcutId, ContentValue? input = null, RunSource source = RunSource.Library) =>
        _library.GetById(shortcutId) is { } s ? RunAsync(s, input, source) : Task.FromResult<RunResult?>(null);

    private void Raise(Shortcut s, TileRunState state, RunResult? result, RunSource source)
    {
        var args = new RunStateChangedEventArgs(s.Id, s.Name, state, result, source);
        _ = _ui.InvokeAsync(() => StateChanged?.Invoke(this, args));
    }
}
