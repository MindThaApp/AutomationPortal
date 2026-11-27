using System.Diagnostics;
using System.Runtime.InteropServices;
using AutomationPortal.Actions.Windows.Apps;
using AutomationPortal.Actions.Windows.Native;
using NAudio.CoreAudioApi;

namespace AutomationPortal.Actions.Windows.Devices;

/// <summary>Sound devices, volume, mute and per-app volume (Windows Core Audio through NAudio).</summary>
public static class AudioService
{
    public sealed record AudioDevice(string Id, string Name, bool IsDefault);

    private const string DefaultId = "default";

    internal static MMDevice? DefaultDevice(DataFlow flow, Role role = Role.Multimedia)
    {
        try
        {
            using var e = new MMDeviceEnumerator();
            return e.GetDefaultAudioEndpoint(flow, role);
        }
        catch (COMException) { return null; }
    }

    /// <summary>Opens the chosen device ("default" or empty = the current default). Dispose the result.</summary>
    internal static MMDevice Open(DataFlow flow, string? id)
    {
        var noun = flow == DataFlow.Render ? Strings.Get("AudioService_Noun_Output") : Strings.Get("AudioService_Noun_Microphone");
        try
        {
            using var e = new MMDeviceEnumerator();
            if (string.IsNullOrWhiteSpace(id) || id == DefaultId)
                return e.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            return e.GetDevice(id);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            throw new ActionException(Strings.Format("AudioDevice_Error_Couldn_T_Find_The_0_Check", noun), ex);
        }
    }

    public static IReadOnlyList<AudioDevice> GetDevices(DataFlow flow)
    {
        using var e = new MMDeviceEnumerator();
        string? defaultId = null;
        try { using var d = e.GetDefaultAudioEndpoint(flow, Role.Multimedia); defaultId = d.ID; } catch (COMException) { }
        var list = new List<AudioDevice>();
        foreach (var d in e.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (d) list.Add(new AudioDevice(d.ID, d.FriendlyName, d.ID == defaultId));
        }
        return list;
    }

    internal static ParameterDescriptor DeviceParam(DataFlow flow, bool includeDefault = true)
    {
        var output = flow == DataFlow.Render;
        return new ParameterDescriptor
        {
            Key = "device",
            Label = output ? Strings.Get("AudioService_Param_Output_Label") : Strings.Get("AudioService_Param_Microphone_Label"),
            Type = ParamType.Choice,
            Default = includeDefault ? ParamValue.Choice(DefaultId) : null,
            Required = !includeDefault,
            ChoicesProvider = (_, ct) => Task.Run<IReadOnlyList<Choice>>(() =>
            {
                var list = new List<Choice>();
                if (includeDefault) list.Add(new Choice(DefaultId, output ? Strings.Get("AudioService_Choice_CurrentOutput") : Strings.Get("AudioService_Choice_CurrentMicrophone")));
                list.AddRange(GetDevices(flow).Select(d => new Choice(d.Id, d.Name)));
                return list;
            }, ct),
        };
    }

    // ---- volume / mute -----------------------------------------------------------------------

    public static int GetVolumePercent(DataFlow flow, string? deviceId = null)
    {
        using var d = Open(flow, deviceId);
        return (int)Math.Round(d.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
    }

    public static void SetVolumePercent(DataFlow flow, int percent, string? deviceId = null)
    {
        using var d = Open(flow, deviceId);
        d.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(percent, 0, 100) / 100f;
    }

    public static bool GetMute(DataFlow flow, string? deviceId = null)
    {
        using var d = Open(flow, deviceId);
        return d.AudioEndpointVolume.Mute;
    }

    public static void SetMute(DataFlow flow, bool mute, string? deviceId = null)
    {
        using var d = Open(flow, deviceId);
        d.AudioEndpointVolume.Mute = mute;
    }

    /// <summary>Makes a device the default for apps, media and calls (the same as picking it in Sound settings; no admin needed).</summary>
    public static void SetDefaultDevice(string deviceId)
    {
        IPolicyConfig config;
        try { config = ComGuids.Create<IPolicyConfig>(ComGuids.PolicyConfigClient); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or InvalidCastException)
        {
            throw new ActionException(Strings.Get("AudioDevice_Error_Windows_Wouldn_T_Let_Automation_Portal"), ex);
        }
        try
        {
            foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
            {
                var hr = config.SetDefaultEndpoint(deviceId, role);
                if (hr != 0) throw new ActionException(Strings.Get("AudioDevice_Error_Windows_Couldn_T_Switch_To_That"));
            }
        }
        finally { Marshal.ReleaseComObject(config); }
    }

    // ---- per-app volume ----------------------------------------------------------------------

    public sealed record AppSession(string ProcessName, int ProcessId, float Volume, bool Muted);

    internal static List<(AudioSessionControl Session, string ProcessName, string? Path)> FindSessions(AppItem app)
    {
        var found = new List<(AudioSessionControl, string, string?)>();
        using var e = new MMDeviceEnumerator();
        foreach (var dev in e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            var sessions = dev.AudioSessionManager.Sessions;
            for (var i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                try
                {
                    if (s.IsSystemSoundsSession) continue;
                    var pid = (int)s.GetProcessID;
                    if (pid <= 0) continue;
                    var path = Win32.GetProcessPath((uint)pid);
                    string name;
                    try { name = Process.GetProcessById(pid).ProcessName; } catch (ArgumentException) { continue; }
                    if (AppMatcher.Matches(app, new AppCandidate(name, path, null, s.DisplayName, false)))
                        found.Add((s, name, path));
                }
                catch (COMException) { }
            }
        }
        return found;
    }
}
