using System.Runtime.CompilerServices;
using Flowmate.Core;
using Flowmate.Triggers.Platform;

[assembly: InternalsVisibleTo("Flowmate.Triggers.Tests")]

namespace Flowmate.Triggers;

/// <summary>Tiny file logger (<c>%LOCALAPPDATA%\Flowmate\logs\triggers.log</c>). Never throws.</summary>
public static class TriggerLog
{
    private static readonly object Gate = new();
    public static string? FilePath { get; set; }

    public static void Write(string message)
    {
        System.Diagnostics.Debug.WriteLine("[Flowmate.Triggers] " + message);
        var path = FilePath;
        if (path is null) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 512 * 1024) File.Delete(path);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}

/// <summary>
/// Everything trigger providers share (system event window, process poller, power state …). Providers are created by
/// assembly scanning with parameterless constructors, so they reach their host services through <see cref="Current"/>,
/// which <see cref="TriggerHost"/> sets.
/// </summary>
public sealed class TriggerEnvironment
{
    internal TriggerEnvironment(FlowmateServices services, SystemEventHub hub)
    {
        Services = services;
        Hub = hub;
        StateStore = new TriggerStateStore(Path.Combine(services.Paths.LocalRoot, "trigger-state"));
        Processes = new ProcessMonitor();
        Power = new PowerMonitor(hub);
        Network = new NetworkMonitor();
        Calls = new CallMonitor();
        Location = new LocationProvider(services, StateStore);
        Drives = new DriveMonitor(hub);
        Displays = new DisplayMonitor(hub);
        Clipboard = new ClipboardWatcher(hub);
    }

    private static TriggerEnvironment? _current;
    internal static TriggerEnvironment Current
    {
        get => _current ?? throw new InvalidOperationException("Flow Mate's trigger system isn't running yet.");
        set => _current = value;
    }
    internal static bool IsAvailable => _current is not null;
    internal static void Clear(TriggerEnvironment env) { if (ReferenceEquals(_current, env)) _current = null; }

    public FlowmateServices Services { get; }
    internal SystemEventHub Hub { get; }
    internal HotkeyService? Hotkeys { get; set; }
    internal TypedPhraseService? TypedPhrases { get; set; }
    internal TriggerStateStore StateStore { get; }
    internal ProcessMonitor Processes { get; }
    internal PowerMonitor Power { get; }
    internal NetworkMonitor Network { get; }
    internal CallMonitor Calls { get; }
    internal LocationProvider Location { get; }
    internal DriveMonitor Drives { get; }
    internal DisplayMonitor Displays { get; }
    internal ClipboardWatcher Clipboard { get; }
    /// <summary>True while the engine does its very first load (so "When I sign in / Flowmate starts" fires once per launch).</summary>
    public bool IsInitialLoad { get; internal set; }

    internal void Stop()
    {
        Processes.Dispose();
        Power.Dispose();
        Network.Dispose();
        Calls.Dispose();
        Drives.Dispose();
        Displays.Dispose();
        Clipboard.Dispose();
    }
}

/// <summary>Optional extras a trigger sink offers. The engine's sinks implement both.</summary>
internal interface ITriggerSinkExtras
{
    /// <summary>Shows or clears a problem message on the automation (for example "Couldn't read that feed").</summary>
    void ReportProblem(string? message);
    /// <summary>Fires and waits for the shortcut's output (null when the shortcut didn't run or has no output). Used by text expansion.</summary>
    Task<ContentValue?> FireAndWaitAsync(ContentValue output, string? description);
}

internal static class TriggerSinkExtensions
{
    public static void ReportProblem(this ITriggerSink sink, string? message) => (sink as ITriggerSinkExtras)?.ReportProblem(message);
}

/// <summary>Marker for triggers that should switch the automation off after it ran once ("On a date & time").</summary>
public interface IOneShotTrigger { }

/// <summary>Disposes a list of actions together.</summary>
internal sealed class DisposableAction : IDisposable
{
    private Action? _action;
    public DisposableAction(Action action) { _action = action; }
    public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
}

internal sealed class CompositeDisposable : IDisposable
{
    private readonly List<IDisposable> _items = new();
    private bool _disposed;

    /// <summary>Adds an item; when the group is already disposed the item is disposed straight away.</summary>
    public void Add(IDisposable d)
    {
        lock (_items)
        {
            if (!_disposed) { _items.Add(d); return; }
        }
        try { d.Dispose(); } catch { }
    }

    public void Dispose()
    {
        List<IDisposable> copy;
        lock (_items) { _disposed = true; copy = _items.ToList(); _items.Clear(); }
        foreach (var d in copy) { try { d.Dispose(); } catch { } }
    }
}

/// <summary>Small JSON blobs under <c>%LOCALAPPDATA%\Flowmate\trigger-state\</c> (seen RSS items, page hashes, cached location …).</summary>
internal sealed class TriggerStateStore
{
    private readonly string _folder;
    private readonly object _gate = new();
    public TriggerStateStore(string folder) { _folder = folder; }
    public string Folder => _folder;

    private string FileFor(string key)
    {
        var safe = string.Concat(key.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(_folder, safe + ".json");
    }

    public T? Load<T>(string key) where T : class
    {
        lock (_gate)
        {
            try
            {
                var f = FileFor(key);
                return File.Exists(f) ? FlowmateJson.Deserialize<T>(File.ReadAllText(f)) : null;
            }
            catch { return null; }
        }
    }

    public void Save<T>(string key, T value)
    {
        lock (_gate)
        {
            try { AtomicFile.WriteAllText(FileFor(key), FlowmateJson.Serialize(value, indented: false)); }
            catch (Exception ex) { TriggerLog.Write($"Couldn't save state '{key}': {ex.Message}"); }
        }
    }

    public void Delete(string key)
    {
        lock (_gate) { try { File.Delete(FileFor(key)); } catch { } }
    }
}
