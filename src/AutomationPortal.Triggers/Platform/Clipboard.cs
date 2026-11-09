using System.Collections.Concurrent;
using AutomationPortal.Core;
using AutomationPortal.Triggers.Native;
using AutomationPortal.Triggers.Platform;

namespace AutomationPortal.Triggers;

/// <summary>One long-lived STA thread for clipboard work (OLE clipboard APIs need STA; the hub and hook threads must not block).</summary>
internal static class StaWorker
{
    private static readonly BlockingCollection<Action> Queue = new();
    private static Thread? _thread;
    private static readonly object Gate = new();

    public static Task<T> Run<T>(Func<T> func)
    {
        lock (Gate)
        {
            if (_thread is null)
            {
                _thread = new Thread(() => { foreach (var a in Queue.GetConsumingEnumerable()) a(); }) { IsBackground = true, Name = "AutomationPortal.Sta" };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
            }
        }
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Add(() =>
        {
            try { tcs.SetResult(func()); } catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }
}

/// <summary>What was on the clipboard. <see cref="Excluded"/>: marked as not for clipboard monitors (password managers, Automation Portal's own pastes).</summary>
public sealed record ClipboardSnapshot(string? Text, IReadOnlyList<string> Files, string? ImagePath, uint OwnerProcessId, bool Excluded);

/// <summary>
/// Clipboard access for the copy trigger and text expansion, supplied by the host (see <see cref="TriggerClipboard.Backend"/>).
/// Calls arrive one at a time on a dedicated STA thread. Retrying while another app holds the clipboard is up to the backend.
/// </summary>
public interface ITriggerClipboard
{
    /// <summary>Files, else text, else (when <paramref name="readImage"/>) a picture saved as PNG into <paramref name="imageFolder"/>. <c>OwnerProcessId</c> is filled in by the caller.</summary>
    ClipboardSnapshot Read(string imageFolder, bool readImage);
    string? ReadText();
    /// <summary>Puts text on the clipboard, hidden from clipboard history and monitors, and returns an opaque copy of what was there.</summary>
    object? SetTextTemporarily(string text);
    /// <summary>Puts back what <see cref="SetTextTemporarily"/> returned (clears the clipboard when null).</summary>
    void Restore(object? previous);
}

public static class TriggerClipboard
{
    /// <summary>Process-wide. Without one, the copy trigger stays quiet and long expansions are typed instead of pasted.</summary>
    public static ITriggerClipboard? Backend { get; set; }
}

internal static class ClipboardAccess
{
    private static ITriggerClipboard Backend => TriggerClipboard.Backend ?? throw AutomationPortalServices.NotAvailable("use the clipboard");

    public static Task<ClipboardSnapshot> ReadAsync(string imageFolder, bool readImage = true) => StaWorker.Run(() =>
    {
        uint ownerPid = 0;
        var owner = Win32.GetClipboardOwner();
        if (owner != IntPtr.Zero) Win32.GetWindowThreadProcessId(owner, out ownerPid);
        return Backend.Read(imageFolder, readImage) with { OwnerProcessId = ownerPid };
    });

    public static Task<string?> ReadTextAsync() => StaWorker.Run(() => Backend.ReadText());

    public static Task<object?> SetTextTemporarilyAsync(string text) => StaWorker.Run(() => Backend.SetTextTemporarily(text));

    public static Task RestoreAsync(object? previous) => StaWorker.Run(() => { Backend.Restore(previous); return 0; });
}

/// <summary>Raises <see cref="Changed"/> when something new is copied (debounced). Copies made by Automation Portal itself and by password managers are skipped.</summary>
internal sealed class ClipboardWatcher : SharedMonitor
{
    private readonly SystemEventHub _hub;
    private CancellationTokenSource? _pending;
    private readonly string _imageFolder = Path.Combine(Path.GetTempPath(), "AutomationPortal", "clipboard");
    private readonly object _gate = new();

    public ClipboardWatcher(SystemEventHub hub) { _hub = hub; }

    public event Action<ClipboardSnapshot>? Changed;
    public bool IncludeImages { get; set; } = true;

    protected override void OnFirstSubscriber()
    {
        if (TriggerClipboard.Backend is null) { TriggerLog.Write("Clipboard trigger: this app has no clipboard access, so copies aren't watched."); return; }
        _hub.ClipboardChanged += OnChanged;
        _ = Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(_imageFolder)) return;
                foreach (var f in Directory.EnumerateFiles(_imageFolder, "clipboard-*.png"))
                    if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-1)) File.Delete(f);
            }
            catch { }
        });
    }
    protected override void OnLastSubscriber() { _hub.ClipboardChanged -= OnChanged; lock (_gate) { _pending?.Cancel(); _pending = null; } }

    private void OnChanged()
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            _pending?.Cancel();
            _pending = cts = new CancellationTokenSource();
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, cts.Token);
                var snap = await ClipboardAccess.ReadAsync(_imageFolder, IncludeImages);
                if (snap.Excluded || snap.OwnerProcessId == (uint)Environment.ProcessId) return;
                if (snap.Text is null && snap.Files.Count == 0 && snap.ImagePath is null) return;
                Raise(Changed, snap);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { TriggerLog.Write("Clipboard read failed: " + ex.Message); }
        });
    }
}
