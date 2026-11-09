using System.Collections.Concurrent;

namespace AutomationPortal.Actions.Documents;

/// <summary>
/// A dedicated single-threaded apartment (STA) thread for WIC imaging, Shell/Outlook COM and the share sheet.
/// Work is queued and runs one item at a time, never on the app's UI thread.
/// </summary>
public sealed class StaWorker
{
    /// <summary>Imaging (WIC, DrawingVisual, RenderTargetBitmap).</summary>
    public static StaWorker Imaging { get; } = new("Automation Portal imaging");

    /// <summary>COM automation that can block for a while (Outlook, Explorer windows). Kept apart so images never wait for it.</summary>
    public static StaWorker Com { get; } = new("Automation Portal COM");

    private readonly BlockingCollection<Action> _queue = new();
    private readonly string _name;
    private Thread? _thread;
    private readonly object _gate = new();

    public StaWorker(string name) => _name = name;

    public Task<T> RunAsync<T>(Func<T> work, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        EnsureThread();
        _queue.Add(() =>
        {
            if (ct.IsCancellationRequested) { tcs.TrySetCanceled(ct); return; }
            try { tcs.TrySetResult(work()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });
        return tcs.Task;
    }

    public Task RunAsync(Action work, CancellationToken ct = default) =>
        RunAsync<object?>(() => { work(); return null; }, ct);

    private void EnsureThread()
    {
        if (_thread is not null) return;
        lock (_gate)
        {
            if (_thread is not null) return;
            var t = new Thread(() =>
            {
                foreach (var job in _queue.GetConsumingEnumerable()) job();
            })
            { IsBackground = true, Name = _name };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            _thread = t;
        }
    }
}
