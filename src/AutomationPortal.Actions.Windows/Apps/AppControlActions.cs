using System.Runtime.InteropServices;
using AutomationPortal.Core.AppControl;
using AutomationPortal.Actions.Windows.Native;
using AutomationPortal.Interop;

namespace AutomationPortal.Actions.Windows.Apps;

/// <summary>
/// What the steps that work other apps use to reach them. Tests replace <see cref="Desktop"/> with a fake tree, so no real
/// window is touched. Replay goes through UI Automation patterns only: no mouse moves, no keystrokes.
/// </summary>
internal static class AppControlHost
{
    internal static Func<IUiDesktop> Desktop { get; set; } = () => new UiaDesktop();
    internal static Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = (t, ct) => Task.Delay(t, ct);

    internal sealed record Target(UiSelector Selector, string AppName, string Label, TimeSpan Timeout);

    internal static readonly ParameterDescriptor[] Common =
    {
        P.App() with { Label = Strings.Get("Target_Label") },
        ParameterDescriptor.Text("window", Strings.Get("Target_Param_Window_Label"), null, "* - Notepad") with
            { Hint = Strings.Get("Target_Param_Window_Hint") },
    };

    internal static readonly ParameterDescriptor Control =
        ParameterDescriptor.Text("control", Strings.Get("Target_Param_Control_Label")) with
            { Required = true, AllowAskEachTime = false, Hint = Strings.Get("Target_Param_Control_Hint") };

    internal static readonly ParameterDescriptor Label = ParameterDescriptor.Text("label", Strings.Get("Target_Param_Label_Label")) with { AllowAskEachTime = false };

    internal static readonly ParameterDescriptor Timeout = ParameterDescriptor.Duration("timeout", Strings.Get("Target_Param_Timeout_Label"), UiSteps.DefaultTimeoutSeconds);

    internal static async Task<Target> ReadAsync(ActionContext ctx, bool needsControl)
    {
        var app = await ctx.RequireAppAsync();
        var process = UiSelector.ProcessFromAppId(app.Id);
        if (process.Length == 0 || process.Contains('!'))
            throw new ActionException(Strings.Get("Target_Error_Choose_The_App_By_Its"));
        var appName = string.IsNullOrWhiteSpace(app.Name) ? process : app.Name;
        var window = (await ctx.GetTextAsync("window")).Trim();
        IReadOnlyList<UiNode> path = Array.Empty<UiNode>();
        if (needsControl)
        {
            path = UiPathText.Parse(await ctx.GetTextAsync("control"));
            if (path is null || path.Count == 0)
                throw new ActionException(Strings.Get("Target_Error_This_Step_Doesn_T_Say_Which"));
        }
        var label = (await ctx.GetTextAsync("label")).Trim();
        if (label.Length == 0) label = UiSteps.LabelOf(path.Count > 0 ? path[^1] : null);
        var timeout = await ctx.GetDurationAsync("timeout", TimeSpan.FromSeconds(UiSteps.DefaultTimeoutSeconds));
        if (timeout <= TimeSpan.Zero) timeout = TimeSpan.FromSeconds(UiSteps.DefaultTimeoutSeconds);
        if (timeout > TimeSpan.FromSeconds(UiSteps.MaxTimeoutSeconds)) timeout = TimeSpan.FromSeconds(UiSteps.MaxTimeoutSeconds);
        return new Target(new UiSelector(process, window, path), appName, label, timeout);
    }

    /// <summary>Finds the control (waiting for it), brings its window forward and runs <paramref name="act"/> on it, off the calling thread.</summary>
    internal static async Task<T> WithElementAsync<T>(ActionContext ctx, Target t, Func<IUiElement, T> act)
    {
        var desktop = await Task.Run(Desktop, ctx.CancellationToken).ConfigureAwait(false);
        ctx.ReportProgress(Strings.Format("Target_Progress", UiSteps.Quote(t.Label), t.AppName));
        var element = await UiReplay.WaitForAsync(desktop, t.Selector, t.AppName, t.Label, t.Timeout, ctx.CancellationToken, Delay).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                UiReplay.FocusWindow(element);
                return act(element);
            }, ctx.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or InvalidCastException)
        {
            throw new ActionException(Strings.Format("Target_Error_0_In_1_Stopped_Answering", UiSteps.Quote(t.Label), t.AppName), ex);
        }
    }
}

/// <summary>An open window the user can choose to record in.</summary>
public sealed record RecordableWindow(long Handle, int ProcessId, string ProcessName, string AppName, string Title);

/// <summary>The open windows offered by "Which app will you show?": one per window with a title, front first, not this app's own.</summary>
public static class RecordableWindows
{
    public static IReadOnlyList<RecordableWindow> List() =>
        WindowService.GetWindows()
            .Where(w => w.ProcessId != Environment.ProcessId && !string.IsNullOrWhiteSpace(w.Title))
            .Select(w => new RecordableWindow(w.Handle.ToInt64(), (int)w.ProcessId, w.ProcessName, w.AppName, w.Title))
            .DistinctBy(w => (w.ProcessId, w.Title))
            .Take(24)
            .ToList();

    /// <summary>Brings the chosen window forward when recording starts. Window calls only: no keystrokes or mouse moves.</summary>
    public static void Activate(RecordableWindow w)
    {
        var h = new IntPtr(w.Handle);
        if (Win32.IsIconic(h)) Win32.ShowWindow(h, 9); // SW_RESTORE
        WindowActivation.Run(() => Win32.SetForegroundWindow(h));
    }
}

public sealed class ClickInAppAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = AppControlIds.Click, Title = Strings.Get("ClickInAppAction_Title"), Category = ActionCategories.AppsAndWindows, Icon = "CursorClick24",
        Keywords = new[] { "click", "press", "button", "control", "record", "repeat", "app", "ui automation", "check box", "tab", "menu" },
        Summary = Strings.Get("ClickInAppAction_Summary"),
        Description = Strings.Get("ClickInAppAction_Description")
                      + Strings.Get("ClickInAppAction_Text"),
        HasOutput = false,
        ImportRisk = ImportRisk.ControlsApps,
        Parameters = AppControlHost.Common.Concat(new[] { AppControlHost.Label, AppControlHost.Control, AppControlHost.Timeout }).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var t = await AppControlHost.ReadAsync(ctx, needsControl: true);
        var did = await AppControlHost.WithElementAsync(ctx, t, e => UiReplay.Click(e, t.Label));
        ctx.Log($"{UiSteps.Quote(t.Label)} in {t.AppName}: {did}.");
        return ctx.Input;
    }
}

public sealed class TypeInAppAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = AppControlIds.Type, Title = Strings.Get("TypeInAppAction_Title"), Category = ActionCategories.AppsAndWindows, Icon = "TextField24",
        Keywords = new[] { "type", "fill", "field", "text box", "enter text", "form", "record", "repeat", "app", "ui automation" },
        Summary = Strings.Get("TypeInAppAction_Summary"),
        Description = Strings.Get("TypeInAppAction_Description")
                      + Strings.Get("TypeInAppAction_Text"),
        HasOutput = false,
        ImportRisk = ImportRisk.ControlsApps,
        Parameters = AppControlHost.Common.Concat(new[]
        {
            AppControlHost.Label,
            ParameterDescriptor.Text("text", Strings.Get("TypeInAppAction_Param_Text_Label")),
            ParameterDescriptor.Bool("secret", Strings.Get("TypeInAppAction_Param_Secret_Label")) with { AllowAskEachTime = false },
            AppControlHost.Control,
            AppControlHost.Timeout,
        }).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var t = await AppControlHost.ReadAsync(ctx, needsControl: true);
        var secret = await ctx.GetBoolAsync("secret");
        string text;
        if (secret || ctx.Step.Parameters.TryGetValue("text", out var v) && v is AskValue)
        {
            // Asked here rather than through the parameter, so the question names the field and a password stays hidden.
            var answer = await ctx.Services.UserInteraction.AskForInputAsync(
                Strings.Format(secret ? "AppControlActions_Ask_Password" : "AppControlActions_Ask_Text", UiSteps.Quote(t.Label), t.AppName),
                secret ? InputKind.Secret : InputKind.Text, null, ctx.CancellationToken);
            text = answer.AsText();
        }
        else text = await ctx.GetTextAsync("text");

        await AppControlHost.WithElementAsync(ctx, t, e => { UiReplay.Type(e, text, t.Label); return true; });
        ctx.Log(secret ? $"Filled in {UiSteps.Quote(t.Label)} in {t.AppName}." : $"Typed into {UiSteps.Quote(t.Label)} in {t.AppName}.");
        return ctx.Input;
    }
}

public sealed class PressEnterInAppAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = AppControlIds.PressEnter, Title = Strings.Get("PressEnterInAppAction_Title"), Category = ActionCategories.AppsAndWindows, Icon = "TextField24",
        Keywords = new[] { "enter", "return", "press", "record", "repeat", "app", "ui automation" },
        Summary = Strings.Get("PressEnterInAppAction_Summary"),
        Description = UiSteps.PressEnterExplanation,
        HasOutput = false,
        ImportRisk = ImportRisk.ControlsApps,
        Parameters = AppControlHost.Common.Concat(new[] { AppControlHost.Label, AppControlHost.Control, AppControlHost.Timeout }).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override Task<ContentValue> RunAsync(ActionContext ctx) =>
        throw new ActionException(UiSteps.PressEnterFailure);
}

public sealed class WaitForWindowAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = AppControlIds.WaitWindow, Title = Strings.Get("WaitForWindowAction_Title"), Category = ActionCategories.AppsAndWindows, Icon = "WindowNew24",
        Keywords = new[] { "wait", "window", "dialog", "appear", "open", "record", "repeat", "app" },
        Summary = Strings.Get("WaitForWindowAction_Summary"),
        Description = Strings.Get("WaitForWindowAction_Description")
                      + Strings.Get("WaitForWindowAction_Text"),
        HasOutput = false,
        ImportRisk = ImportRisk.ControlsApps,
        Parameters = AppControlHost.Common.Concat(new[] { AppControlHost.Timeout }).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var t = await AppControlHost.ReadAsync(ctx, needsControl: false);
        var label = string.IsNullOrWhiteSpace(t.Selector.WindowTitle) ? Strings.Format("AppControlActions_AWindow", t.AppName) : t.Selector.WindowTitle;
        await AppControlHost.WithElementAsync(ctx, t with { Label = label }, e => true);
        return ctx.Input;
    }
}
