using System.Runtime.InteropServices;
using AutomationPortal.Actions.Windows.Devices;
using AutomationPortal.Actions.Windows.Native;

namespace AutomationPortal.Actions.Windows.Apps;

internal static class WindowTargets
{
    /// <summary>Finds the window an action should work on, from its "target" parameters.</summary>
    public static async Task<WindowInfo> ResolveAsync(ActionContext ctx)
    {
        var target = await ctx.GetChoiceAsync("target", "front");
        var ct = ctx.CancellationToken;
        switch (target)
        {
            case "app":
                {
                    var app = await ctx.RequireAppAsync();
                    var windows = await Task.Run(() => WindowService.WindowsOf(app), ct).ConfigureAwait(false);
                    return windows.FirstOrDefault()
                           ?? throw new ActionException(Strings.Format("WindowTargets_Error_0_Doesn_T_Have_An_Open", SwitchToAppAction.DisplayName(app)));
                }
            case "input":
                {
                    var content = await ctx.GetContentAsync("window");
                    var item = content.Items.OfType<WindowItem>().FirstOrDefault()
                               ?? throw new ActionException(Strings.Get("WindowTargets_Error_There_S_No_Window_To_Work"));
                    return await Task.Run(() => WindowService.Describe(new IntPtr(item.Handle)), ct).ConfigureAwait(false)
                           ?? throw new ActionException(Strings.Format("WindowTargets_Error_The_Window_0_Has_Been", item.Title));
                }
            default:
                return await Task.Run(() => WindowService.GetFront(), ct).ConfigureAwait(false)
                       ?? throw new ActionException(Strings.Get("WindowTargets_Error_There_S_No_Window_Open_To"));
        }
    }
}

public sealed class ArrangeWindowAction : ShortcutAction
{
    private static readonly Choice[] Arrangements =
    {
        new("maximize", Strings.Get("ArrangeWindowAction_Choice_Maximize")), new("minimize", Strings.Get("ArrangeWindowAction_Choice_Minimize")), new("restore", Strings.Get("ArrangeWindowAction_Choice_Restore")),
        new("left", Strings.Get("ArrangeWindowAction_Choice_Left")), new("right", Strings.Get("ArrangeWindowAction_Choice_Right")), new("top", Strings.Get("ArrangeWindowAction_Choice_Top")), new("bottom", Strings.Get("ArrangeWindowAction_Choice_Bottom")),
        new("topleft", Strings.Get("ArrangeWindowAction_Choice_Topleft")), new("topright", Strings.Get("ArrangeWindowAction_Choice_Topright")),
        new("bottomleft", Strings.Get("ArrangeWindowAction_Choice_Bottomleft")), new("bottomright", Strings.Get("ArrangeWindowAction_Choice_Bottomright")),
        new("leftthird", Strings.Get("ArrangeWindowAction_Choice_Leftthird")), new("middlethird", Strings.Get("ArrangeWindowAction_Choice_Middlethird")), new("rightthird", Strings.Get("ArrangeWindowAction_Choice_Rightthird")),
        new("center", Strings.Get("ArrangeWindowAction_Choice_Center")), new("nextmonitor", Strings.Get("ArrangeWindowAction_Choice_Nextmonitor")), new("prevmonitor", Strings.Get("ArrangeWindowAction_Choice_Prevmonitor")),
        new("custom", Strings.Get("ArrangeWindowAction_Choice_Custom")),
    };

    private static readonly ActionDescriptor D = new()
    {
        Id = "win.arrange", Title = Strings.Get("ArrangeWindowAction_Title"), Category = ActionCategories.AppsAndWindows, Icon = "WindowArrowUp24",
        Keywords = new[] { "snap", "maximize", "minimize", "resize", "move", "half", "corner", "monitor", "tile", "center" },
        Summary = Strings.Get("ArrangeWindowAction_Summary"),
        Description = Strings.Get("ArrangeWindowAction_Description"),
        OutputKind = ContentKind.Window, OutputName = Strings.Get("ArrangeWindowAction_OutputName"),
        Parameters = P.WindowTarget().Concat(new[]
        {
            ParameterDescriptor.ChoiceOf("arrangement", Strings.Get("ArrangeWindowAction_Param_Arrangement_Label"), "maximize", Arrangements),
            ParameterDescriptor.Number("x", Strings.Get("ArrangeWindowAction_Param_X_Label"), 0, null, null, "px") with { VisibleWhen = new VisibleWhen("arrangement", "custom") },
            ParameterDescriptor.Number("y", Strings.Get("ArrangeWindowAction_Param_Y_Label"), 0, null, null, "px") with { VisibleWhen = new VisibleWhen("arrangement", "custom") },
            ParameterDescriptor.Number("width", Strings.Get("ArrangeWindowAction_Param_Width_Label"), 0, 0, 20000, "px") with { VisibleWhen = new VisibleWhen("arrangement", "custom") },
            ParameterDescriptor.Number("height", Strings.Get("ArrangeWindowAction_Param_Height_Label"), 0, 0, 20000, "px") with { VisibleWhen = new VisibleWhen("arrangement", "custom") },
        }).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    internal static SnapPosition? SnapFor(string id) => id switch
    {
        "left" => SnapPosition.LeftHalf, "right" => SnapPosition.RightHalf, "top" => SnapPosition.TopHalf, "bottom" => SnapPosition.BottomHalf,
        "topleft" => SnapPosition.TopLeft, "topright" => SnapPosition.TopRight, "bottomleft" => SnapPosition.BottomLeft, "bottomright" => SnapPosition.BottomRight,
        "leftthird" => SnapPosition.LeftThird, "middlethird" => SnapPosition.MiddleThird, "rightthird" => SnapPosition.RightThird,
        _ => null,
    };

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var window = await WindowTargets.ResolveAsync(ctx);
        var arrangement = await ctx.GetChoiceAsync("arrangement", "maximize");
        var (cx, cy, cw, ch) = (0, 0, 0, 0);
        if (arrangement == "custom")
        {
            cx = (int)Math.Round(await ctx.GetNumberAsync("x", 0));
            cy = (int)Math.Round(await ctx.GetNumberAsync("y", 0));
            cw = (int)Math.Round(await ctx.GetNumberAsync("width", 0));
            ch = (int)Math.Round(await ctx.GetNumberAsync("height", 0));
        }
        var ok = await Task.Run(() => Apply(window.Handle, arrangement, cx, cy, cw, ch), ctx.CancellationToken).ConfigureAwait(false);
        if (!ok)
            throw new ActionException(Strings.Format("ArrangeWindowAction_Error_Windows_Wouldn_T_Move_0_If", window.Title));
        return ContentValue.From(window.ToItem());
    }

    private static bool Apply(IntPtr h, string arrangement, int cx, int cy, int cw, int ch)
    {
        using var _ = DpiScope.PerMonitorV2();
        switch (arrangement)
        {
            case "maximize": Win32.ShowWindow(h, Win32.SW_SHOWMAXIMIZED); return true;
            case "minimize": Win32.ShowWindow(h, Win32.SW_MINIMIZE); return true;
            case "restore": Win32.ShowWindow(h, Win32.SW_RESTORE); return true;
        }

        var monitors = DisplayService.GetMonitors();
        if (monitors.Count == 0) return false;
        var wasMaximized = Win32.IsZoomed(h);
        if (wasMaximized || Win32.IsIconic(h)) Win32.ShowWindow(h, Win32.SW_RESTORE);

        var rect = DisplayService.ToBox(WindowService.GetRect(h));
        var visible = DisplayService.ToBox(WindowService.GetVisibleFrame(h));
        var works = monitors.Select(m => m.WorkArea).ToList();
        var current = WindowGeometry.MonitorIndexFor(visible, works);
        var work = works[current];

        Box target;
        var reMaximize = false;
        if (SnapFor(arrangement) is { } snap) target = WindowGeometry.Snap(work, snap);
        else
            switch (arrangement)
            {
                case "center":
                    target = wasMaximized ? WindowGeometry.Snap(work, SnapPosition.Center) : WindowGeometry.CenterIn(work, visible);
                    break;
                case "nextmonitor":
                case "prevmonitor":
                    {
                        var next = WindowGeometry.NextMonitor(works, current, arrangement == "nextmonitor" ? 1 : -1);
                        target = WindowGeometry.MoveToMonitor(visible, work, works[next]);
                        reMaximize = wasMaximized;
                        break;
                    }
                case "custom":
                    target = new Box(cx, cy, cw > 0 ? cw : visible.Width, ch > 0 ? ch : visible.Height);
                    break;
                default: return false;
            }

        var placed = WindowGeometry.AdjustForInvisibleBorders(target, rect, visible);
        var ok = Win32.SetWindowPos(h, IntPtr.Zero, placed.X, placed.Y, placed.Width, placed.Height, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        if (ok && reMaximize) Win32.ShowWindow(h, Win32.SW_SHOWMAXIMIZED);
        return ok;
    }
}

public sealed class KeepWindowOnTopAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "win.onTop", Title = Strings.Get("KeepWindowOnTopAction_Title"), Category = ActionCategories.AppsAndWindows, Icon = "PinOff24",
        Keywords = new[] { "always on top", "pin", "float", "topmost" },
        Summary = Strings.Get("KeepWindowOnTopAction_Summary"),
        Description = Strings.Get("KeepWindowOnTopAction_Description"),
        OutputKind = ContentKind.Boolean, OutputName = Strings.Get("KeepWindowOnTopAction_OutputName"),
        Parameters = P.WindowTarget().Append(P.OnOffToggle("state", Strings.Get("KeepWindowOnTopAction_Param_State_Label"), "on", "off")).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var window = await WindowTargets.ResolveAsync(ctx);
        var state = await ctx.GetChoiceAsync("state", "toggle");
        var result = await Task.Run(() =>
        {
            var topmost = (Win32.GetWindowLongPtr(window.Handle, Win32.GWL_EXSTYLE).ToInt64() & Win32.WS_EX_TOPMOST) != 0;
            var want = Toggle.Resolve(state, topmost);
            var ok = Win32.SetWindowPos(window.Handle, want ? Win32.HWND_TOPMOST : Win32.HWND_NOTOPMOST, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            if (!ok) throw new ActionException(Strings.Format("KeepWindowOnTopAction_Error_Windows_Wouldn_T_Change_0_If", window.Title));
            return want;
        }, ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Bool(result);
    }
}

public sealed class ShowDesktopAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "win.showDesktop", Title = Strings.Get("ShowDesktopAction_Title"), Category = ActionCategories.AppsAndWindows, Icon = "DesktopArrowRight24",
        Keywords = new[] { "minimize all", "hide windows", "desktop", "win+d" },
        Summary = Strings.Get("ShowDesktopAction_Summary"),
        Description = Strings.Get("ShowDesktopAction_Description"),
        HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("action", Strings.Get("ShowDesktopAction_Param_Action_Label"), "show", new Choice("show", Strings.Get("ShowDesktopAction_Param_Action_Choice_Show")), new Choice("restore", Strings.Get("ShowDesktopAction_Param_Action_Choice_Restore"))),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var show = await ctx.GetChoiceAsync("action", "show") != "restore";
        await Sta.Run(() =>
        {
            var type = Type.GetTypeFromProgID("Shell.Application") ?? throw new ActionException(Strings.Get("ShowDesktopAction_Error_Windows_Desktop_Controls_Aren_T_Available"));
            dynamic shell = Activator.CreateInstance(type)!;
            try { if (show) shell.MinimizeAll(); else shell.UndoMinimizeALL(); }
            finally { Marshal.FinalReleaseComObject(shell); }
        }).ConfigureAwait(false);
        return ContentValue.Empty;
    }
}

public sealed class CloseWindowAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "win.close", Title = Strings.Get("CloseWindowAction_Title"), Category = ActionCategories.AppsAndWindows, Icon = "Dismiss24",
        Keywords = new[] { "close", "shut", "x", "tab", "window" },
        Summary = Strings.Get("CloseWindowAction_Summary"),
        Description = Strings.Get("CloseWindowAction_Description"),
        HasOutput = false,
        Parameters = P.WindowTarget(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var window = await WindowTargets.ResolveAsync(ctx);
        if (!Win32.PostMessageW(window.Handle, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
            throw new ActionException(Strings.Format("CloseWindowAction_Error_Windows_Wouldn_T_Close_0_If", window.Title));
        return ContentValue.Empty;
    }
}

public sealed class GetWindowsAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "win.list", Title = Strings.Get("GetWindowsAction_Title"), Category = ActionCategories.AppsAndWindows, Icon = "WindowApps24",
        Keywords = new[] { "open windows", "list", "titles", "tabs" },
        Summary = Strings.Get("GetWindowsAction_Summary"),
        Description = Strings.Get("GetWindowsAction_Description"),
        OutputKind = ContentKind.Window, OutputName = Strings.Get("GetWindowsAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("scope", Strings.Get("GetWindowsAction_Param_Scope_Label"), "all", new Choice("all", Strings.Get("GetWindowsAction_Param_Scope_Choice_All")), new Choice("app", Strings.Get("GetWindowsAction_Param_Scope_Choice_App"))),
            P.App() with { VisibleWhen = new VisibleWhen("scope", "app") },
            ParameterDescriptor.Bool("includeMinimized", Strings.Get("GetWindowsAction_Param_IncludeMinimized_Label"), true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var scope = await ctx.GetChoiceAsync("scope", "all");
        var includeMin = await ctx.GetBoolAsync("includeMinimized", true);
        var app = scope == "app" ? await ctx.RequireAppAsync() : null;
        var windows = await Task.Run(() =>
        {
            var list = app is null ? WindowService.GetWindows() : WindowService.WindowsOf(app).ToList();
            return list.Where(w => w.ProcessId != Environment.ProcessId && (includeMin || !w.IsMinimized)).ToList();
        }, ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.FromItems(windows.Select(w => (ContentItem)w.ToItem()));
    }
}

public sealed class SaveWindowLayoutAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "win.saveLayout", Title = Strings.Get("SaveWindowLayoutAction_Title"), Category = ActionCategories.AppsAndWindows, Icon = "LayoutColumnTwo24",
        Keywords = new[] { "workspace", "arrangement", "remember windows", "positions", "setup" },
        Summary = Strings.Get("SaveWindowLayoutAction_Summary"),
        Description = Strings.Get("SaveWindowLayoutAction_Description"),
        OutputKind = ContentKind.Number, OutputName = Strings.Get("SaveWindowLayoutAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Text("name", Strings.Get("SaveWindowLayoutAction_Param_Name_Label"), "My layout") with { Required = true },
            ParameterDescriptor.Bool("includeMinimized", Strings.Get("SaveWindowLayoutAction_Param_IncludeMinimized_Label"), false),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var name = (await ctx.GetTextAsync("name")).Trim();
        if (name.Length == 0) throw new ActionException(Strings.Get("SaveWindowLayoutAction_Error_Give_The_Layout_A_Name"));
        var includeMin = await ctx.GetBoolAsync("includeMinimized", false);
        var layout = await Task.Run(() => WindowLayouts.Capture(name, includeMin), ctx.CancellationToken).ConfigureAwait(false);
        if (layout.Windows.Count == 0) throw new ActionException(Strings.Get("SaveWindowLayoutAction_Error_There_Are_No_Open_Windows"));
        new WindowLayoutStore(ctx.Services.Paths.RoamingRoot).Save(layout);
        return ContentValue.Number(layout.Windows.Count);
    }
}

public sealed class RestoreWindowLayoutAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "win.restoreLayout", Title = Strings.Get("RestoreWindowLayoutAction_Title"), Category = ActionCategories.AppsAndWindows, Icon = "LayoutColumnTwo24",
        Keywords = new[] { "workspace", "arrangement", "positions", "setup", "restore windows" },
        Summary = Strings.Get("RestoreWindowLayoutAction_Summary"),
        Description = Strings.Get("RestoreWindowLayoutAction_Description"),
        OutputKind = ContentKind.Number, OutputName = Strings.Get("RestoreWindowLayoutAction_OutputName"),
        Parameters = new[]
        {
            new ParameterDescriptor
            {
                Key = "name", Label = Strings.Get("RestoreWindowLayoutAction_Param_IncludeMinimized_Label"), Type = ParamType.Choice, Required = true,
                ChoicesProvider = (services, ct) => Task.FromResult<IReadOnlyList<Choice>>(
                    new WindowLayoutStore(services.Paths.RoamingRoot).Load().Select(l => new Choice(l.Name, l.Name)).ToList()),
            },
            ParameterDescriptor.Bool("launchMissing", Strings.Get("RestoreWindowLayoutAction_Param_LaunchMissing_Label"), true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var name = (await ctx.GetChoiceAsync("name")).Trim();
        if (name.Length == 0) throw new ActionException(Strings.Get("RestoreWindowLayoutAction_Error_Choose_Which_Layout_To_Restore"));
        var store = new WindowLayoutStore(ctx.Services.Paths.RoamingRoot);
        var layout = store.Find(name) ?? throw new ActionException(Strings.Format("RestoreWindowLayoutAction_Error_There_S_No_Saved_Layout_Called", name));
        var launch = await ctx.GetBoolAsync("launchMissing", true);
        var count = await WindowLayouts.RestoreAsync(layout, launch, ctx.ReportProgress, ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Number(count);
    }
}
