using AutomationPortal.Core;

namespace AutomationPortal.Triggers;

public sealed class FileChangedTrigger : TriggerProvider, IFileWatchTrigger
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "file.changed", Title = Strings.Get("Trigger_FileChanged_Title"), Category = TP.CatFiles, Icon = "FolderArrowRight24",
        Summary = Strings.Get("Trigger_FileChanged_Summary"),
        Description = Strings.Get("Trigger_FileChanged_Description"),
        OutputKind = ContentKind.File, OutputDescription = Strings.Get("Trigger_FileChanged_OutputDescription"),
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "events", Label = Strings.Get("Trigger_FileChanged_Param_Events_Label"), Type = ParamType.List, AllowVariables = false, AllowAskEachTime = false,
                Choices = new Choice[] { new("added", "added"), new("changed", "changed"), new("removed", "removed") }, Default = ParamValue.List("added") },
            new ParameterDescriptor { Key = "folder", Label = Strings.Get("Trigger_FileChanged_Param_Folder_Label"), Type = ParamType.Folder, Required = true, AllowVariables = false, AllowAskEachTime = false },
            ParameterDescriptor.ChoiceOf("kind", Strings.Get("Trigger_FileChanged_Param_Kind_Label"), "any", FileMatchFilter.KindChoices.ToArray()),
            ParameterDescriptor.Text("extensions", Strings.Get("Trigger_FileChanged_Param_Extensions_Label"), null, Strings.Get("Trigger_FileChanged_Param_Extensions_Placeholder")) with { AllowVariables = false, AllowAskEachTime = false },
            ParameterDescriptor.Bool("subfolders", Strings.Get("Trigger_FileChanged_Param_Subfolders_Label")),
            ParameterDescriptor.Number("wait", Strings.Get("Trigger_FileChanged_Param_Wait_Label"), 3, 1, 60),
        },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var folder = config.GetFolder("folder");
        if (folder is null) { sink.ReportProblem(Strings.Get("Trigger_FileChanged_Problem_ChooseAFolderTo")); return new DisposableAction(() => { }); }
        var events = config.GetStrings("events").Select(e => e.ToLowerInvariant()).ToHashSet();
        if (events.Count == 0) events.Add("added");
        var kind = config.GetChoice("kind", "any");
        var extensions = FileMatchFilter.ParseExtensions(config.GetString("extensions"));
        var wait = TimeSpan.FromSeconds(Math.Clamp(config.GetNumber("wait", 3), 1, 60));
        var subfolders = config.GetBool("subfolders");
        var folderName = Path.GetFileName(folder.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : folder;

        var batcher = new FolderChangeBatcher(wait, new DiskFileProbe(), p => FileMatchFilter.Accepts(p, kind, extensions));
        var handler = new FolderTriggerEvents(batcher, sink, events, folderName, wait);
        var gate = new object();
        FileSystemWatcher? watcher = null;

        void Attach()
        {
            lock (gate)
            {
                if (watcher is not null) return;
                if (!Directory.Exists(folder)) { sink.ReportProblem(Strings.Format("Trigger_Folder_Problem_Missing", folder)); return; }
                try
                {
                    var w = new FileSystemWatcher(folder)
                    {
                        IncludeSubdirectories = subfolders, InternalBufferSize = 64 * 1024,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    };
                    w.Created += (_, e) => handler.OnCreated(e.FullPath);
                    w.Changed += (_, e) => handler.OnChanged(e.FullPath);
                    w.Deleted += (_, e) => handler.OnDeleted(e.FullPath);
                    w.Renamed += (_, e) => handler.OnRenamed(e.OldFullPath, e.FullPath);
                    w.Error += (_, e) => { TriggerLog.Write("Folder watcher error: " + e.GetException().Message); lock (gate) { watcher?.Dispose(); watcher = null; } };
                    w.EnableRaisingEvents = true;
                    watcher = w;
                    sink.ReportProblem(null);
                }
                catch (Exception ex)
                {
                    sink.ReportProblem(Strings.Format("Trigger_Folder_Problem_CantWatch", folder, ex.Message));
                }
            }
        }

        Attach();
        var timer = new Timer(_ =>
        {
            try
            {
                if (watcher is null) Attach();
                handler.Tick();
            }
            catch (Exception ex) { TriggerLog.Write("Folder trigger: " + ex.Message); }
        }, null, 500, 500);

        return new DisposableAction(() =>
        {
            timer.Dispose();
            lock (gate) { watcher?.Dispose(); watcher = null; }
        });
    }
}

/// <summary>
/// The folder trigger without the FileSystemWatcher: feeds file events to the batcher and fires finished batches.
/// While the automation is busy (including an Ask-first prompt), while its shortcut runs and shortly after, nothing fires:
/// events keep collecting in the batcher. When that window is over, the paths the run itself handled or produced (its input
/// files, a renamed file, a resized copy, a zip) are left out, so the automation can't trigger itself over and over; anything
/// else that arrived meanwhile (more downloads, files the user saved) still fires, once. A batch that is ready during the
/// cooldown waits for it to end. Files a run makes without giving them back (a command's output) do fire again: the engine's
/// runaway breaker pauses an automation that keeps running too often.
/// </summary>
internal sealed class FolderTriggerEvents
{
    private readonly FolderChangeBatcher _batcher;
    private readonly ITriggerSink _sink;
    private readonly HashSet<string> _events;
    private readonly string _folderName;
    private readonly TimeSpan _ownRunGrace;
    // Paths whose events arrived during the run window: only these are checked against what the run touched.
    private readonly HashSet<string> _duringRun = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public FolderTriggerEvents(FolderChangeBatcher batcher, ITriggerSink sink, HashSet<string> events, string folderName, TimeSpan wait)
    {
        _batcher = batcher;
        _sink = sink;
        _events = events;
        _folderName = folderName;
        _ownRunGrace = wait + TimeSpan.FromSeconds(2);
    }

    private ITriggerSinkRunWindow? Window => _sink as ITriggerSinkRunWindow;
    private bool FromOwnRun => Window?.IsOwnRunRecent(_ownRunGrace) == true;

    private void Note(string path)
    {
        var during = FromOwnRun;
        lock (_gate)
        {
            if (during) _duringRun.Add(path);
            else _duringRun.Remove(path); // the user touched it again after the run: that's their change
        }
    }

    // Noting and queueing happen under one lock, so Tick can't clear a path's note before its event is queued.
    public void OnCreated(string path) { lock (_gate) { Note(path); _batcher.OnCreated(path); } }
    public void OnChanged(string path) { lock (_gate) { Note(path); _batcher.OnChanged(path); } }
    public void OnDeleted(string path) { lock (_gate) { Note(path); _batcher.OnDeleted(path); } }
    public void OnRenamed(string oldPath, string newPath) { lock (_gate) { Note(oldPath); Note(newPath); _batcher.OnRenamed(oldPath, newPath); } }

    /// <summary>Called a few times per second: fires the next finished batch, if any.</summary>
    public void Tick()
    {
        // Hold everything while the automation is busy (checking its conditions, asking first, running), during the run's grace
        // period and until the cooldown has passed: the batcher keeps collecting, one entry per path, and drops none.
        if (Window?.IsBusy() == true || FromOwnRun) return;
        if (Window?.CooldownRemaining() > TimeSpan.Zero) return;
        if (_batcher.Poll() is not { } batch)
        {
            // Nothing waits in the batcher, so no remembered path can come up again.
            lock (_gate) if (!_batcher.HasPending) _duringRun.Clear();
            return;
        }

        bool Keep(string path)
        {
            lock (_gate)
            {
                if (!_duringRun.Remove(path)) return true;
            }
            return Window?.WasTouchedByOwnRun(path) != true;
        }
        var added = batch.Added.Where(Keep).ToList();
        var changed = batch.Changed.Where(Keep).ToList();
        var removed = batch.Removed.Where(Keep).ToList();

        var paths = new List<string>();
        if (_events.Contains("added")) paths.AddRange(added);
        if (_events.Contains("changed")) paths.AddRange(changed);
        if (_events.Contains("removed")) paths.AddRange(removed);
        if (paths.Count == 0) return;
        var kind = removed.Count > 0 && paths.Count == removed.Count ? 0 : added.Count > 0 && _events.Contains("added") ? 1 : 2;
        string text;
        if (paths.Count == 1)
            text = Strings.Format(kind == 0 ? "Trigger_Folder_Fired_FileRemoved" : kind == 1 ? "Trigger_Folder_Fired_FileAdded" : "Trigger_Folder_Fired_FileChanged", Path.GetFileName(paths[0]), _folderName);
        else
            text = Strings.Format(kind == 0 ? "Trigger_Folder_Fired_FilesRemoved" : kind == 1 ? "Trigger_Folder_Fired_FilesAdded" : "Trigger_Folder_Fired_FilesChanged", paths.Count, _folderName);
        _sink.Fire(ContentValue.Files(paths), text);
    }
}

public sealed class DriveConnectedTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "drive.connected", Title = Strings.Get("Trigger_DriveConnected_Title"), Category = TP.CatFiles, Icon = "UsbStick24",
        Summary = Strings.Get("Trigger_DriveConnected_Summary"),
        Description = Strings.Get("Trigger_DriveConnected_Description"),
        OutputKind = ContentKind.Folder, OutputDescription = Strings.Get("Trigger_DriveConnected_OutputDescription"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("event", Strings.Get("Trigger_DriveConnected_Param_Event_Label"), "connected", new Choice("connected", Strings.Get("Trigger_DriveConnected_Param_Event_Choice_Connected")), new Choice("removed", Strings.Get("Trigger_DriveConnected_Param_Event_Choice_Removed"))),
        },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var env = TP.Env;
        var wantAdded = config.GetChoice("event", "connected") != "removed";
        void OnDrive(DriveEvent e)
        {
            if (e.Added != wantAdded) return;
            var name = string.IsNullOrWhiteSpace(e.Label) ? e.Root : $"{e.Label} ({e.Root.TrimEnd('\\')})";
            sink.Fire(ContentValue.Folder(e.Root), wantAdded ? Strings.Format("Trigger_Drive_Fired_PluggedIn", name) : Strings.Format("Trigger_Drive_Fired_Removed", e.Root.TrimEnd('\\')));
        }
        return TP.Watch(env.Drives, () => env.Drives.Changed += OnDrive, () => env.Drives.Changed -= OnDrive);
    }
}

// ---- network ---------------------------------------------------------------------------------

public abstract class WifiTriggerBase : TriggerProvider
{
    protected static ParameterDescriptor NetworkParam() => ParameterDescriptor.DynamicChoice("network", Strings.Get("Trigger_DriveConnected_Param_Network_Label"), async (services, ct) =>
    {
        var list = new List<Choice> { new("", Strings.Get("Trigger_Wifi_Choice_AnyNetwork")) };
        list.AddRange((await NetworkMonitor.KnownWifiNamesAsync(ct)).Select(n => new Choice(n, n)));
        return list;
    }, "");

    protected abstract bool Joined { get; }

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var env = TP.Env;
        var wanted = config.GetChoice("network", "").Trim();
        bool Matches(string name) => wanted.Length == 0 || string.Equals(wanted, name, StringComparison.OrdinalIgnoreCase);
        void OnChange((NetState Old, NetState New) c)
        {
            if (c.Old.WifiName == c.New.WifiName) return;
            if (Joined && c.New.WifiName is { } joined && Matches(joined))
                sink.Fire(ContentValue.Text(joined), Strings.Format("Trigger_Wifi_Fired_Joined", joined));
            else if (!Joined && c.Old.WifiName is { } left && Matches(left))
                sink.Fire(ContentValue.Text(left), Strings.Format("Trigger_Wifi_Fired_Left", left));
        }
        return TP.Watch(env.Network, () => env.Network.Changed += OnChange, () => env.Network.Changed -= OnChange);
    }
}

public sealed class WifiJoinedTrigger : WifiTriggerBase
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "net.wifiJoined", Title = Strings.Get("Trigger_NetWifiJoined_Title"), Category = TP.CatNetwork, Icon = "Wifi124",
        Summary = Strings.Get("Trigger_NetWifiJoined_Summary"),
        Description = Strings.Get("Trigger_NetWifiJoined_Description"),
        OutputKind = ContentKind.Text, OutputDescription = Strings.Get("Trigger_NetWifiJoined_OutputDescription"),
        Parameters = new[] { NetworkParam() },
    };
    public override TriggerDescriptor Descriptor => D;
    protected override bool Joined => true;
}

public sealed class WifiLeftTrigger : WifiTriggerBase
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "net.wifiLeft", Title = Strings.Get("Trigger_NetWifiLeft_Title"), Category = TP.CatNetwork, Icon = "WifiOff24",
        Summary = Strings.Get("Trigger_NetWifiLeft_Summary"),
        Description = Strings.Get("Trigger_NetWifiLeft_Description"),
        OutputKind = ContentKind.Text, OutputDescription = Strings.Get("Trigger_NetWifiJoined_OutputDescription"),
        Parameters = new[] { NetworkParam() },
    };
    public override TriggerDescriptor Descriptor => D;
    protected override bool Joined => false;
}

public abstract class OnlineTriggerBase : TriggerProvider
{
    protected abstract bool FireWhenOnline { get; }

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var env = TP.Env;
        void OnChange((NetState Old, NetState New) c)
        {
            if (c.Old.Online == c.New.Online || c.New.Online != FireWhenOnline) return;
            sink.Fire(TP.Now(), FireWhenOnline ? Strings.Get("Trigger_NetWifiLeft_Fired_TheInternetIsBack") : Strings.Get("Trigger_NetWifiLeft_Fired_TheInternetDropped"));
        }
        return TP.Watch(env.Network, () => env.Network.Changed += OnChange, () => env.Network.Changed -= OnChange);
    }
}

public sealed class OnlineTrigger : OnlineTriggerBase
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "net.online", Title = Strings.Get("Trigger_NetOnline_Title"), Category = TP.CatNetwork, Icon = "GlobeArrowUp24",
        Summary = Strings.Get("Trigger_NetOnline_Title"),
        Description = Strings.Get("Trigger_NetOnline_Description"),
        OutputKind = ContentKind.Date, OutputDescription = Strings.Get("Trigger_NetOnline_OutputDescription"),
        Parameters = Array.Empty<ParameterDescriptor>(),
    };
    public override TriggerDescriptor Descriptor => D;
    protected override bool FireWhenOnline => true;
}

public sealed class OfflineTrigger : OnlineTriggerBase
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "net.offline", Title = Strings.Get("Trigger_NetOffline_Title"), Category = TP.CatNetwork, Icon = "GlobeProhibited24",
        Summary = Strings.Get("Trigger_NetOffline_Title"),
        Description = Strings.Get("Trigger_NetOffline_Description"),
        OutputKind = ContentKind.Date, OutputDescription = Strings.Get("Trigger_NetOffline_OutputDescription"),
        Parameters = Array.Empty<ParameterDescriptor>(),
    };
    public override TriggerDescriptor Descriptor => D;
    protected override bool FireWhenOnline => false;
}

// ---- power -----------------------------------------------------------------------------------

public abstract class PowerSourceTriggerBase : TriggerProvider
{
    protected abstract bool FireWhenPlugged { get; }

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var env = TP.Env;
        void OnChange((PowerState Old, PowerState New) c)
        {
            if (c.Old.OnAc == c.New.OnAc || c.New.OnAc != FireWhenPlugged) return;
            var output = c.New.BatteryPercent is { } p ? ContentValue.Number(p) : ContentValue.Text(FireWhenPlugged ? "Plugged in" : "On battery");
            sink.Fire(output, FireWhenPlugged ? Strings.Get("Trigger_NetOffline_Fired_TheChargerWasPlugged") : Strings.Get("Trigger_NetOffline_Fired_TheChargerWasUnplugged"));
        }
        return TP.Watch(env.Power, () => env.Power.Changed += OnChange, () => env.Power.Changed -= OnChange);
    }
}

public sealed class PluggedInTrigger : PowerSourceTriggerBase
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "power.pluggedIn", Title = Strings.Get("Trigger_PowerPluggedIn_Title"), Category = TP.CatPower, Icon = "PlugConnected24",
        Summary = Strings.Get("Trigger_PowerPluggedIn_Title"),
        Description = Strings.Get("Trigger_PowerPluggedIn_Description"),
        OutputKind = ContentKind.Number, OutputDescription = Strings.Get("Trigger_PowerPluggedIn_OutputDescription"),
        Parameters = Array.Empty<ParameterDescriptor>(),
    };
    public override TriggerDescriptor Descriptor => D;
    protected override bool FireWhenPlugged => true;
}

public sealed class UnpluggedTrigger : PowerSourceTriggerBase
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "power.unplugged", Title = Strings.Get("Trigger_PowerUnplugged_Title"), Category = TP.CatPower, Icon = "PlugDisconnected24",
        Summary = Strings.Get("Trigger_PowerUnplugged_Title"),
        Description = Strings.Get("Trigger_PowerUnplugged_Description"),
        OutputKind = ContentKind.Number, OutputDescription = Strings.Get("Trigger_PowerPluggedIn_OutputDescription"),
        Parameters = Array.Empty<ParameterDescriptor>(),
    };
    public override TriggerDescriptor Descriptor => D;
    protected override bool FireWhenPlugged => false;
}

internal static class PowerPlanApis
{
    /// <summary>Swapped for a fake in tests.</summary>
    public static AutomationPortal.Core.Platform.IPowerPlanApi Current { get; set; } = new AutomationPortal.Core.Platform.WindowsPowerPlanApi();
}

public sealed class PowerPlanChangedTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "power.plan", Title = Strings.Get("Trigger_PowerPlan_Title"), Category = TP.CatPower, Icon = "Flash24",
        Summary = Strings.Get("Trigger_PowerPlan_Summary"),
        Description = Strings.Get("Trigger_PowerPlan_Description"),
        OutputKind = ContentKind.Text, OutputDescription = Strings.Get("Trigger_PowerPlan_OutputDescription"),
        Parameters = new[]
        {
            ParameterDescriptor.DynamicChoice("plan", Strings.Get("Trigger_PowerPlan_Param_Plan_Label"), (_, _) =>
                Task.FromResult<IReadOnlyList<Choice>>(new[] { new Choice("", Strings.Get("Trigger_PowerPlan_Param_Plan_Choice_X")) }
                    .Concat(PowerPlanApis.Current.List().Select(p => new Choice(p.Name, p.Name))).ToList()), ""),
        },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var api = PowerPlanApis.Current;
        var wanted = config.GetChoice("plan", "").Trim();
        var filter = new AutomationPortal.Core.Platform.PowerPlanChangeFilter(api.GetActive()?.Id ?? Guid.Empty);
        return api.Subscribe(id =>
        {
            if (!filter.Accept(id)) return;
            var name = api.List().FirstOrDefault(p => p.Id == id)?.Name ?? id.ToString();
            if (!ConditionLogic.PlanMatches(name, wanted)) return;
            sink.Fire(ContentValue.Text(name), Strings.Format("Trigger_PowerPlan_Fired_Changed", name));
        });
    }
}

public sealed class BatteryLevelTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "power.battery", Title = Strings.Get("Trigger_PowerBattery_Title"), Category = TP.CatPower, Icon = "Battery224",
        Summary = Strings.Get("Trigger_PowerBattery_Summary"),
        Description = Strings.Get("Trigger_PowerBattery_Description"),
        OutputKind = ContentKind.Number, OutputDescription = Strings.Get("Trigger_PowerPluggedIn_OutputDescription"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("direction", Strings.Get("Trigger_PowerBattery_Param_Direction_Label"), "below", new Choice("below", Strings.Get("Trigger_PowerBattery_Param_Direction_Choice_Below")), new Choice("above", Strings.Get("Trigger_PowerBattery_Param_Direction_Choice_Above"))),
            ParameterDescriptor.Number("percent", Strings.Get("Trigger_PowerBattery_Param_Percent_Label"), 20, 1, 99, Strings.Get("Trigger_PowerBattery_Param_Percent_Unit")),
        },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var env = TP.Env;
        var below = config.GetChoice("direction", "below") != "above";
        var level = Math.Clamp(config.GetInt("percent", 20), 1, 99);
        void OnChange((PowerState Old, PowerState New) c)
        {
            if (c.Old.BatteryPercent is not { } was || c.New.BatteryPercent is not { } now) return;
            var crossed = below ? was >= level && now < level : was <= level && now > level;
            if (crossed) sink.Fire(ContentValue.Number(now), Strings.Format("Trigger_Battery_Fired_Level", now));
        }
        return TP.Watch(env.Power, () => env.Power.Changed += OnChange, () => env.Power.Changed -= OnChange);
    }
}

public sealed class BatterySaverTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "power.saver", Title = Strings.Get("Trigger_PowerSaver_Title"), Category = TP.CatPower, Icon = "BatterySaver24",
        Summary = Strings.Get("Trigger_PowerSaver_Summary"),
        Description = Strings.Get("Trigger_PowerSaver_Description"),
        OutputKind = ContentKind.Number, OutputDescription = Strings.Get("Trigger_PowerPluggedIn_OutputDescription"),
        Parameters = new[] { ParameterDescriptor.ChoiceOf("state", Strings.Get("Trigger_PowerSaver_Param_State_Label"), "on", new Choice("on", Strings.Get("Trigger_PowerSaver_Param_State_Choice_On")), new Choice("off", Strings.Get("Trigger_PowerSaver_Param_State_Choice_Off"))) },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var env = TP.Env;
        var wantOn = config.GetChoice("state", "on") != "off";
        void OnChange((PowerState Old, PowerState New) c)
        {
            if (c.Old.SaverOn == c.New.SaverOn || c.New.SaverOn != wantOn) return;
            sink.Fire(c.New.BatteryPercent is { } p ? ContentValue.Number(p) : ContentValue.Text(wantOn ? "On" : "Off"), Strings.Get(wantOn ? "Trigger_Saver_Fired_On" : "Trigger_Saver_Fired_Off"));
        }
        return TP.Watch(env.Power, () => env.Power.Changed += OnChange, () => env.Power.Changed -= OnChange);
    }
}
