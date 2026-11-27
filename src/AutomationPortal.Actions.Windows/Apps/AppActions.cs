using System.Diagnostics;
using AutomationPortal.Actions.Windows.Native;

namespace AutomationPortal.Actions.Windows.Apps;

public sealed class OpenAppAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "app.open", Title = Strings.Get("OpenAppAction_Title"), Category = ActionCategories.AppsAndWindows, Icon = "Open24",
        Keywords = new[] { "launch", "start", "run", "program", "store app" },
        Summary = Strings.Get("OpenAppAction_Summary"),
        Description = Strings.Get("OpenAppAction_Description"),
        OutputKind = ContentKind.App, OutputName = Strings.Get("OpenAppAction_OutputName"),
        ImportRiskFor = OpenRisk,
        Parameters = new[]
        {
            P.App(),
            ParameterDescriptor.ChoiceOf("ifOpen", Strings.Get("OpenAppAction_Param_IfOpen_Label"), "front", new Choice("front", Strings.Get("OpenAppAction_Param_IfOpen_Choice_Front")), new Choice("again", Strings.Get("OpenAppAction_Param_IfOpen_Choice_Again"))),
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
        TestRootGuard.BlockSystemChange("opening apps");
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
        Id = "app.quit", Title = Strings.Get("QuitAppAction_Title"), Category = ActionCategories.AppsAndWindows, Icon = "DismissCircle24",
        Keywords = new[] { "close", "exit", "kill", "stop", "end task" },
        Summary = Strings.Get("QuitAppAction_Summary"),
        Description = Strings.Get("QuitAppAction_Description"),
        OutputKind = ContentKind.Boolean, OutputName = Strings.Get("QuitAppAction_OutputName"), DestructiveWhenOn = "force",
        Parameters = new[]
        {
            P.App(),
            ParameterDescriptor.Bool("force", Strings.Get("QuitAppAction_Param_Force_Label"), false),
            ParameterDescriptor.Number("wait", Strings.Get("QuitAppAction_Param_Wait_Label"), 5, 1, 120, "seconds"),
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
        Id = "app.isRunning", Title = Strings.Get("IsAppRunningAction_Title"), Category = ActionCategories.AppsAndWindows, Icon = "CheckmarkCircle24",
        Keywords = new[] { "open", "check", "running", "active" },
        Summary = Strings.Get("IsAppRunningAction_Summary"),
        Description = Strings.Get("IsAppRunningAction_Description"),
        OutputKind = ContentKind.Boolean, OutputName = Strings.Get("IsAppRunningAction_OutputName"),
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
        Id = "app.list", Title = Strings.Get("GetRunningAppsAction_Title"), Category = ActionCategories.AppsAndWindows, Icon = "AppsList24",
        Keywords = new[] { "open apps", "running", "programs", "task list" },
        Summary = Strings.Get("GetRunningAppsAction_Summary"),
        Description = Strings.Get("GetRunningAppsAction_Description"),
        InputKinds = Array.Empty<ContentKind>(), OutputKind = ContentKind.App, OutputName = Strings.Get("GetRunningAppsAction_OutputName"),
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
        Id = "app.front", Title = Strings.Get("GetFrontAppAction_Title"), Category = ActionCategories.AppsAndWindows, Icon = "WindowApps24",
        Keywords = new[] { "current app", "active window", "foreground", "focused" },
        Summary = Strings.Get("GetFrontAppAction_Summary"),
        Description = Strings.Get("GetFrontAppAction_Description"),
        OutputKind = ContentKind.App, OutputName = Strings.Get("GetFrontAppAction_OutputName"),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var w = await Task.Run(() => WindowService.GetFront(), ctx.CancellationToken).ConfigureAwait(false)
                ?? throw new ActionException(Strings.Get("GetFrontAppAction_Error_There_Is_No_App_Window"));
        return ContentValue.From(new AppItem(w.ExePath ?? w.ProcessName, w.AppName, w.ExePath));
    }
}

public sealed class SwitchToAppAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "app.switch", Title = Strings.Get("SwitchToAppAction_Title"), Category = ActionCategories.AppsAndWindows, Icon = "ArrowSwap24",
        Keywords = new[] { "focus", "bring to front", "activate", "go to" },
        Summary = Strings.Get("SwitchToAppAction_Summary"),
        Description = Strings.Get("SwitchToAppAction_Description"),
        OutputKind = ContentKind.Window, OutputName = Strings.Get("SwitchToAppAction_OutputName"),
        Parameters = new[]
        {
            P.App(),
            ParameterDescriptor.Bool("openIfNeeded", Strings.Get("SwitchToAppAction_Param_OpenIfNeeded_Label"), false),
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
            if (!open) throw new ActionException(Strings.Format("SwitchToAppAction_Error_0_Isn_T_Open_So_There_S", DisplayName(app)));
            await AppLauncher.LaunchAsync(app, null, ct).ConfigureAwait(false);
            for (var i = 0; i < 40 && window is null; i++)
            {
                await Task.Delay(250, ct).ConfigureAwait(false);
                window = await Task.Run(() => WindowService.WindowsOf(app).FirstOrDefault(), ct).ConfigureAwait(false);
            }
            if (window is null) throw new ActionException(Strings.Format("SwitchToAppAction_Error_Opened_0_But_No_Window", DisplayName(app)));
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
        Id = "app.waitFor", Title = Strings.Get("WaitForAppAction_Title"), Category = ActionCategories.AppsAndWindows, Icon = "HourglassHalf24",
        Keywords = new[] { "wait", "until", "pause", "launch", "closes" },
        Summary = Strings.Get("WaitForAppAction_Summary"),
        Description = Strings.Get("WaitForAppAction_Description"),
        OutputKind = ContentKind.App, OutputName = Strings.Get("WaitForAppAction_OutputName"),
        Parameters = new[]
        {
            P.App(),
            ParameterDescriptor.ChoiceOf("event", Strings.Get("WaitForAppAction_Param_Event_Label"), "opens", new Choice("opens", Strings.Get("WaitForAppAction_Param_Event_Choice_Opens")), new Choice("closes", Strings.Get("WaitForAppAction_Param_Event_Choice_Closes"))),
            ParameterDescriptor.Duration("timeout", Strings.Get("WaitForAppAction_Param_Timeout_Label"), 60),
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
                throw new ActionException(Strings.Format(wantOpen ? "WaitForAppAction_Error_DidNotOpen" : "WaitForAppAction_Error_DidNotClose", (int)timeout.TotalSeconds, SwitchToAppAction.DisplayName(app)));
            await Task.Delay(500, ctx.CancellationToken).ConfigureAwait(false);
        }
    }
}

public sealed class OpenFilesAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "app.openFile", Title = Strings.Get("OpenFilesAction_Title"), Category = ActionCategories.AppsAndWindows, Icon = "DocumentArrowRight24",
        Keywords = new[] { "open", "launch", "default app", "open with", "document" },
        Summary = Strings.Get("OpenFilesAction_Summary"),
        Description = Strings.Get("OpenFilesAction_Description"),
        InputKinds = new[] { ContentKind.File, ContentKind.Folder, ContentKind.Image }, OutputKind = ContentKind.File, OutputName = Strings.Get("OpenFilesAction_OutputName"),
        ImportRisk = ImportRisk.RunsPrograms,
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", Strings.Get("OpenFilesAction_Param_Files_Label"), isInput: true) with { Required = true },
            P.App("app", Strings.Get("OpenFilesAction_Param_App_Label"), required: false),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        TestRootGuard.BlockSystemChange("opening files");
        var items = (await ctx.GetFilesAsync("files", includeFolders: true)).ToList();
        if (items.Count == 0) throw new ActionException(Strings.Get("OpenFilesAction_Error_There_S_Nothing_To_Open_Choose"));
        foreach (var f in items)
            if (!f.Exists) throw new ActionException(Strings.Format("OpenFilesAction_Error_Couldn_T_Find_0_Check_It", f.FullName));
        var app = await ctx.GetAppAsync();
        if (app is null)
        {
            foreach (var f in items)
            {
                try { Process.Start(new ProcessStartInfo(f.Path) { UseShellExecute = true })?.Dispose(); }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    throw new ActionException(Strings.Format("OpenFilesAction_Error_Windows_Has_No_App_Set", f.FullName), ex);
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
        Id = "app.reveal", Title = Strings.Get("ShowInExplorerAction_Title"), Category = ActionCategories.AppsAndWindows, Icon = "FolderOpen24",
        Keywords = new[] { "reveal", "explorer", "locate", "folder", "find file" },
        Summary = Strings.Get("ShowInExplorerAction_Summary"),
        Description = Strings.Get("ShowInExplorerAction_Description"),
        InputKinds = new[] { ContentKind.File, ContentKind.Folder, ContentKind.Image }, OutputKind = ContentKind.File, OutputName = Strings.Get("ShowInExplorerAction_OutputName"),
        Parameters = new[] { ParameterDescriptor.FileParam("files", Strings.Get("ShowInExplorerAction_Param_Files_Label"), isInput: true) with { Required = true } },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var items = (await ctx.GetFilesAsync("files", includeFolders: true)).ToList();
        if (items.Count == 0) throw new ActionException(Strings.Get("ShowInExplorerAction_Error_There_S_Nothing_To_Show_Choose"));
        var missing = items.FirstOrDefault(i => !i.Exists);
        if (missing is not null) throw new ActionException(Strings.Format("ShowInExplorerAction_Error_Couldn_T_Find_0_Check_It", missing.FullName));
        await Sta.Run(() => ShellReveal.Select(items.Select(i => i.Path))).ConfigureAwait(false);
        return ContentValue.FromItems(items);
    }
}
