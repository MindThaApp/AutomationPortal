using System.Diagnostics;
using System.Text.RegularExpressions;
using Flowmate.Triggers.Native;
using Flowmate.Triggers.Platform;
using Microsoft.Win32;
using Windows.Networking.Connectivity;

namespace Flowmate.Triggers;

/// <summary>Base for shared watchers that only run while somebody is subscribed.</summary>
internal abstract class SharedMonitor : IDisposable
{
    private readonly object _subGate = new();
    private int _subscribers;
    private bool _disposed;

    public IDisposable Subscribe()
    {
        lock (_subGate)
        {
            if (_disposed) return new DisposableAction(() => { });
            if (++_subscribers == 1) SafeStart();
        }
        return new DisposableAction(() =>
        {
            lock (_subGate)
            {
                if (_subscribers > 0 && --_subscribers == 0) SafeStop();
            }
        });
    }

    private void SafeStart() { try { OnFirstSubscriber(); } catch (Exception ex) { TriggerLog.Write($"{GetType().Name} couldn't start: {ex.Message}"); } }
    private void SafeStop() { try { OnLastSubscriber(); } catch (Exception ex) { TriggerLog.Write($"{GetType().Name} couldn't stop: {ex.Message}"); } }

    protected abstract void OnFirstSubscriber();
    protected abstract void OnLastSubscriber();

    protected static void Raise<T>(Action<T>? handler, T arg)
    {
        if (handler is null) return;
        foreach (var d in handler.GetInvocationList().Cast<Action<T>>())
        {
            try { d(arg); } catch (Exception ex) { TriggerLog.Write("Monitor handler failed: " + ex.Message); }
        }
    }

    public void Dispose()
    {
        lock (_subGate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_subscribers > 0) { _subscribers = 0; SafeStop(); }
        }
    }
}

internal sealed record ProcInfo(int Pid, string Name, string? Path);

/// <summary>Watches the list of running processes (snapshot every 1.5 s). WMI process events would need administrator rights.</summary>
internal sealed class ProcessMonitor : SharedMonitor
{
    private Timer? _timer;
    private Dictionary<int, ProcInfo> _known = new();
    private int _busy;
    private readonly object _gate = new();

    public event Action<ProcInfo>? Started;
    public event Action<ProcInfo>? Exited;

    public IReadOnlyList<ProcInfo> Known { get { lock (_gate) return _known.Values.ToList(); } }

    public static IReadOnlyList<ProcInfo> EnumerateAll(bool withPaths)
    {
        var list = new List<ProcInfo>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                list.Add(new ProcInfo(p.Id, p.ProcessName, withPaths ? Win32.GetProcessPath((uint)p.Id) : null));
            }
            catch { }
            finally { p.Dispose(); }
        }
        return list;
    }

    protected override void OnFirstSubscriber()
    {
        lock (_gate) _known = EnumerateAll(true).ToDictionary(p => p.Pid);
        _timer = new Timer(_ => Poll(), null, 1500, 1500);
    }

    protected override void OnLastSubscriber()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Poll()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var now = EnumerateAll(false);
            List<ProcInfo> started = new(), exited = new();
            lock (_gate)
            {
                var nowIds = now.Select(p => p.Pid).ToHashSet();
                foreach (var p in now)
                    if (!_known.ContainsKey(p.Pid))
                        started.Add(p with { Path = Win32.GetProcessPath((uint)p.Pid) });
                foreach (var old in _known.Values)
                    if (!nowIds.Contains(old.Pid)) exited.Add(old);
                foreach (var e in exited) _known.Remove(e.Pid);
                foreach (var s in started) _known[s.Pid] = s;
            }
            foreach (var s in started) Raise(Started, s);
            foreach (var e in exited) Raise(Exited, e);
        }
        catch (Exception ex) { TriggerLog.Write("Process poll failed: " + ex.Message); }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }
}

internal readonly record struct PowerState(bool OnAc, int? BatteryPercent, bool SaverOn, bool HasBattery)
{
    public static PowerState Read()
    {
        if (!Win32.GetSystemPowerStatus(out var s)) return new(true, null, false, false);
        var hasBattery = (s.BatteryFlag & 128) == 0 && s.BatteryFlag != 255;
        int? pct = s.BatteryLifePercent <= 100 ? s.BatteryLifePercent : null;
        return new(s.ACLineStatus != 0, hasBattery ? pct : null, (s.SystemStatusFlag & 1) != 0, hasBattery);
    }
}

/// <summary>AC / battery / battery-saver changes. Windows power notifications trigger an immediate re-read; a 10 s poll is the safety net.</summary>
internal sealed class PowerMonitor : SharedMonitor
{
    private readonly SystemEventHub _hub;
    private Timer? _timer;
    private PowerState _last;
    private readonly object _gate = new();

    public PowerMonitor(SystemEventHub hub) { _hub = hub; }

    public event Action<(PowerState Old, PowerState New)>? Changed;
    public static PowerState Current => PowerState.Read();

    protected override void OnFirstSubscriber()
    {
        _last = PowerState.Read();
        _hub.PowerSettingChanged += Check;
        _timer = new Timer(_ => Check(), null, 10_000, 10_000);
    }

    protected override void OnLastSubscriber()
    {
        _hub.PowerSettingChanged -= Check;
        _timer?.Dispose();
        _timer = null;
    }

    private void Check()
    {
        (PowerState Old, PowerState New)? change = null;
        lock (_gate)
        {
            var now = PowerState.Read();
            if (now != _last) { change = (_last, now); _last = now; }
        }
        if (change is { } c) Raise(Changed, c);
    }
}

internal readonly record struct NetState(bool Online, string? WifiName)
{
    public static NetState Read()
    {
        var online = false;
        string? wifi = null;
        try
        {
            online = NetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
            foreach (var p in NetworkInformation.GetConnectionProfiles())
            {
                if (!p.IsWlanConnectionProfile || p.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.None) continue;
                var ssid = p.WlanConnectionProfileDetails?.GetConnectedSsid();
                // The SSID can be hidden when Windows location access is off; the profile name normally equals it.
                wifi = !string.IsNullOrWhiteSpace(ssid) ? ssid : p.ProfileName;
                break;
            }
        }
        catch (Exception ex) { TriggerLog.Write("Network state read failed: " + ex.Message); }
        return new NetState(online, wifi);
    }
}

/// <summary>Online/offline and Wi-Fi network changes. Online flips are only reported once they last 3 s (Wi-Fi roaming flickers).</summary>
internal sealed class NetworkMonitor : SharedMonitor
{
    private Timer? _timer;
    private NetState _committed;
    private bool? _pendingOnline;
    private DateTime _pendingSince;
    private readonly object _gate = new();

    public event Action<(NetState Old, NetState New)>? Changed;
    public static NetState Current => NetState.Read();

    protected override void OnFirstSubscriber()
    {
        _committed = NetState.Read();
        _pendingOnline = null;
        NetworkInformation.NetworkStatusChanged += OnStatus;
        _timer = new Timer(_ => Check(), null, 3000, 3000);
    }

    protected override void OnLastSubscriber()
    {
        NetworkInformation.NetworkStatusChanged -= OnStatus;
        _timer?.Dispose();
        _timer = null;
    }

    private void OnStatus(object? sender) => Check();

    private void Check()
    {
        (NetState, NetState)? change = null;
        lock (_gate)
        {
            var now = NetState.Read();
            var effective = now;
            if (now.Online != _committed.Online)
            {
                if (_pendingOnline != now.Online) { _pendingOnline = now.Online; _pendingSince = DateTime.UtcNow; }
                if (DateTime.UtcNow - _pendingSince < TimeSpan.FromSeconds(2.5)) effective = now with { Online = _committed.Online };
            }
            else _pendingOnline = null;
            if (effective != _committed) { change = (_committed, effective); _committed = effective; _pendingOnline = null; }
        }
        if (change is { } c) Raise(Changed, c);
    }

    /// <summary>Wi-Fi networks Windows has saved (works without location permission).</summary>
    public static async Task<IReadOnlyList<string>> KnownWifiNamesAsync(CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", "wlan show profiles") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p is null) return Array.Empty<string>();
            var text = await p.StandardOutput.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            return Regex.Matches(text, @"^\s*All User Profile\s*:\s*(.+?)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
                .Select(m => m.Groups[1].Value).Where(s => s.Length > 0).Distinct().OrderBy(s => s).ToList();
        }
        catch { return Array.Empty<string>(); }
    }
}

internal readonly record struct CallState(bool Mic, bool Camera, string? MicApp, string? CameraApp)
{
    public bool Any => Mic || Camera;
    public string? App => MicApp ?? CameraApp;
}

/// <summary>
/// Detects calls by asking Windows which apps are using the microphone or camera right now
/// (CapabilityAccessManager ConsentStore: LastUsedTimeStop == 0 means "in use"). Polls every 2 s; "in use" ends only after 4 s of silence.
/// </summary>
internal sealed class CallMonitor : SharedMonitor
{
    private const string Root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";
    private Timer? _timer;
    private CallState _state;
    private int _micIdle, _camIdle;
    private readonly object _gate = new();

    public event Action<CallState>? Changed;

    public CallState State { get { lock (_gate) return _state; } }

    /// <summary>Reads the registry once (no debounce).</summary>
    public static CallState ReadNow()
    {
        var mic = FindInUse("microphone");
        var cam = FindInUse("webcam");
        return new CallState(mic is not null, cam is not null, mic, cam);
    }

    private static string? FindInUse(string capability)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Root + capability);
            if (key is null) return null;
            foreach (var name in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(name);
                if (sub is null) continue;
                if (name.Equals("NonPackaged", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var app in sub.GetSubKeyNames())
                    {
                        using var appKey = sub.OpenSubKey(app);
                        if (appKey is not null && InUse(appKey))
                        {
                            var path = app.Replace('#', '\\');
                            if (IsOwnProcess(path)) continue;
                            return Path.GetFileNameWithoutExtension(path);
                        }
                    }
                }
                else if (InUse(sub)) return name.Contains('_') ? name[..name.IndexOf('_')] : name;
            }
        }
        catch { }
        return null;
    }

    private static bool IsOwnProcess(string path) =>
        Environment.ProcessPath is { } me && string.Equals(path, me, StringComparison.OrdinalIgnoreCase);

    private static bool InUse(RegistryKey key)
    {
        var start = key.GetValue("LastUsedTimeStart");
        var stop = key.GetValue("LastUsedTimeStop");
        return start is long s && s != 0 && stop is long e && e == 0;
    }

    protected override void OnFirstSubscriber()
    {
        var now = ReadNow();
        lock (_gate) { _state = now; _micIdle = _camIdle = 0; }
        _timer = new Timer(_ => Poll(), null, 2000, 2000);
    }

    protected override void OnLastSubscriber()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Poll()
    {
        CallState? change = null;
        lock (_gate)
        {
            var raw = ReadNow();
            var mic = Debounce(raw.Mic, _state.Mic, ref _micIdle);
            var cam = Debounce(raw.Camera, _state.Camera, ref _camIdle);
            var next = new CallState(mic, cam, raw.MicApp ?? (mic ? _state.MicApp : null), raw.CameraApp ?? (cam ? _state.CameraApp : null));
            if (next != _state) { change = next; _state = next; }
        }
        if (change is { } c) Raise(Changed, c);
    }

    private static bool Debounce(bool raw, bool stable, ref int idle)
    {
        if (raw) { idle = 0; return true; }
        if (!stable) return false;
        return ++idle < 2;
    }
}

internal sealed record DriveEvent(string Root, string? Label, bool Added);

/// <summary>Plugged-in / removed drives, found by comparing drive letters on every Windows device notification and every 3 s.</summary>
internal sealed class DriveMonitor : SharedMonitor
{
    private readonly SystemEventHub _hub;
    private Timer? _timer;
    private HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public DriveMonitor(SystemEventHub hub) { _hub = hub; }

    public event Action<DriveEvent>? Changed;

    private static HashSet<string> Letters() =>
        DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Removable or DriveType.Fixed).Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Drives that can be unplugged: removable drives plus any non-system fixed disk (USB hard drives show up as fixed).</summary>
    public static IReadOnlyList<(string Root, string Label)> CurrentDrives() =>
        DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Removable or DriveType.Fixed)
            .Select(d => (d.Name, Label(d))).ToList();

    private static string Label(DriveInfo d)
    {
        try { return d.IsReady ? d.VolumeLabel : ""; } catch { return ""; }
    }

    protected override void OnFirstSubscriber()
    {
        lock (_gate) _known = Letters();
        _hub.DeviceChanged += Check;
        _timer = new Timer(_ => Check(), null, 3000, 3000);
    }

    protected override void OnLastSubscriber()
    {
        _hub.DeviceChanged -= Check;
        _timer?.Dispose();
        _timer = null;
    }

    private void Check()
    {
        var events = new List<DriveEvent>();
        lock (_gate)
        {
            HashSet<string> now;
            try { now = Letters(); } catch { return; }
            foreach (var r in now.Where(r => !_known.Contains(r)))
            {
                string? label = null;
                try { var d = new DriveInfo(r); label = d.IsReady ? d.VolumeLabel : null; } catch { }
                events.Add(new DriveEvent(r, label, true));
            }
            events.AddRange(_known.Where(r => !now.Contains(r)).Select(r => new DriveEvent(r, null, false)));
            _known = now;
        }
        foreach (var e in events) Raise(Changed, e);
    }
}

/// <summary>Monitor count changes (plugging in / unplugging a screen).</summary>
internal sealed class DisplayMonitor : SharedMonitor
{
    private readonly SystemEventHub _hub;
    private Timer? _timer;
    private int _count;
    private readonly object _gate = new();

    public DisplayMonitor(SystemEventHub hub) { _hub = hub; }

    public event Action<(int Old, int New)>? Changed;
    public static int CurrentCount => Win32.GetSystemMetrics(Win32.SM_CMONITORS);

    protected override void OnFirstSubscriber()
    {
        _count = CurrentCount;
        _hub.DisplayChanged += Check;
        _timer = new Timer(_ => Check(), null, 3000, 3000);
    }

    protected override void OnLastSubscriber()
    {
        _hub.DisplayChanged -= Check;
        _timer?.Dispose();
        _timer = null;
    }

    private void Check()
    {
        (int, int)? change = null;
        lock (_gate)
        {
            var now = CurrentCount;
            if (now != _count) { change = (_count, now); _count = now; }
        }
        if (change is { } c) Raise(Changed, c);
    }
}
