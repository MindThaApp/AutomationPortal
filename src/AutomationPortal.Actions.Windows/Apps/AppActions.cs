using System.Diagnostics;
using Flowmate.Actions.Windows.Native;

namespace Flowmate.Actions.Windows.Apps;

public sealed class OpenAppAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "app.open", Title = "Open App", Category = ActionCategories.AppsAndWindows, Icon = "Open24",
        Keywords = new[] { "launch", "start", "run", "program", "store app" },
        Summary = "Open {app}",
        Description = "Starts an app installed on this PC, including Microsoft Store apps. If the app is already open it can be brought to the front instead of opening another one.",
        OutputKind = ContentKind.App, OutputName = "App",
        ImportRiskFor = OpenRisk,
        Parameters = new[]
        {
            P.App(),
            ParameterDescriptor.ChoiceOf("ifOpen", "If it is already open", "front", new Choice("front", "bring it to the front"), new Choice("again", "open it again")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    /// <summary>
    /// Opening a Store app or a program from Program Files or Windows is ordinary. Any other target (a program somewhere else, a path on
    /// another PC, a name or link Windows resolves itself, or a variable) could start something the user never installed.
    /// </summary>
    internal static ImportRisk OpenRisk(ActionInstance a) => a.Parameters.GetValueOrDefault("app") switch
    {
        null or AskValue => ImportRisk.None,
        AppValue app when string.IsNullOrWhiteSpace(app.Id) || app.IsStoreAppId || app.IsInSystemFolder => ImportRisk.None,
        _ => ImportRisk.RunsPrograms,
    };

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var app = await ctx.RequireAppAsync();
        if (await ctx.GetChoiceAsync("ifOpen", "front") == "front")
        {
            var windows = await Task.Run(() => WindowService.WindowsOf(app), ctx.CancellationToken).ConfigureAwait(false);
            if (windows.Count > 0)
            {
                await Task.Run(() => WindowService.BringToFront(windows[0].Handle), ctx.CancellationToken).ConfigureAwait(false);
                return ContentValue.From(app);
            }
        }
        return ContentValue.From(await AppLauncher.LaunchAsync(app, null, ctx.CancellationToken).ConfigureAwait(false));
    }
}

public sealed class QuitAppAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "app.quit", Title = "Quit App", Category = ActionCategories.AppsAndWindows, Icon = "DismissCircle24",
        Keywords = new[] { "close", "exit", "kill", "stop", "end task" },
        Summary = "Quit {app}",
        Description = "Closes an app politely, like clicking its close button, so it can ask to save your work. Optionally force it to stop if it hasn't closed in time (unsaved work is lost); forcing only stops that exact program, never Windows itself or programs it started. Gives back true when the app is no longer running.",
        OutputKind = ContentKind.Boolean, OutputName = "App Quit", DestructiveWhenOn = "force",
        Parameters = new[]
        {
            P.App(),
            ParameterDescriptor.Bool("force", "Force it to stop if it doesn't close", false),
            ParameterDescriptor.Number("wait", "Wait up to", 5, 1, 120, "seconds"),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var app = await ctx.RequireAppAsync();
        var force = await ctx.GetBoolAsync("force", false);
        var wait = TimeSpan.FromSeconds(Math.Clamp(await ctx.GetNumberAsync("wait", 5), 1, 120));
        var ct = ctx.CancellationToken;

        var gone = await Task.Run(async () =>
        {
            var processes = RunningApps.FindProcesses(app);
            try
            {
                if (processes.Count == 0 && WindowService.WindowsOf(app).Count == 0) return true;
                foreach (var w in WindowService.WindowsOf(app)) Win32.PostMessageW(w.Handle, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                foreach (var p in processes) { try { if (!p.HasExited && p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow(); } catch { } }

                var until = DateTime.UtcNow + wait;
                while (DateTime.UtcNow < until && processes.Any(Alive)) await Task.Delay(200, ct).ConfigureAwait(false);

                if (force)
                {
                    // Only the exact program (or Store package) chosen, never a look-alike by name or a Windows system process,
                    // and only that process: programs it started keep running.
                    foreach (var p in processes.Where(Alive))
                    {
                        try
                        {
                            if (AppMatcher.MatchesForForceStop(app, new AppCandidate(p.ProcessName, Win32.GetProcessPath((uint)p.Id), null, null, false)))
                                p.Kill(entireProcessTree: false);
                        }
                        catch { }
                    }
                    await Task.Delay(300, ct).ConfigureAwait(false);
                }
                return !processes.Any(Alive);
            }
            finally { foreach (var p in processes) p.Dispose(); }
        }, ct).ConfigureAwait(false);
        return ContentValue.Bool(gone);
    }

    private static bool Alive(Process p) { try { return !p.HasExited; } catch { return false; } }
}

public sealed class IsAppRunningAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "app.isRunning", Title = "Is App Running", Category = ActionCategories.AppsAndWindows, Icon = "CheckmarkCircle24",
        Keywords = new[] { "open", "check", "running", "active" },
        Summary = "Is {app} running",
        Description = "Checks whether an app is open right now. Gives back true or false, which you can use in an If.",
        OutputKind = ContentKind.Boolean, OutputName = "Is Running",
        Parameters = new[] { P.App() },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var app = await ctx.RequireAppAsync();
        return ContentValue.Bool(await Task.Run(() => RunningApps.IsRunning(app), ctx.CancellationToken).ConfigureAwait(false));
    }
}

public sealed class GetRunningAppsAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "app.list", Title = "Get Running Apps", Category = ActionCategories.AppsAndWindows, Icon = "AppsList24",
        Keywords = new[] { "open apps", "running", "programs", "task list" },
        Summary = "Get the apps that are open",
        Description = "Gets a list of the apps that have a window open right now, front app first.",
        InputKinds = Array.Empty<ContentKind>(), OutputKind = ContentKind.App, OutputName = "Running Apps",
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var apps = await Task.Run(RunningApps.GetRunningApps, ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.FromItems(apps.Select(a => (ContentItem)a.App));
    }
}

public sealed class GetFrontAppAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "app.front", Title = "Get Front App", Category = ActionCategories.AppsAndWindows, Icon = "WindowApps24",
        Keywords = new[] { "current app", "active window", "foreground", "focused" },
        Summary = "Get the app in front",
        Description = "Gets the app you are using right now (Flow Mate's own windows are ignored).",
        OutputKind = ContentKind.App, OutputName = "Front App",
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var w = await Task.Run(() => WindowService.GetFront(), ctx.CancellationToken).ConfigureAwait(false)
                ?? throw new ActionException("There is no app window open to get.");
        return ContentValue.From(new AppItem(w.ExePath ?? w.ProcessName, w.AppName, w.ExePath));
    }
}

public sealed class SwitchToAppAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "app.switch", Title = "Switch to App", Category = ActionCategories.AppsAndWindows, Icon = "ArrowSwap24",
        Keywords = new[] { "focus", "bring to front", "activate", "go to" },
        Summary = "Switch to {app}",
        Description = "Brings an open app's window to the front. Restores it first if it was minimised.",
        OutputKind = ContentKind.Window, OutputName = "Window",
        Parameters = new[]
        {
            P.App(),
            ParameterDescriptor.Bool("openIfNeeded", "Open the app if it isn't running", false),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var app = await ctx.RequireAppAsync();
        var open = await ctx.GetBoolAsync("openIfNeeded", false);
        var ct = ctx.CancellationToken;
        var window = await Task.Run(() => WindowService.WindowsOf(app).FirstOrDefault(), ct).ConfigureAwait(false);
        if (window is null)
        {
            if (!open) throw new ActionException($"{DisplayName(app)} isn't open, so there's no window to switch to. Open it first, or turn on \"Open the app if it isn't running\".");
            await AppLauncher.LaunchAsync(app, null, ct).ConfigureAwait(false);
            for (var i = 0; i < 40 && window is null; i++)
            {
                await Task.Delay(250, ct).ConfigureAwait(false);
                window = await Task.Run(() => WindowService.WindowsOf(app).FirstOrDefault(), ct).ConfigureAwait(false);
            }
            if (window is null) throw new ActionException($"Opened {DisplayName(app)} but no window appeared in time.");
        }
        var ok = await Task.Run(() => WindowService.BringToFront(window.Handle), ct).ConfigureAwait(false);
        if (!ok) ctx.Log($"Windows kept {DisplayName(app)} in the background (another app is holding the focus).");
        return ContentValue.From(window.ToItem());
    }

    internal static string DisplayName(AppItem app) => string.IsNullOrWhiteSpace(app.Name) ? app.Id : app.Name;
}

public sealed class WaitForAppAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "app.waitFor", Title = "Wait for App to Open/Close", Category = ActionCategories.AppsAndWindows, Icon = "HourglassHalf24",
        Keywords = new[] { "wait", "until", "pause", "launch", "closes" },
        Summary = "Wait until {app} {event}",
        Description = "Pauses the shortcut until an app opens or closes. If it takes longer than the time limit the shortcut stops with a message.",
        OutputKind = ContentKind.App, OutputName = "App",
        Parameters = new[]
        {
            P.App(),
            ParameterDescriptor.ChoiceOf("event", "Wait until it", "opens", new Choice("opens", "opens"), new Choice("closes", "closes")),
            ParameterDescriptor.Duration("timeout", "Give up after", 60),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var app = await ctx.RequireAppAsync();
        var wantOpen = await ctx.GetChoiceAsync("event", "opens") != "closes";
        var timeout = await ctx.GetDurationAsync("timeout", TimeSpan.FromSeconds(60));
        var started = DateTime.UtcNow;
        while (true)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            var open = await Task.Run(() => wantOpen ? WindowService.WindowsOf(app).Count > 0 : RunningApps.IsRunning(app), ctx.CancellationToken).ConfigureAwait(false);
            if (open == wantOpen) return ContentValue.From(app);
            if (timeout > TimeSpan.Zero && DateTime.UtcNow - started > timeout)
                throw new ActionException($"Waited {(int)timeout.TotalSeconds} seconds but {SwitchToAppAction.DisplayName(app)} didn't {(wantOpen ? "open" : "close")}.");
            await Task.Delay(500, ctx.CancellationToken).ConfigureAwait(false);
        }
    }
}

public sealed class OpenFilesAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "app.openFile", Title = "Open Files", Category = ActionCategories.AppsAndWindows, Icon = "DocumentArrowRight24",
        Keywords = new[] { "open", "launch", "default app", "open with", "document" },
        Summary = "Open {files} with {app}",
        Description = "Opens files or folders in their usual app, or in an app you choose.",
        InputKinds = new[] { ContentKind.File, ContentKind.Folder, ContentKind.Image }, OutputKind = ContentKind.File, OutputName = "Opened Files",
        ImportRisk = ImportRisk.RunsPrograms,
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", "Files", isInput: true) with { Required = true },
            P.App("app", "App (leave empty for the usual app)", required: false),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var items = (await ctx.GetFilesAsync("files", includeFolders: true)).ToList();
        if (items.Count == 0) throw new ActionException("There's nothing to open. Choose a file or folder first.");
        foreach (var f in items)
            if (!f.Exists) throw new ActionException($"Couldn't find '{f.FullName}'. Check it still exists.");
        var app = await ctx.GetAppAsync();
        if (app is null)
        {
            foreach (var f in items)
            {
                try { Process.Start(new ProcessStartInfo(f.Path) { UseShellExecute = true })?.Dispose(); }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    throw new ActionException($"Windows has no app set up to open '{f.FullName}'. Choose an app in this action, or pick a default in Settings > Apps > Default apps.", ex);
                }
            }
        }
        else
        {
            await AppLauncher.LaunchAsync(app, items.Select(f => f.Path).ToList(), ctx.CancellationToken).ConfigureAwait(false);
        }
        return ContentValue.FromItems(items);
    }
}

public sealed class ShowInExplorerAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "app.reveal", Title = "Show in File Explorer", Category = ActionCategories.AppsAndWindows, Icon = "FolderOpen24",
        Keywords = new[] { "reveal", "explorer", "locate", "folder", "find file" },
        Summary = "Show {files} in File Explorer",
        Description = "Opens File Explorer with the files selected (or opens the folder).",
        InputKinds = new[] { ContentKind.File, ContentKind.Folder, ContentKind.Image }, OutputKind = ContentKind.File, OutputName = "Files",
        Parameters = new[] { ParameterDescriptor.FileParam("files", "Files or folders", isInput: true) with { Required = true } },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var items = (await ctx.GetFilesAsync("files", includeFolders: true)).ToList();
        if (items.Count == 0) throw new ActionException("There's nothing to show. Choose a file or folder first.");
        var missing = items.FirstOrDefault(i => !i.Exists);
        if (missing is not null) throw new ActionException($"Couldn't find '{missing.FullName}'. Check it still exists.");
        await Sta.Run(() => ShellReveal.Select(items.Select(i => i.Path))).ConfigureAwait(false);
        return ContentValue.FromItems(items);
    }
}
