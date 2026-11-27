using System.Runtime.InteropServices;

namespace AutomationPortal.Core.Platform;

public sealed record PowerPlan(Guid Id, string Name);

/// <summary>The Windows power plans (powrprof). Tests use a fake so nothing ever touches the real plan.</summary>
public interface IPowerPlanApi
{
    IReadOnlyList<PowerPlan> List();
    PowerPlan? GetActive();
    void SetActive(Guid id);
    /// <summary>Calls back whenever the active plan notification arrives (Windows also sends one right after subscribing).</summary>
    IDisposable Subscribe(Action<Guid> onActivePlanNotification);
}

public static class PowerPlanLogic
{
    /// <summary>Finds a plan by name (any case) or by its GUID text.</summary>
    public static PowerPlan? Find(IReadOnlyList<PowerPlan> plans, string? nameOrId)
    {
        var text = nameOrId?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (Guid.TryParse(text, out var id) && plans.FirstOrDefault(p => p.Id == id) is { } byId) return byId;
        return plans.FirstOrDefault(p => string.Equals(p.Name, text, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Turns raw notifications into real changes: the notification that arrives on registration and repeats of the
/// same plan are dropped.
/// </summary>
public sealed class PowerPlanChangeFilter
{
    private readonly object _gate = new();
    private Guid _last;

    public PowerPlanChangeFilter(Guid current) { _last = current; }

    public bool Accept(Guid notified)
    {
        lock (_gate)
        {
            if (notified == _last) return false;
            _last = notified;
            return true;
        }
    }
}

public sealed class WindowsPowerPlanApi : IPowerPlanApi
{
    private const uint AccessScheme = 16;
    private const uint DeviceNotifyCallback = 2;
    private const uint PbtPowerSettingChange = 0x8013;
    private static readonly Guid ActiveSchemeSetting = new("31F9F286-5084-42FE-B720-2B0264993763");

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint NotifyCallback(IntPtr context, uint type, IntPtr setting);

    [StructLayout(LayoutKind.Sequential)]
    private struct SubscribeParameters { public IntPtr Callback; public IntPtr Context; }

    [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr subGroup, uint flags, uint index, IntPtr buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr subGroup, IntPtr setting, IntPtr buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerSettingRegisterNotification(ref Guid setting, uint flags, ref SubscribeParameters recipient, out IntPtr handle);
    [DllImport("powrprof.dll")] private static extern uint PowerSettingUnregisterNotification(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);

    public IReadOnlyList<PowerPlan> List()
    {
        var plans = new List<PowerPlan>();
        for (uint i = 0; i < 64; i++)
        {
            var buf = Marshal.AllocHGlobal(16);
            try
            {
                uint size = 16;
                if (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, i, buf, ref size) != 0) break;
                var id = Marshal.PtrToStructure<Guid>(buf);
                plans.Add(new PowerPlan(id, FriendlyName(id)));
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        return plans;
    }

    public PowerPlan? GetActive()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var p) != 0 || p == IntPtr.Zero) return null;
        try
        {
            var id = Marshal.PtrToStructure<Guid>(p);
            return new PowerPlan(id, FriendlyName(id));
        }
        finally { LocalFree(p); }
    }

    public void SetActive(Guid id)
    {
        if (PowerSetActiveScheme(IntPtr.Zero, ref id) != 0)
            throw new InvalidOperationException(Strings.Get("PowerPlans_Error_Switch"));
    }

    public IDisposable Subscribe(Action<Guid> onActivePlanNotification) => new Subscription(onActivePlanNotification);

    private static string FriendlyName(Guid id)
    {
        uint size = 0;
        PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
        if (size < 2) return id.ToString();
        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            return PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, buf, ref size) == 0
                ? Marshal.PtrToStringUni(buf) ?? id.ToString() : id.ToString();
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private sealed class Subscription : IDisposable
    {
        // Held in a field so the GC can't collect the delegate while Windows still holds its function pointer.
        private readonly NotifyCallback _callback;
        private IntPtr _handle;

        public Subscription(Action<Guid> onChanged)
        {
            _callback = (_, type, setting) =>
            {
                try
                {
                    if (type == PbtPowerSettingChange && setting != IntPtr.Zero)
                    {
                        // POWERBROADCAST_SETTING: Guid PowerSetting; uint DataLength; byte Data[]
                        var which = Marshal.PtrToStructure<Guid>(setting);
                        if (which == ActiveSchemeSetting && Marshal.ReadInt32(setting, 16) >= 16)
                            onChanged(Marshal.PtrToStructure<Guid>(setting + 20));
                    }
                }
                catch { }
                return 0;
            };
            var p = new SubscribeParameters { Callback = Marshal.GetFunctionPointerForDelegate(_callback), Context = IntPtr.Zero };
            var g = ActiveSchemeSetting;
            if (PowerSettingRegisterNotification(ref g, DeviceNotifyCallback, ref p, out _handle) != 0) _handle = IntPtr.Zero;
        }

        public void Dispose()
        {
            var h = Interlocked.Exchange(ref _handle, IntPtr.Zero);
            if (h != IntPtr.Zero) PowerSettingUnregisterNotification(h);
            GC.KeepAlive(_callback);
        }
    }
}
