using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using Flowmate.Core;
using Flowmate.Triggers.Native;
using Flowmate.Triggers.Platform;

namespace Flowmate.Triggers;

/// <summary>One long-lived STA thread for clipboard work (the WPF clipboard needs STA; the hub and hook threads must not block).</summary>
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
                _thread = new Thread(() => { foreach (var a in Queue.GetConsumingEnumerable()) a(); }) { IsBackground = true, Name = "Flowmate.Sta" };
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

internal sealed record ClipboardSnapshot(string? Text, IReadOnlyList<string> Files, string? ImagePath, uint OwnerProcessId, bool Excluded);

internal static class ClipboardAccess
{
    private static T Retry<T>(Func<T> f, T fallback)
    {
        for (var i = 0; i < 6; i++)
        {
            try { return f(); }
            catch (COMException) { Thread.Sleep(40); }
            catch (ExternalException) { Thread.Sleep(40); }
        }
        return fallback;
    }

    /// <summary>Reads what is on the clipboard. Images are saved as PNG files into <paramref name="imageFolder"/>.</summary>
    public static Task<ClipboardSnapshot> ReadAsync(string imageFolder, bool readImage = true) => StaWorker.Run(() => Read(imageFolder, readImage));

    private static ClipboardSnapshot Read(string imageFolder, bool readImage)
    {
        uint ownerPid = 0;
        var owner = Win32.GetClipboardOwner();
        if (owner != IntPtr.Zero) Win32.GetWindowThreadProcessId(owner, out ownerPid);
        var excluded = Retry(() => Clipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing"), false);
        if (excluded) return new ClipboardSnapshot(null, Array.Empty<string>(), null, ownerPid, true);

        var files = Retry(() => Clipboard.ContainsFileDropList() ? Clipboard.GetFileDropList().Cast<string>().ToList() : new List<string>(), new List<string>());
        string? text = null;
        if (files.Count == 0) text = Retry(() => Clipboard.ContainsText() ? Clipboard.GetText() : null, null);
        string? image = null;
        if (readImage && files.Count == 0 && text is null)
        {
            image = Retry(() =>
            {
                if (!Clipboard.ContainsImage()) return null;
                var bmp = Clipboard.GetImage();
                if (bmp is null) return null;
                Directory.CreateDirectory(imageFolder);
                var path = Path.Combine(imageFolder, $"clipboard-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using var fs = File.Create(path);
                enc.Save(fs);
                return path;
            }, null);
        }
        return new ClipboardSnapshot(text, files, image, ownerPid, false);
    }

    public static Task<bool> HasImageAsync() => StaWorker.Run(() => Retry(Clipboard.ContainsImage, false));

    public static Task<string?> ReadTextAsync() => StaWorker.Run(() => Retry(() => Clipboard.ContainsText() ? Clipboard.GetText() : null, null));

    /// <summary>Puts text on the clipboard (hidden from clipboard history and from Flowmate's own copy trigger) and returns a snapshot to restore later.</summary>
    public static Task<IDataObject?> SetTextTemporarilyAsync(string text) => StaWorker.Run(() =>
    {
        var previous = Retry(Snapshot, null);
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(BitConverter.GetBytes(1)));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
        Retry(() => { Clipboard.SetDataObject(data, true); return true; }, false);
        return previous;
    });

    public static Task RestoreAsync(IDataObject? previous) => StaWorker.Run(() =>
    {
        Retry(() =>
        {
            if (previous is null) Clipboard.Clear(); else Clipboard.SetDataObject(previous, true);
            return true;
        }, false);
        return 0;
    });

    private static IDataObject? Snapshot()
    {
        var current = Clipboard.GetDataObject();
        if (current is null) return null;
        var copy = new DataObject();
        var any = false;
        foreach (var format in current.GetFormats(false))
        {
            try
            {
                var data = current.GetData(format, false);
                if (data is null) continue;
                copy.SetData(format, data, false);
                any = true;
            }
            catch { }
        }
        return any ? copy : null;
    }
}

/// <summary>Raises <see cref="Changed"/> when something new is copied (debounced). Copies made by Flowmate itself and by password managers are skipped.</summary>
internal sealed class ClipboardWatcher : SharedMonitor
{
    private readonly SystemEventHub _hub;
    private CancellationTokenSource? _pending;
    private readonly string _imageFolder = Path.Combine(Path.GetTempPath(), "Flowmate", "clipboard");
    private readonly object _gate = new();

    public ClipboardWatcher(SystemEventHub hub) { _hub = hub; }

    public event Action<ClipboardSnapshot>? Changed;
    public bool IncludeImages { get; set; } = true;

    protected override void OnFirstSubscriber() => _hub.ClipboardChanged += OnChanged;
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
