using Flowmate.Core;

namespace Flowmate.Triggers;

public sealed class FileChangedTrigger : TriggerProvider, IFileWatchTrigger
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "file.changed", Title = "When files change in a folder", Category = TP.CatFiles, Icon = "FolderArrowRight24",
        Summary = "When files are {events} in {folder}",
        Description = "Runs when files are added, changed or removed in a folder. Files that are still downloading are ignored, Flow Mate waits until each file has finished arriving, and a burst of files becomes one run with all of them.",
        OutputKind = ContentKind.File, OutputDescription = "The files that were added, changed or removed",
        Parameters = new[]
        {
            new ParameterDescriptor { Key = "events", Label = "Files that are", Type = ParamType.List, AllowVariables = false, AllowAskEachTime = false,
                Choices = new Choice[] { new("added", "added"), new("changed", "changed"), new("removed", "removed") }, Default = ParamValue.List("added") },
            new ParameterDescriptor { Key = "folder", Label = "Folder", Type = ParamType.Folder, Required = true, AllowVariables = false, AllowAskEachTime = false },
            ParameterDescriptor.ChoiceOf("kind", "Only", "any", FileMatchFilter.KindChoices.ToArray()),
            ParameterDescriptor.Text("extensions", "Only these file types", null, "pdf, docx, jpg") with { AllowVariables = false, AllowAskEachTime = false },
            ParameterDescriptor.Bool("subfolders", "Include subfolders"),
            ParameterDescriptor.Number("wait", "Group files that arrive within (seconds)", 3, 1, 60),
        },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var folder = config.GetFolder("folder");
        if (folder is null) { sink.ReportProblem("Choose a folder to watch."); return new DisposableAction(() => { }); }
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
                if (!Directory.Exists(folder)) { sink.ReportProblem($"Couldn't find the folder \"{folder}\". Flow Mate will start watching as soon as it exists."); return; }
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
                    sink.ReportProblem($"Flow Mate can't watch \"{folder}\": {ex.Message}");
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
/// runaway breaker pauses an automation that keeps running only on files from its own previous run.
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

    public void OnCreated(string path) { Note(path); _batcher.OnCreated(path); }
    public void OnChanged(string path) { Note(path); _batcher.OnChanged(path); }
    public void OnDeleted(string path) { Note(path); _batcher.OnDeleted(path); }
    public void OnRenamed(string oldPath, string newPath) { Note(oldPath); Note(newPath); _batcher.OnRenamed(oldPath, newPath); }

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
            if (!_batcher.HasPending) lock (_gate) _duringRun.Clear();
            return;
        }

        var held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Keep(string path)
        {
            lock (_gate)
            {
                if (!_duringRun.Remove(path)) return true;
            }
            held.Add(path);
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
        var verb = removed.Count > 0 && paths.Count == removed.Count ? "removed from" : added.Count > 0 && _events.Contains("added") ? "added to" : "changed in";
        // Only files that appeared during the last run: a command or program the run started may be feeding the folder.
        Window?.NoteSelfFed(paths.All(held.Contains));
        _sink.Fire(ContentValue.Files(paths), paths.Count == 1 ? $"{Path.GetFileName(paths[0])} was {verb} {_folderName}" : $"{paths.Count} files were {verb} {_folderName}");
    }
}

public sealed class DriveConnectedTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "drive.connected", Title = "When a USB drive is plugged in or removed", Category = TP.CatFiles, Icon = "UsbStick24",
        Summary = "When a drive is {event}",
        Description = "Runs when a USB stick, memory card or external drive appears or disappears in File Explorer.",
        OutputKind = ContentKind.Folder, OutputDescription = "The drive (for example E:\\)",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("event", "When a drive is", "connected", new Choice("connected", "plugged in"), new Choice("removed", "removed")),
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
            sink.Fire(ContentValue.Folder(e.Root), wantAdded ? $"{name} was plugged in" : $"{e.Root.TrimEnd('\\')} was removed");
        }
        return TP.Watch(env.Drives, () => env.Drives.Changed += OnDrive, () => env.Drives.Changed -= OnDrive);
    }
}

// ---- network ---------------------------------------------------------------------------------

public abstract class WifiTriggerBase : TriggerProvider
{
    protected static ParameterDescriptor NetworkParam() => ParameterDescriptor.DynamicChoice("network", "Network", async (services, ct) =>
    {
        var list = new List<Choice> { new("", "any network") };
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
                sink.Fire(ContentValue.Text(joined), $"You joined {joined}");
            else if (!Joined && c.Old.WifiName is { } left && Matches(left))
                sink.Fire(ContentValue.Text(left), $"You left {left}");
        }
        return TP.Watch(env.Network, () => env.Network.Changed += OnChange, () => env.Network.Changed -= OnChange);
    }
}

public sealed class WifiJoinedTrigger : WifiTriggerBase
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "net.wifiJoined", Title = "When I join Wi-Fi", Category = TP.CatNetwork, Icon = "Wifi124",
        Summary = "When I join {network}",
        Description = "Runs when the PC connects to a Wi-Fi network (any network, or one you pick). Windows 11 may hide Wi-Fi names from apps unless Location is on (Settings > Privacy & security > Location); Flow Mate then falls back to the saved network name.",
        OutputKind = ContentKind.Text, OutputDescription = "The Wi-Fi network name",
        Parameters = new[] { NetworkParam() },
    };
    public override TriggerDescriptor Descriptor => D;
    protected override bool Joined => true;
}

public sealed class WifiLeftTrigger : WifiTriggerBase
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "net.wifiLeft", Title = "When I leave Wi-Fi", Category = TP.CatNetwork, Icon = "WifiOff24",
        Summary = "When I leave {network}",
        Description = "Runs when the PC disconnects from a Wi-Fi network (any network, or one you pick).",
        OutputKind = ContentKind.Text, OutputDescription = "The Wi-Fi network name",
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
            sink.Fire(TP.Now(), FireWhenOnline ? "The internet is back" : "The internet dropped");
        }
        return TP.Watch(env.Network, () => env.Network.Changed += OnChange, () => env.Network.Changed -= OnChange);
    }
}

public sealed class OnlineTrigger : OnlineTriggerBase
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "net.online", Title = "When the internet comes back", Category = TP.CatNetwork, Icon = "GlobeArrowUp24",
        Summary = "When the internet comes back",
        Description = "Runs when the PC gets an internet connection again after being offline for a few seconds or more.",
        OutputKind = ContentKind.Date, OutputDescription = "The date and time it came back",
        Parameters = Array.Empty<ParameterDescriptor>(),
    };
    public override TriggerDescriptor Descriptor => D;
    protected override bool FireWhenOnline => true;
}

public sealed class OfflineTrigger : OnlineTriggerBase
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "net.offline", Title = "When the internet drops", Category = TP.CatNetwork, Icon = "GlobeProhibited24",
        Summary = "When the internet drops",
        Description = "Runs when the PC loses its internet connection for a few seconds or more.",
        OutputKind = ContentKind.Date, OutputDescription = "The date and time it dropped",
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
            sink.Fire(output, FireWhenPlugged ? "The charger was plugged in" : "The charger was unplugged");
        }
        return TP.Watch(env.Power, () => env.Power.Changed += OnChange, () => env.Power.Changed -= OnChange);
    }
}

public sealed class PluggedInTrigger : PowerSourceTriggerBase
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "power.pluggedIn", Title = "When the charger is plugged in", Category = TP.CatPower, Icon = "PlugConnected24",
        Summary = "When the charger is plugged in",
        Description = "Runs when the laptop starts charging.",
        OutputKind = ContentKind.Number, OutputDescription = "The battery level in percent",
        Parameters = Array.Empty<ParameterDescriptor>(),
    };
    public override TriggerDescriptor Descriptor => D;
    protected override bool FireWhenPlugged => true;
}

public sealed class UnpluggedTrigger : PowerSourceTriggerBase
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "power.unplugged", Title = "When the charger is unplugged", Category = TP.CatPower, Icon = "PlugDisconnected24",
        Summary = "When the charger is unplugged",
        Description = "Runs when the laptop switches to battery power.",
        OutputKind = ContentKind.Number, OutputDescription = "The battery level in percent",
        Parameters = Array.Empty<ParameterDescriptor>(),
    };
    public override TriggerDescriptor Descriptor => D;
    protected override bool FireWhenPlugged => false;
}

internal static class PowerPlanApis
{
    /// <summary>Swapped for a fake in tests.</summary>
    public static Flowmate.Core.Platform.IPowerPlanApi Current { get; set; } = new Flowmate.Core.Platform.WindowsPowerPlanApi();
}

public sealed class PowerPlanChangedTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "power.plan", Title = "When the power plan changes", Category = TP.CatPower, Icon = "Flash24",
        Summary = "When the power plan changes to {plan}",
        Description = "Runs when Windows switches to another power plan, from Settings, a shortcut or another app. Leave the plan on 'any plan' to run for every change.",
        OutputKind = ContentKind.Text, OutputDescription = "The name of the new power plan",
        Parameters = new[]
        {
            ParameterDescriptor.DynamicChoice("plan", "Plan", (_, _) =>
                Task.FromResult<IReadOnlyList<Choice>>(new[] { new Choice("", "any plan") }
                    .Concat(PowerPlanApis.Current.List().Select(p => new Choice(p.Name, p.Name))).ToList()), ""),
        },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var api = PowerPlanApis.Current;
        var wanted = config.GetChoice("plan", "").Trim();
        var filter = new Flowmate.Core.Platform.PowerPlanChangeFilter(api.GetActive()?.Id ?? Guid.Empty);
        return api.Subscribe(id =>
        {
            if (!filter.Accept(id)) return;
            var name = api.List().FirstOrDefault(p => p.Id == id)?.Name ?? id.ToString();
            if (!ConditionLogic.PlanMatches(name, wanted)) return;
            sink.Fire(ContentValue.Text(name), $"The power plan changed to {name}");
        });
    }
}

public sealed class BatteryLevelTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "power.battery", Title = "When the battery level changes", Category = TP.CatPower, Icon = "Battery224",
        Summary = "When the battery goes {direction} {percent}%",
        Description = "Runs once when the battery drops below, or rises above, the level you choose.",
        OutputKind = ContentKind.Number, OutputDescription = "The battery level in percent",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("direction", "Goes", "below", new Choice("below", "below"), new Choice("above", "above")),
            ParameterDescriptor.Number("percent", "Battery level", 20, 1, 99, "%"),
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
            if (crossed) sink.Fire(ContentValue.Number(now), $"The battery is {now}%");
        }
        return TP.Watch(env.Power, () => env.Power.Changed += OnChange, () => env.Power.Changed -= OnChange);
    }
}

public sealed class BatterySaverTrigger : TriggerProvider
{
    private static readonly TriggerDescriptor D = new()
    {
        Id = "power.saver", Title = "When Battery saver changes", Category = TP.CatPower, Icon = "BatterySaver24",
        Summary = "When Battery saver turns {state}",
        Description = "Runs when Windows turns Battery saver on or off.",
        OutputKind = ContentKind.Number, OutputDescription = "The battery level in percent",
        Parameters = new[] { ParameterDescriptor.ChoiceOf("state", "Turns", "on", new Choice("on", "on"), new Choice("off", "off")) },
    };
    public override TriggerDescriptor Descriptor => D;

    public override IDisposable Start(TriggerConfig config, ITriggerSink sink)
    {
        var env = TP.Env;
        var wantOn = config.GetChoice("state", "on") != "off";
        void OnChange((PowerState Old, PowerState New) c)
        {
            if (c.Old.SaverOn == c.New.SaverOn || c.New.SaverOn != wantOn) return;
            sink.Fire(c.New.BatteryPercent is { } p ? ContentValue.Number(p) : ContentValue.Text(wantOn ? "On" : "Off"), $"Battery saver turned {(wantOn ? "on" : "off")}");
        }
        return TP.Watch(env.Power, () => env.Power.Changed += OnChange, () => env.Power.Changed -= OnChange);
    }
}
