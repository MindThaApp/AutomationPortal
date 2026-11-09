using System.Diagnostics;
using AutomationPortal.Core;

namespace AutomationPortal.Triggers;

public abstract class CallTriggerBase : TriggerProvider
{
    protected static ParameterDescriptor SourceParam() =>
        ParameterDescriptor.ChoiceOf("source", "Count a call when", "any",
            new Choice("any", "the microphone or camera is in use"), new Choice("mic", "the microphone is in use"), new Choice("camera", "the camera is in use"));

    protected abstract bool FireWhenStarting { get; }

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var env = TP.Env;
        var source = config.GetChoice("source", "any");
        bool Active(CallState s) => source switch { "mic" => s.Mic, "camera" => s.Camera, _ => s.Any };
        string? lastApp = null;
        var was = Active(env.Calls.State);
        void OnChanged(CallState s)
        {
            var now = Active(s);
            var app = source == "camera" ? s.CameraApp : source == "mic" ? s.MicApp : s.App;
            if (app is not null) lastApp = app;
            if (now == was) return;
            was = now;
            if (now != FireWhenStarting) return;
            var name = app ?? lastApp;
            var output = name is null ? ContentValue.Text("a call") : ContentValue.From(new AppItem(name, name));
            sink.Fire(output, FireWhenStarting ? $"A call started{(name is null ? "" : " in " + name)}" : "The call ended");
        }
        return TP.Watch(env.Calls, () => env.Calls.Changed += OnChanged, () => env.Calls.Changed -= OnChanged);
    }
}

public sealed class CallStartedTrigger : CallTriggerBase
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "call.started", Title = "When I'm in a call", Category = TP.CatCalls, Icon = "Call24",
        Summary = "When a call starts ({source})",
        Description = "Runs when an app starts using your microphone or camera, like Teams, Zoom or Meet. Windows keeps track of this, so no extra setup is needed.",
        OutputKind = ContentKind.App, OutputDescription = "The app that is using the microphone or camera",
        Parameters = new[] { SourceParam() },
    };
    public override TriggerDescriptor Descriptor => D;
    protected override bool FireWhenStarting => true;
}

public sealed class CallEndedTrigger : CallTriggerBase
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "call.ended", Title = "When a call ends", Category = TP.CatCalls, Icon = "CallEnd24",
        Summary = "When a call ends ({source})",
        Description = "Runs a few seconds after the last app stops using your microphone or camera.",
        OutputKind = ContentKind.App, OutputDescription = "The app that was using the microphone or camera",
        Parameters = new[] { SourceParam() },
    };
    public override TriggerDescriptor Descriptor => D;
    protected override bool FireWhenStarting => false;
}

public sealed class ScreenshotTakenTrigger : TriggerProvider
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
    private static readonly TriggerDescriptor D = new()
    {
        Id = "screen.screenshotTaken", Title = "When I take a screenshot", Category = TP.CatCalls, Icon = "ScreenCut20",
        Summary = "When I take a screenshot",
        Description = "Runs when a new picture appears in your Screenshots folder (Win + Print Screen), and optionally when you copy a snip with Snipping Tool (Win + Shift + S).",
        OutputKind = ContentKind.Image, OutputDescription = "The screenshot picture",
        Parameters = new[]
        {
            ParameterDescriptor.Bool("snips", "Also when I copy a snip with Snipping Tool", true),
        },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var env = TP.Env;
        var holder = new CompositeDisposable();
        var folder = Native.Win32.ScreenshotsFolder();
        long lastFileTicks = 0;

        var batcher = new FolderChangeBatcher(TimeSpan.FromSeconds(1), new DiskFileProbe(),
            p => ImageExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase));
        FileSystemWatcher? watcher = null;
        var gate = new object();

        void Attach()
        {
            lock (gate)
            {
                if (watcher is not null || !Directory.Exists(folder)) return;
                var w = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite, InternalBufferSize = 32 * 1024 };
                w.Created += (_, e) => batcher.OnCreated(e.FullPath);
                w.Renamed += (_, e) => batcher.OnRenamed(e.OldFullPath, e.FullPath);
                w.EnableRaisingEvents = true;
                watcher = w;
            }
        }

        var timer = new Timer(_ =>
        {
            try
            {
                if (watcher is null) Attach();
                if (batcher.Poll() is { } batch && batch.Added.Count > 0)
                {
                    Interlocked.Exchange(ref lastFileTicks, DateTime.UtcNow.Ticks);
                    sink.Fire(ContentValue.Images(batch.Added), batch.Added.Count == 1 ? "A screenshot was saved" : $"{batch.Added.Count} screenshots were saved");
                }
            }
            catch (Exception ex) { TriggerLog.Write("Screenshot watcher: " + ex.Message); }
        }, null, 500, 500);
        holder.Add(timer);
        holder.Add(new DisposableAction(() => { lock (gate) { watcher?.Dispose(); watcher = null; } }));

        if (config.GetBool("snips", true))
        {
            void OnClip(ClipboardSnapshot snap)
            {
                if (snap.ImagePath is null || !IsSnippingTool(snap.OwnerProcessId)) return;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(2500); // Snipping Tool may also save the file; let the folder watcher report that one
                    if (DateTime.UtcNow.Ticks - Interlocked.Read(ref lastFileTicks) < TimeSpan.FromSeconds(8).Ticks) return;
                    sink.Fire(ContentValue.Image(snap.ImagePath), "A snip was copied");
                });
            }
            holder.Add(TP.Watch(env.Clipboard, () => env.Clipboard.Changed += OnClip, () => env.Clipboard.Changed -= OnClip));
        }
        return holder;
    }

    private static bool IsSnippingTool(uint pid)
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            var n = p.ProcessName;
            return n.Contains("Snip", StringComparison.OrdinalIgnoreCase) || n.Contains("ScreenClip", StringComparison.OrdinalIgnoreCase) || n.Contains("ScreenSketch", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
