using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;

namespace AutomationPortal.Actions.Windows.Devices;

/// <summary>Wi-Fi, Bluetooth and network information.</summary>
public static class NetworkService
{
    // ---- radios ------------------------------------------------------------------------------

    /// <summary>Turns a radio on/off/toggles it. Returns whether it is on afterwards.</summary>
    internal static async Task<bool> SetRadioAsync(RadioKind kind, string state)
    {
        var name = kind == RadioKind.WiFi ? "Wi-Fi" : "Bluetooth";
        var access = await Radio.RequestAccessAsync();
        if (access != RadioAccessStatus.Allowed)
            throw new ActionException(Strings.Format("NetworkService_Error_Windows_Didn_T_Let_Automation_Portal", name, name));
        var radios = (await Radio.GetRadiosAsync()).Where(r => r.Kind == kind).ToList();
        if (radios.Count == 0) throw new ActionException(Strings.Format("NetworkService_Error_This_PC_Doesn_T_Have_0", name));
        var target = Toggle.Resolve(state, radios[0].State == RadioState.On);
        foreach (var radio in radios)
        {
            var result = await radio.SetStateAsync(target ? RadioState.On : RadioState.Off);
            if (result != RadioAccessStatus.Allowed)
                throw new ActionException(Strings.Format(target ? "NetworkService_Error_CouldNotTurnOn" : "NetworkService_Error_CouldNotTurnOff", name));
        }
        return target;
    }

    // ---- saved Wi-Fi networks ----------------------------------------------------------------

    /// <summary>Reads profile names from the output of <c>netsh wlan show profiles</c> (any language).</summary>
    public static IReadOnlyList<string> ParseProfiles(string netshOutput)
    {
        var names = new List<string>();
        foreach (var line in netshOutput.Split('\n'))
        {
            var m = Regex.Match(line.TrimEnd('\r'), @"^\s{2,}\S[^:]*\s:\s(.+)$");
            if (m.Success)
            {
                var name = m.Groups[1].Value.Trim();
                if (name.Length > 0 && !names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
            }
        }
        return names;
    }

    public static async Task<IReadOnlyList<string>> GetSavedWifiNamesAsync(CancellationToken ct)
    {
        try
        {
            var (exitCode, output) = await ProcessRunner.RunRawAsync("netsh.exe", "wlan show profiles", TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
            return exitCode == 0 ? ParseProfiles(ConsoleText.Decode(output)) : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return Array.Empty<string>(); }
    }

    internal static async Task<string> JoinWifiAsync(string profile, CancellationToken ct)
    {
        var r = await ProcessRunner.RunAsync("netsh.exe", $"wlan connect name=\"{profile.Replace("\"", "")}\"", TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
        if (r.ExitCode != 0)
            throw new ActionException(Strings.Format("NetworkService_Error_Windows_Couldn_T_Connect_To_0", profile, FirstLine(r.Output)).Trim());
        // The request returns before the connection is up; wait a little for the link to come up.
        var until = DateTime.UtcNow.AddSeconds(15);
        await Task.Delay(1500, ct).ConfigureAwait(false);
        while (DateTime.UtcNow < until)
        {
            var current = await CurrentWifiNameAsync(ct).ConfigureAwait(false);
            if (current is null ? WifiLinkIsUp() : current.Equals(profile, StringComparison.OrdinalIgnoreCase)) return profile;
            await Task.Delay(750, ct).ConfigureAwait(false);
        }
        throw new ActionException(Strings.Format("NetworkService_Error_Asked_Windows_To_Connect_To", profile));
    }

    private static string FirstLine(string text) => text.Split('\n').FirstOrDefault()?.Trim() ?? "";

    /// <summary>The connected Wi-Fi network's name, or null when it can't be read.</summary>
    internal static async Task<string?> CurrentWifiNameAsync(CancellationToken ct)
    {
        try
        {
            var p = NetworkInformation.GetInternetConnectionProfile();
            if (p is { IsWlanConnectionProfile: true })
            {
                var ssid = p.WlanConnectionProfileDetails?.GetConnectedSsid();
                if (!string.IsNullOrWhiteSpace(ssid)) return ssid;
                if (!string.IsNullOrWhiteSpace(p.ProfileName)) return p.ProfileName;
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { }
        await Task.CompletedTask;
        return null;
    }

    private static bool WifiLinkIsUp() =>
        NetworkInterface.GetAllNetworkInterfaces().Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && n.OperationalStatus == OperationalStatus.Up);

    // ---- network details ---------------------------------------------------------------------

    public sealed record NetworkDetails(bool Connected, bool Internet, string Type, string Name, int? SignalPercent,
        string? IpAddress, string? Gateway, string? Dns, long? SpeedMbps, string? Mac, string? Adapter, string? Note);

    public static NetworkDetails GetDetails()
    {
        ConnectionProfile? profile = null;
        try { profile = NetworkInformation.GetInternetConnectionProfile(); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { }
        if (profile is null)
        {
            var offline = NetworkInterface.GetAllNetworkInterfaces().Any(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback);
            return new NetworkDetails(offline, false, offline ? "Network" : "None", "", null, null, null, null, null, null, null, offline ? Strings.Get("NetworkService_Note_NoInternet") : null);
        }

        var internet = profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
        var isWifi = profile.IsWlanConnectionProfile;
        var type = isWifi ? "Wi-Fi" : profile.IsWwanConnectionProfile ? "Mobile data" : "Ethernet";
        string name = profile.ProfileName ?? "";
        string? note = null;
        if (isWifi)
        {
            string? ssid = null;
            try { ssid = profile.WlanConnectionProfileDetails?.GetConnectedSsid(); } catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { }
            if (!string.IsNullOrWhiteSpace(ssid)) name = ssid;
            else if (string.IsNullOrWhiteSpace(name))
                note = Strings.Get("NetworkService_Note_WifiNameNeedsLocation");
        }
        int? signal = null;
        try { if (profile.GetSignalBars() is { } bars) signal = bars * 20; } catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { }

        NetworkInterface? nic = null;
        var adapterId = profile.NetworkAdapter?.NetworkAdapterId;
        if (adapterId is { } id)
            nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Id.Trim('{', '}').Equals(id.ToString(), StringComparison.OrdinalIgnoreCase));

        string? ip = null, gateway = null, dns = null, mac = null, adapter = null;
        long? speed = null;
        if (nic is not null)
        {
            var props = nic.GetIPProperties();
            ip = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
            gateway = props.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
            var servers = props.DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).ToList();
            dns = servers.Count > 0 ? string.Join(", ", servers) : null;
            var bytes = nic.GetPhysicalAddress().GetAddressBytes();
            mac = bytes.Length > 0 ? string.Join(":", bytes.Select(b => b.ToString("X2"))) : null;
            speed = nic.Speed > 0 ? nic.Speed / 1_000_000 : null;
            adapter = nic.Description;
        }
        return new NetworkDetails(true, internet, type, name, signal, ip, gateway, dns, speed, mac, adapter, note);
    }
}
