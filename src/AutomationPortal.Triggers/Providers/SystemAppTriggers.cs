using AutomationPortal.Core;
using AutomationPortal.Triggers.Native;

namespace AutomationPortal.Triggers;

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
        Id = "system.signIn", Title = Strings.Get("Trigger_SystemSignIn_Title"), Category = TP.CatSystem, Icon = "Person24",
        Summary = Strings.Get("Trigger_SystemSignIn_Summary"),
        Description = Strings.Get("Trigger_SystemSignIn_Description"),
        OutputKind = ContentKind.Date, OutputDescription = Strings.Get("Trigger_SystemSignIn_OutputDescription"),
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
                try { await Task.Delay(TimeSpan.FromSeconds(4), cts.Token); sink.Fire(TP.Now(), Strings.Get("Trigger_SystemSignIn_Fired_YouSignedIn")); }
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
        Id = "system.wake", Title = Strings.Get("Trigger_SystemWake_Title"), Category = TP.CatSystem, Icon = "WeatherMoon24",
        Summary = Strings.Get("Trigger_SystemWake_Title"),
        Description = Strings.Get("Trigger_SystemWake_Description"),
        OutputKind = ContentKind.Date, OutputDescription = Strings.Get("Trigger_SystemWake_OutputDescription"),
        Parameters = Array.Empty<ParameterDescriptor>(),
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var hub = TP.Env.Hub;
        var cts = new CancellationTokenSource();
        void OnResume() => _ = Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(5), cts.Token); sink.Fire(TP.Now(), Strings.Get("Trigger_SystemWake_Fired_ThePCWokeUp")); }
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
            if (locked == FireOnLock) sink.Fire(TP.Now(), locked ? Strings.Get("Trigger_SystemWake_Fired_ThePCWasLocked") : Strings.Get("Trigger_SystemWake_Fired_ThePCWasUnlocked"));
        }
        hub.SessionLockChanged += OnChange;
        return new DisposableAction(() => hub.SessionLockChanged -= OnChange);
    }
}

public sealed class LockTrigger : SessionLockTriggerBase
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "system.lock", Title = Strings.Get("Trigger_SystemLock_Title"), Category = TP.CatSystem, Icon = "LockClosed24",
        Summary = Strings.Get("Trigger_SystemLock_Title"),
        Description = Strings.Get("Trigger_SystemLock_Description"),
        OutputKind = ContentKind.Date, OutputDescription = Strings.Get("Trigger_SystemLock_OutputDescription"),
        Parameters = Array.Empty<ParameterDescriptor>(),
    };
    public override TriggerDescriptor Descriptor => D;
    protected override bool FireOnLock => true;
}

public sealed class UnlockTrigger : SessionLockTriggerBase
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "system.unlock", Title = Strings.Get("Trigger_SystemUnlock_Title"), Category = TP.CatSystem, Icon = "LockOpen24",
        Summary = Strings.Get("Trigger_SystemUnlock_Title"),
        Description = Strings.Get("Trigger_SystemUnlock_Description"),
        OutputKind = ContentKind.Date, OutputDescription = Strings.Get("Trigger_SystemUnlock_OutputDescription"),
        Parameters = Array.Empty<ParameterDescriptor>(),
    };
    public override TriggerDescriptor Descriptor => D;
    protected override bool FireOnLock => false;
}

public sealed class IdleTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "system.idle", Title = Strings.Get("Trigger_SystemIdle_Title"), Category = TP.CatSystem, Icon = "PersonClock24",
        Summary = Strings.Get("Trigger_SystemIdle_Summary"),
        Description = Strings.Get("Trigger_SystemIdle_Description"),
        OutputKind = ContentKind.Number, OutputDescription = Strings.Get("Trigger_SystemIdle_OutputDescription"),
        Parameters = new[] { ParameterDescriptor.Number("minutes", Strings.Get("Trigger_SystemIdle_Param_Minutes_Label"), 5, 1, 720) },
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
                sink.Fire(ContentValue.Number(Math.Round(idle.TotalMinutes)), PluralAgreement.Fix(Strings.Format("Trigger_Idle_Fired_Away", Math.Round(idle.TotalMinutes))));
            }
        }, "Idle trigger"), null, 5000, 5000);
    }
}

public sealed class BackTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "system.back", Title = Strings.Get("Trigger_SystemBack_Title"), Category = TP.CatSystem, Icon = "PersonArrowRight24",
        Summary = Strings.Get("Trigger_SystemBack_Summary"),
        Description = Strings.Get("Trigger_SystemBack_Description"),
        OutputKind = ContentKind.Number, OutputDescription = Strings.Get("Trigger_SystemBack_OutputDescription"),
        Parameters = new[] { ParameterDescriptor.Number("minutes", Strings.Get("Trigger_SystemBack_Param_Minutes_Label"), 5, 1, 720) },
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
                sink.Fire(ContentValue.Number(away), PluralAgreement.Fix(Strings.Format("Trigger_Back_Fired_Away", away)));
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
        if (app is null) { sink.ReportProblem(Strings.Get("Trigger_SystemBack_Problem_ChooseAnAppFor")); return new DisposableAction(() => { }); }
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
        Id = "app.opened", Title = Strings.Get("Trigger_AppOpened_Title"), Category = TP.CatApps, Icon = "Apps24",
        Summary = Strings.Get("Trigger_AppOpened_Summary"),
        Description = Strings.Get("Trigger_AppOpened_Description"),
        OutputKind = ContentKind.App, OutputDescription = Strings.Get("Trigger_AppOpened_OutputDescription"),
        Parameters = new[] { TP.AppParam() },
    };
    public override TriggerDescriptor Descriptor => D;
    protected override bool FireOnOpen => true;
}

public sealed class AppClosedTrigger : AppProcessTriggerBase
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "app.closed", Title = Strings.Get("Trigger_AppClosed_Title"), Category = TP.CatApps, Icon = "AppsListDetail24",
        Summary = Strings.Get("Trigger_AppClosed_Summary"),
        Description = Strings.Get("Trigger_AppClosed_Description"),
        OutputKind = ContentKind.App, OutputDescription = Strings.Get("Trigger_AppClosed_OutputDescription"),
        Parameters = new[] { TP.AppParam() },
    };
    public override TriggerDescriptor Descriptor => D;
    protected override bool FireOnOpen => false;
}

public sealed class AppFocusedTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "app.focused", Title = Strings.Get("Trigger_AppFocused_Title"), Category = TP.CatApps, Icon = "WindowArrowUp24",
        Summary = Strings.Get("Trigger_AppFocused_Summary"),
        Description = Strings.Get("Trigger_AppFocused_Description"),
        OutputKind = ContentKind.App, OutputDescription = Strings.Get("Trigger_AppFocused_OutputDescription"),
        Parameters = new[] { TP.AppParam() },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var hub = TP.Env.Hub;
        var app = config.GetApp("app") ?? (config.GetString("app") is { Length: > 0 } s ? new AppValue(s, s) : null);
        if (app is null) { sink.ReportProblem(Strings.Get("Trigger_SystemBack_Problem_ChooseAnAppFor")); return new DisposableAction(() => { }); }
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
            sink.Fire(ContentValue.From(info.ToItem()), Strings.Format("Trigger_AppFront_Fired_CameToFront", app.Name ?? info.DisplayName));
        });
        hub.ForegroundChanged += OnForeground;
        return new DisposableAction(() => hub.ForegroundChanged -= OnForeground);
    }
}
