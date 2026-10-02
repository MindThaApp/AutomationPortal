using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace Flowmate.Triggers;

internal sealed record DeviceEvent(string Name, bool Connected);

/// <summary>
/// Watches a set of Windows devices through a DeviceWatcher and reports which ones connect or disconnect after the first
/// enumeration (devices that were already connected when watching starts are not reported).
/// </summary>
internal sealed class DeviceWatch : IDisposable
{
    private readonly List<DeviceWatcher> _watchers = new();
    private readonly Dictionary<string, string> _names = new();
    private readonly HashSet<DeviceWatcher> _completed = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTime> _recent = new();
    private readonly Func<DeviceInformation, string?> _nameOf;
    private bool _disposed;

    public event Action<DeviceEvent>? Changed;

    public DeviceWatch(IEnumerable<(string Selector, DeviceInformationKind Kind)> selectors, Func<DeviceInformation, string?>? nameOf = null)
    {
        _nameOf = nameOf ?? (i => i.Name);
        foreach (var (selector, kind) in selectors)
        {
            DeviceWatcher w;
            try { w = DeviceInformation.CreateWatcher(selector, null, kind); }
            catch (Exception ex) { TriggerLog.Write("Couldn't create device watcher: " + ex.Message); continue; }
            w.Added += (s, info) => OnAdded((DeviceWatcher)s, info);
            w.Removed += (s, update) => OnRemoved(update.Id);
            w.EnumerationCompleted += (s, _) => { lock (_gate) _completed.Add((DeviceWatcher)s); };
            _watchers.Add(w);
        }
    }

    public void Start()
    {
        foreach (var w in _watchers) { try { w.Start(); } catch (Exception ex) { TriggerLog.Write("Device watcher didn't start: " + ex.Message); } }
    }

    private void OnAdded(DeviceWatcher watcher, DeviceInformation info)
    {
        var name = _nameOf(info);
        if (string.IsNullOrWhiteSpace(name)) return;
        bool live;
        lock (_gate) { _names[info.Id] = name; live = _completed.Contains(watcher) && !_disposed; }
        if (live) Report(new DeviceEvent(name, true));
    }

    private void OnRemoved(string id)
    {
        string? name;
        bool live;
        lock (_gate) { live = !_disposed; _names.Remove(id, out name); }
        if (name is not null && live) Report(new DeviceEvent(name, false));
    }

    private void Report(DeviceEvent e)
    {
        lock (_gate)
        {
            // Classic and low-energy Bluetooth, or several USB interfaces, can report the same device twice.
            var key = (e.Connected ? "+" : "-") + e.Name;
            if (_recent.TryGetValue(key, out var at) && DateTime.UtcNow - at < TimeSpan.FromSeconds(4)) return;
            _recent[key] = DateTime.UtcNow;
        }
        try { Changed?.Invoke(e); } catch (Exception ex) { TriggerLog.Write("Device handler failed: " + ex.Message); }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        foreach (var w in _watchers)
        {
            try
            {
                if (w.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) w.Stop();
            }
            catch { }
        }
        _watchers.Clear();
    }

    // ---- ready-made watchers and pick-lists --------------------------------------------------

    public static DeviceWatch Bluetooth() => new(new[]
    {
        (BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected), DeviceInformationKind.Unknown),
        (BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected), DeviceInformationKind.Unknown),
    });

    private const string UsbInterfaceClass = "{a5dcbf10-6530-11d2-901f-00c04fb951ed}";
    public static readonly string UsbSelector = $"System.Devices.InterfaceClassGuid:=\"{UsbInterfaceClass}\" AND System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True";

    public static DeviceWatch Usb() => new(new[] { (UsbSelector, DeviceInformationKind.DeviceInterface) }, i => UsbFriendlyName(i.Name));

    /// <summary>Hubs and root controllers are plumbing, not devices the user would name.</summary>
    internal static string? UsbFriendlyName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (name.Contains("Hub", StringComparison.OrdinalIgnoreCase) || name.Contains("Root", StringComparison.OrdinalIgnoreCase)) return null;
        return name.Trim();
    }

    public static async Task<IReadOnlyList<string>> PairedBluetoothNamesAsync(CancellationToken ct)
    {
        try
        {
            var found = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true)).AsTask(ct);
            var le = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)).AsTask(ct);
            return found.Concat(le).Select(d => d.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n).ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    public static async Task<IReadOnlyList<string>> UsbNamesAsync(CancellationToken ct)
    {
        try
        {
            var found = await DeviceInformation.FindAllAsync(UsbSelector, null, DeviceInformationKind.DeviceInterface).AsTask(ct);
            return found.Select(d => UsbFriendlyName(d.Name)).Where(n => n is not null).Select(n => n!).Distinct().OrderBy(n => n).ToList();
        }
        catch { return Array.Empty<string>(); }
    }
}

/// <summary>Sound-output changes through Windows Core Audio: the default speaker/headphones changed, or an output was plugged in.</summary>
internal sealed class AudioOutputMonitor : SharedMonitor
{
    private MMDeviceEnumerator? _enumerator;
    private Client? _client;
    private readonly Dictionary<string, DateTime> _recent = new();
    private readonly object _gate = new();

    public event Action<string>? OutputChanged;

    private sealed class Client : IMMNotificationClient
    {
        private readonly AudioOutputMonitor _owner;
        public Client(AudioOutputMonitor owner) { _owner = owner; }
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia) _owner.Report(defaultDeviceId);
        }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            if (newState == DeviceState.Active) _owner.Report(deviceId, mustBeRender: true);
        }
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }

    private void Report(string deviceId, bool mustBeRender = false)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(deviceId);
            if (mustBeRender && device.DataFlow != DataFlow.Render) return;
            var name = device.FriendlyName;
            lock (_gate)
            {
                if (_recent.TryGetValue(name, out var at) && DateTime.UtcNow - at < TimeSpan.FromSeconds(3)) return;
                _recent[name] = DateTime.UtcNow;
            }
            Raise(OutputChanged, name);
        }
        catch (Exception ex) { TriggerLog.Write("Audio change not read: " + ex.Message); }
    }

    protected override void OnFirstSubscriber()
    {
        _enumerator = new MMDeviceEnumerator();
        _client = new Client(this);
        _enumerator.RegisterEndpointNotificationCallback(_client);
    }

    protected override void OnLastSubscriber()
    {
        if (_enumerator is not null && _client is not null) _enumerator.UnregisterEndpointNotificationCallback(_client);
        _enumerator?.Dispose();
        _enumerator = null;
        _client = null;
    }

    public static IReadOnlyList<string> OutputNames()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).Select(d => d.FriendlyName).Distinct().OrderBy(n => n).ToList();
        }
        catch { return Array.Empty<string>(); }
    }
}
