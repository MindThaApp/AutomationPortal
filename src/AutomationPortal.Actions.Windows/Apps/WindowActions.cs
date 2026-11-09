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
                           ?? throw new ActionException($"{SwitchToAppAction.DisplayName(app)} doesn't have an open window. Open it first.");
                }
            case "input":
                {
                    var content = await ctx.GetContentAsync("window");
                    var item = content.Items.OfType<WindowItem>().FirstOrDefault()
                               ?? throw new ActionException("There's no window to work on. Put a Get Windows or Switch to App action before this one.");
                    return await Task.Run(() => WindowService.Describe(new IntPtr(item.Handle)), ct).ConfigureAwait(false)
                           ?? throw new ActionException($"The window '{item.Title}' has been closed or hidden.");
                }
            default:
                return await Task.Run(() => WindowService.GetFront(), ct).ConfigureAwait(false)
                       ?? throw new ActionException("There's no window open to work on.");
        }
    }
}

public sealed class ArrangeWindowAction : ShortcutAction
{
    private static readonly Choice[] Arrangements =
    {
        new("maximize", "Maximize"), new("minimize", "Minimize"), new("restore", "Restore"),
        new("left", "Snap to left half"), new("right", "Snap to right half"), new("top", "Snap to top half"), new("bottom", "Snap to bottom half"),
        new("topleft", "Snap to top-left corner"), new("topright", "Snap to top-right corner"),
        new("bottomleft", "Snap to bottom-left corner"), new("bottomright", "Snap to bottom-right corner"),
        new("leftthird", "Snap to left third"), new("middlethird", "Snap to middle third"), new("rightthird", "Snap to right third"),
        new("center", "Center"), new("nextmonitor", "Move to next screen"), new("prevmonitor", "Move to previous screen"),
        new("custom", "Set size & position"),
    };

    private static readonly ActionDescriptor D = new()
    {
        Id = "win.arrange", Title = "Arrange Window", Category = ActionCategories.AppsAndWindows, Icon = "WindowArrowUp24",
        Keywords = new[] { "snap", "maximize", "minimize", "resize", "move", "half", "corner", "monitor", "tile", "center" },
        Summary = "{arrangement} for {target}",
        Description = "Maximizes, minimizes, snaps (halves, corners, thirds), centers, moves to another screen, or sets the exact size and position of a window.",
        OutputKind = ContentKind.Window, OutputName = "Window",
        Parameters = P.WindowTarget().Concat(new[]
        {
            ParameterDescriptor.ChoiceOf("arrangement", "Arrangement", "maximize", Arrangements),
            ParameterDescriptor.Number("x", "Left edge", 0, null, null, "px") with { VisibleWhen = new VisibleWhen("arrangement", "custom") },
            ParameterDescriptor.Number("y", "Top edge", 0, null, null, "px") with { VisibleWhen = new VisibleWhen("arrangement", "custom") },
            ParameterDescriptor.Number("width", "Width (0 = keep)", 0, 0, 20000, "px") with { VisibleWhen = new VisibleWhen("arrangement", "custom") },
            ParameterDescriptor.Number("height", "Height (0 = keep)", 0, 0, 20000, "px") with { VisibleWhen = new VisibleWhen("arrangement", "custom") },
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
            throw new ActionException($"Windows wouldn't move '{window.Title}'. If the app is running as administrator, Automation Portal can't control its window.");
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
        Id = "win.onTop", Title = "Keep Window on Top", Category = ActionCategories.AppsAndWindows, Icon = "PinOff24",
        Keywords = new[] { "always on top", "pin", "float", "topmost" },
        Summary = "Keep {target} on top: {state}",
        Description = "Makes a window stay above all other windows, or lets it go back to normal. Gives back true when the window is on top afterwards.",
        OutputKind = ContentKind.Boolean, OutputName = "Is On Top",
        Parameters = P.WindowTarget().Append(P.OnOffToggle("state", "Keep on top", "on", "off")).ToArray(),
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
            if (!ok) throw new ActionException($"Windows wouldn't change '{window.Title}'. If the app is running as administrator, Automation Portal can't control its window.");
            return want;
        }, ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Bool(result);
    }
}

public sealed class ShowDesktopAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "win.showDesktop", Title = "Show Desktop", Category = ActionCategories.AppsAndWindows, Icon = "DesktopArrowRight24",
        Keywords = new[] { "minimize all", "hide windows", "desktop", "win+d" },
        Summary = "{action}",
        Description = "Minimizes every window to show the desktop, or brings the windows back.",
        HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("action", "Action", "show", new Choice("show", "Show the desktop"), new Choice("restore", "Bring the windows back")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var show = await ctx.GetChoiceAsync("action", "show") != "restore";
        await Sta.Run(() =>
        {
            var type = Type.GetTypeFromProgID("Shell.Application") ?? throw new ActionException("Windows' desktop controls aren't available on this PC.");
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
        Id = "win.close", Title = "Close Window", Category = ActionCategories.AppsAndWindows, Icon = "Dismiss24",
        Keywords = new[] { "close", "shut", "x", "tab", "window" },
        Summary = "Close {target}",
        Description = "Closes a window, like clicking its X button. The app may ask to save your work first.",
        HasOutput = false,
        Parameters = P.WindowTarget(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var window = await WindowTargets.ResolveAsync(ctx);
        if (!Win32.PostMessageW(window.Handle, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
            throw new ActionException($"Windows wouldn't close '{window.Title}'. If the app is running as administrator, Automation Portal can't control its window.");
        return ContentValue.Empty;
    }
}

public sealed class GetWindowsAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "win.list", Title = "Get Windows", Category = ActionCategories.AppsAndWindows, Icon = "WindowApps24",
        Keywords = new[] { "open windows", "list", "titles", "tabs" },
        Summary = "Get {scope}",
        Description = "Gets the windows that are open, front window first. Each one has a title and the app it belongs to.",
        OutputKind = ContentKind.Window, OutputName = "Windows",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("scope", "Which windows", "all", new Choice("all", "all open windows"), new Choice("app", "the windows of an app")),
            P.App() with { VisibleWhen = new VisibleWhen("scope", "app") },
            ParameterDescriptor.Bool("includeMinimized", "Include minimized windows", true),
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
        Id = "win.saveLayout", Title = "Save Window Layout", Category = ActionCategories.AppsAndWindows, Icon = "LayoutColumnTwo24",
        Keywords = new[] { "workspace", "arrangement", "remember windows", "positions", "setup" },
        Summary = "Save the window layout as {name}",
        Description = "Remembers which windows are open and where they sit on your screens, under a name. Use Restore Window Layout to put them back.",
        OutputKind = ContentKind.Number, OutputName = "Windows Saved",
        Parameters = new[]
        {
            ParameterDescriptor.Text("name", "Layout name", "My layout") with { Required = true },
            ParameterDescriptor.Bool("includeMinimized", "Include minimized windows", false),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var name = (await ctx.GetTextAsync("name")).Trim();
        if (name.Length == 0) throw new ActionException("Give the layout a name so you can restore it later.");
        var includeMin = await ctx.GetBoolAsync("includeMinimized", false);
        var layout = await Task.Run(() => WindowLayouts.Capture(name, includeMin), ctx.CancellationToken).ConfigureAwait(false);
        if (layout.Windows.Count == 0) throw new ActionException("There are no open windows to save.");
        new WindowLayoutStore(ctx.Services.Paths.RoamingRoot).Save(layout);
        return ContentValue.Number(layout.Windows.Count);
    }
}

public sealed class RestoreWindowLayoutAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "win.restoreLayout", Title = "Restore Window Layout", Category = ActionCategories.AppsAndWindows, Icon = "LayoutColumnTwo24",
        Keywords = new[] { "workspace", "arrangement", "positions", "setup", "restore windows" },
        Summary = "Restore the window layout {name}",
        Description = "Puts windows back where a saved layout had them. Apps that aren't open can be opened first.",
        OutputKind = ContentKind.Number, OutputName = "Windows Restored",
        Parameters = new[]
        {
            new ParameterDescriptor
            {
                Key = "name", Label = "Layout", Type = ParamType.Choice, Required = true,
                ChoicesProvider = (services, ct) => Task.FromResult<IReadOnlyList<Choice>>(
                    new WindowLayoutStore(services.Paths.RoamingRoot).Load().Select(l => new Choice(l.Name, l.Name)).ToList()),
            },
            ParameterDescriptor.Bool("launchMissing", "Open apps that aren't running", true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var name = (await ctx.GetChoiceAsync("name")).Trim();
        if (name.Length == 0) throw new ActionException("Choose which layout to restore.");
        var store = new WindowLayoutStore(ctx.Services.Paths.RoamingRoot);
        var layout = store.Find(name) ?? throw new ActionException($"There's no saved layout called '{name}'. Save one first with Save Window Layout.");
        var launch = await ctx.GetBoolAsync("launchMissing", true);
        var count = await WindowLayouts.RestoreAsync(layout, launch, ctx.ReportProgress, ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Number(count);
    }
}
