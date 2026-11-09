using AutomationPortal.Actions.Windows.Native;

namespace AutomationPortal.Actions.Windows.Devices;

/// <summary>
/// Keeps Windows from sleeping while Automation Portal runs. A dedicated thread holds <c>SetThreadExecutionState</c>
/// (the setting belongs to the thread that asked for it) until the time is up or <see cref="Stop"/> is called.
/// It ends when Automation Portal exits.
/// </summary>
public static class KeepAwakeService
{
    private sealed class Session
    {
        public required CancellationTokenSource Cts { get; init; }
        public required Thread Thread { get; init; }
        public DateTimeOffset? Until { get; init; }
        public bool KeepScreenOn { get; init; }
    }

    private static readonly object Gate = new();
    private static Session? _current;

    public static bool IsActive { get { lock (Gate) return _current is not null; } }
    public static DateTimeOffset? Until { get { lock (Gate) return _current?.Until; } }
    public static bool KeepsScreenOn { get { lock (Gate) return _current?.KeepScreenOn ?? false; } }

    /// <summary>Starts (or restarts) keeping the PC awake. A null or non-positive <paramref name="duration"/> means until <see cref="Stop"/>.</summary>
    public static void Start(TimeSpan? duration, bool keepScreenOn)
    {
        lock (Gate)
        {
            StopLocked();
            var cts = new CancellationTokenSource();
            TimeSpan? span = duration is { } d && d > TimeSpan.Zero ? d : null;
            var until = span is null ? (DateTimeOffset?)null : DateTimeOffset.Now + span.Value;
            Session? session = null;
            var thread = new Thread(() =>
            {
                var flags = Win32.ES_CONTINUOUS | Win32.ES_SYSTEM_REQUIRED | (keepScreenOn ? Win32.ES_DISPLAY_REQUIRED : 0);
                Win32.SetThreadExecutionState(flags);
                try { cts.Token.WaitHandle.WaitOne(span ?? Timeout.InfiniteTimeSpan); }
                finally
                {
                    Win32.SetThreadExecutionState(Win32.ES_CONTINUOUS);
                    lock (Gate) { if (ReferenceEquals(_current, session)) _current = null; }
                }
            }) { IsBackground = true, Name = "Automation Portal keep awake" };
            session = new Session { Cts = cts, Thread = thread, Until = until, KeepScreenOn = keepScreenOn };
            _current = session;
            thread.Start();
        }
    }

    public static bool Stop()
    {
        lock (Gate) return StopLocked();
    }

    private static bool StopLocked()
    {
        if (_current is not { } s) return false;
        _current = null;
        s.Cts.Cancel();
        return true;
    }
}

public sealed record BatteryInfo(bool HasBattery, int Percent, bool PluggedIn, bool Charging, int? MinutesLeft, bool BatterySaverOn)
{
    /// <summary>Builds from the raw Windows power status values (SYSTEM_POWER_STATUS).</summary>
    public static BatteryInfo From(byte acLine, byte batteryFlag, byte percent, byte systemStatusFlag, uint secondsLeft)
    {
        var hasBattery = batteryFlag != 128 && batteryFlag != 255 && percent != 255;
        return new BatteryInfo(
            hasBattery,
            hasBattery ? Math.Min((int)percent, 100) : 0,
            acLine == 1,
            hasBattery && (batteryFlag & 8) != 0,
            hasBattery && secondsLeft != uint.MaxValue && acLine != 1 ? (int)(secondsLeft / 60) : null,
            systemStatusFlag == 1);
    }
}

internal static class PowerService
{
    public static BatteryInfo GetBattery()
    {
        if (!Win32.GetSystemPowerStatus(out var s)) throw new ActionException("Windows wouldn't tell Automation Portal about the battery.");
        return BatteryInfo.From(s.ACLineStatus, s.BatteryFlag, s.BatteryLifePercent, s.SystemStatusFlag, s.BatteryLifeTime);
    }

    public static readonly IReadOnlyList<(string Id, string Label, Guid Overlay, string PowerCfgScheme)> Modes = new[]
    {
        ("efficiency", "Best power efficiency", new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a"), "SCHEME_MAX"),
        ("balanced", "Balanced", Guid.Empty, "SCHEME_BALANCED"),
        ("performance", "Better performance", new Guid("3af9b8d9-7c97-431d-ad78-34a8bfea439f"), "SCHEME_MIN"),
        ("max", "Best performance", new Guid("ded574b5-45a0-4f42-8737-46345c09c238"), "SCHEME_MIN"),
    };

    public static async Task SetModeAsync(string id, CancellationToken ct)
    {
        var mode = Modes.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (mode.Id is null) throw new ActionException($"I don't know the power mode '{id}'. Choose one from the list.");
        var rc = Win32.PowerSetActiveOverlayScheme(mode.Overlay);
        if (rc == 0) return;
        // Overlay modes can be missing (older Windows, some desktops); fall back to the classic power plans.
        var result = await ProcessRunner.RunAsync("powercfg.exe", $"/setactive {mode.PowerCfgScheme}", TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new ActionException($"Windows wouldn't switch to '{mode.Label}' on this PC. Change it in Settings > System > Power & battery.");
    }
}
