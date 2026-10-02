using Flowmate.Core;
using Flowmate.Triggers.Native;

namespace Flowmate.Triggers;

internal static class IdleTime
{
    public static TimeSpan Current()
    {
        var info = new Win32.LASTINPUTINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.LASTINPUTINFO>() };
        if (!Win32.GetLastInputInfo(ref info)) return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
    }
}

public sealed class SignInTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "system.signIn", Title = "When I sign in", Category = TP.CatSystem, Icon = "Person24",
        Summary = "When I sign in or Flow Mate starts",
        Description = "Runs once each time Flow Mate starts, which is normally when you sign in to Windows (turn on \"Start with Windows\" in Settings).",
        OutputKind = ContentKind.Date, OutputDescription = "The date and time Flow Mate started",
        Parameters = Array.Empty<ParameterDescriptor>(),
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var cts = new CancellationTokenSource();
        if (TP.Env.IsInitialLoad)
        {
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(TimeSpan.FromSeconds(4), cts.Token); sink.Fire(TP.Now(), "You signed in"); }
                catch (OperationCanceledException) { }
            });
        }
        return new DisposableAction(() => cts.Cancel());
    }
}

public sealed class WakeTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "system.wake", Title = "When the PC wakes up", Category = TP.CatSystem, Icon = "WeatherMoon24",
        Summary = "When the PC wakes up",
        Description = "Runs a few seconds after the PC wakes from sleep or hibernation, so Wi-Fi has a moment to reconnect.",
        OutputKind = ContentKind.Date, OutputDescription = "The date and time it woke",
        Parameters = Array.Empty<ParameterDescriptor>(),
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var hub = TP.Env.Hub;
        var cts = new CancellationTokenSource();
        void OnResume() => _ = Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(5), cts.Token); sink.Fire(TP.Now(), "The PC woke up"); }
            catch (OperationCanceledException) { }
        });
        hub.Resumed += OnResume;
        return new DisposableAction(() => { hub.Resumed -= OnResume; cts.Cancel(); });
    }
}

public abstract class SessionLockTriggerBase : TriggerProvider
{
    protected abstract bool FireOnLock { get; }

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var hub = TP.Env.Hub;
        void OnChange(bool locked)
        {
            if (locked == FireOnLock) sink.Fire(TP.Now(), locked ? "The PC was locked" : "The PC was unlocked");
        }
        hub.SessionLockChanged += OnChange;
        return new DisposableAction(() => hub.SessionLockChanged -= OnChange);
    }
}

public sealed class LockTrigger : SessionLockTriggerBase
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "system.lock", Title = "When the PC is locked", Category = TP.CatSystem, Icon = "LockClosed24",
        Summary = "When the PC is locked",
        Description = "Runs when you lock Windows (Win + L) or it locks itself.",
        OutputKind = ContentKind.Date, OutputDescription = "The date and time it was locked",
        Parameters = Array.Empty<ParameterDescriptor>(),
    };
    public override TriggerDescriptor Descriptor => D;
    protected override bool FireOnLock => true;
}

public sealed class UnlockTrigger : SessionLockTriggerBase
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "system.unlock", Title = "When the PC is unlocked", Category = TP.CatSystem, Icon = "LockOpen24",
        Summary = "When the PC is unlocked",
        Description = "Runs when you sign back in after the PC was locked.",
        OutputKind = ContentKind.Date, OutputDescription = "The date and time it was unlocked",
        Parameters = Array.Empty<ParameterDescriptor>(),
    };
    public override TriggerDescriptor Descriptor => D;
    protected override bool FireOnLock => false;
}

public sealed class IdleTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "system.idle", Title = "When I've been away", Category = TP.CatSystem, Icon = "PersonClock24",
        Summary = "When I haven't used the PC for {minutes} minutes",
        Description = "Runs when you haven't touched the keyboard or mouse for the number of minutes you choose. It runs once per break.",
        OutputKind = ContentKind.Number, OutputDescription = "How many minutes you've been away",
        Parameters = new[] { ParameterDescriptor.Number("minutes", "Minutes away", 5, 1, 720) },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var limit = TimeSpan.FromMinutes(Math.Max(1, config.GetNumber("minutes", 5)));
        var fired = false;
        return new Timer(_ => TriggerLog.Guard(() =>
        {
            var idle = IdleTime.Current();
            if (idle < TimeSpan.FromSeconds(5)) fired = false;
            else if (!fired && idle >= limit)
            {
                fired = true;
                sink.Fire(ContentValue.Number(Math.Round(idle.TotalMinutes)), PluralAgreement.Fix($"You've been away for {Math.Round(idle.TotalMinutes)} minutes"));
            }
        }, "Idle trigger"), null, 5000, 5000);
    }
}

public sealed class BackTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "system.back", Title = "When I come back", Category = TP.CatSystem, Icon = "PersonArrowRight24",
        Summary = "When I'm back after {minutes} minutes away",
        Description = "Runs when you start using the keyboard or mouse again after being away for at least the number of minutes you choose.",
        OutputKind = ContentKind.Number, OutputDescription = "How many minutes you were away",
        Parameters = new[] { ParameterDescriptor.Number("minutes", "Minutes away", 5, 1, 720) },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var limit = TimeSpan.FromMinutes(Math.Max(1, config.GetNumber("minutes", 5)));
        var longest = TimeSpan.Zero;
        return new Timer(_ => TriggerLog.Guard(() =>
        {
            var idle = IdleTime.Current();
            if (idle >= limit) longest = idle;
            else if (longest >= limit && idle < TimeSpan.FromSeconds(4))
            {
                var away = Math.Round(longest.TotalMinutes);
                longest = TimeSpan.Zero;
                sink.Fire(ContentValue.Number(away), PluralAgreement.Fix($"Welcome back, you were away for {away} minutes"));
            }
            else if (idle < TimeSpan.FromSeconds(4)) longest = TimeSpan.Zero;
        }, "Welcome-back trigger"), null, 2000, 2000);
    }
}

// ---- apps ------------------------------------------------------------------------------------

public abstract class AppProcessTriggerBase : TriggerProvider
{
    protected abstract bool FireOnOpen { get; }

    protected static AppValue? AppFrom(TriggerConfig config) =>
        config.GetApp("app") ?? (config.GetString("app") is { Length: > 0 } s ? new AppValue(s, s) : null);

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var env = TP.Env;
        var app = AppFrom(config);
        if (app is null) { sink.ReportProblem("Choose an app for this trigger."); return new DisposableAction(() => { }); }
        var label = app.Name ?? app.Id;
        var running = new HashSet<int>();
        var gate = new object();
        bool Matches(ProcInfo p) => AppMatcher.Matches(app.Id, app.Name, p.Name, p.Path);

        void OnStarted(ProcInfo p)
        {
            if (!Matches(p)) return;
            bool first;
            lock (gate) { first = running.Count == 0; running.Add(p.Pid); }
            if (first && FireOnOpen) sink.Fire(ContentValue.From(new AppItem(p.Path ?? app.Id, label, p.Path)), $"{label} opened");
        }
        void OnExited(ProcInfo p)
        {
            bool last;
            lock (gate) { if (!running.Remove(p.Pid)) return; last = running.Count == 0; }
            if (last && !FireOnOpen) sink.Fire(ContentValue.From(new AppItem(p.Path ?? app.Id, label, p.Path)), $"{label} closed");
        }

        var sub = env.Processes.Subscribe();
        lock (gate) foreach (var p in env.Processes.Known.Where(Matches)) running.Add(p.Pid);
        env.Processes.Started += OnStarted;
        env.Processes.Exited += OnExited;
        return new DisposableAction(() => { env.Processes.Started -= OnStarted; env.Processes.Exited -= OnExited; sub.Dispose(); });
    }
}

public sealed class AppOpenedTrigger : AppProcessTriggerBase
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "app.opened", Title = "When an app opens", Category = TP.CatApps, Icon = "Apps24",
        Summary = "When {app} opens",
        Description = "Runs when the app you choose starts. If it's already open when Flow Mate starts, nothing happens until you close it and open it again.",
        OutputKind = ContentKind.App, OutputDescription = "The app that opened",
        Parameters = new[] { TP.AppParam() },
    };
    public override TriggerDescriptor Descriptor => D;
    protected override bool FireOnOpen => true;
}

public sealed class AppClosedTrigger : AppProcessTriggerBase
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "app.closed", Title = "When an app closes", Category = TP.CatApps, Icon = "AppsListDetail24",
        Summary = "When {app} closes",
        Description = "Runs when the last window of the app you choose is closed and the app stops running.",
        OutputKind = ContentKind.App, OutputDescription = "The app that closed",
        Parameters = new[] { TP.AppParam() },
    };
    public override TriggerDescriptor Descriptor => D;
    protected override bool FireOnOpen => false;
}

public sealed class AppFocusedTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "app.focused", Title = "When an app comes to the front", Category = TP.CatApps, Icon = "WindowArrowUp24",
        Summary = "When {app} comes to the front",
        Description = "Runs each time you switch to the app you choose. It doesn't run again while you stay in that app.",
        OutputKind = ContentKind.App, OutputDescription = "The app that came to the front",
        Parameters = new[] { TP.AppParam() },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var hub = TP.Env.Hub;
        var app = config.GetApp("app") ?? (config.GetString("app") is { Length: > 0 } s ? new AppValue(s, s) : null);
        if (app is null) { sink.ReportProblem("Choose an app for this trigger."); return new DisposableAction(() => { }); }
        var wasMatching = false;
        var last = DateTime.MinValue;
        var gate = new object();
        void OnForeground(IntPtr hwnd) => _ = Task.Run(() =>
        {
            var info = AppInfo.FromWindow(hwnd);
            if (info is null || info.ProcessId == Environment.ProcessId) return;
            var matches = AppMatcher.Matches(app, info);
            lock (gate)
            {
                var fire = matches && !wasMatching && DateTime.UtcNow - last > TimeSpan.FromMilliseconds(500);
                wasMatching = matches;
                if (!fire) return;
                last = DateTime.UtcNow;
            }
            sink.Fire(ContentValue.From(info.ToItem()), $"{app.Name ?? info.DisplayName} came to the front");
        });
        hub.ForegroundChanged += OnForeground;
        return new DisposableAction(() => hub.ForegroundChanged -= OnForeground);
    }
}
