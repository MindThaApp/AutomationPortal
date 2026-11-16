using System.Runtime.InteropServices;
using AutomationPortal.Core.AppControl;
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
        P.App() with { Label = "App" },
        ParameterDescriptor.Text("window", "Window title", null, "* - Notepad") with
            { Hint = "The window's title. * stands for any text, so \"* - Notepad\" matches any Notepad document. Empty means any window of the app." },
    };

    internal static readonly ParameterDescriptor Control =
        ParameterDescriptor.Text("control", "Where the control is") with
            { Required = true, AllowAskEachTime = false, Hint = "Recorded for you: the path from the window down to the control, by id, name and type. No screen positions." };

    internal static readonly ParameterDescriptor Label = ParameterDescriptor.Text("label", "Control name") with { AllowAskEachTime = false };

    internal static readonly ParameterDescriptor Timeout = ParameterDescriptor.Duration("timeout", "Wait for it up to", UiSteps.DefaultTimeoutSeconds);

    internal static async Task<Target> ReadAsync(ActionContext ctx, bool needsControl)
    {
        var app = await ctx.RequireAppAsync();
        var process = UiSelector.ProcessFromAppId(app.Id);
        if (process.Length == 0 || process.Contains('!'))
            throw new ActionException("Choose the app by its program (for example notepad.exe). Recording the step again sets this for you.");
        var appName = string.IsNullOrWhiteSpace(app.Name) ? process : app.Name;
        var window = (await ctx.GetTextAsync("window")).Trim();
        IReadOnlyList<UiNode> path = Array.Empty<UiNode>();
        if (needsControl)
        {
            path = UiPathText.Parse(await ctx.GetTextAsync("control"));
            if (path is null || path.Count == 0)
                throw new ActionException("This step doesn't say which control to use. Record it again, or delete it.");
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
        ctx.ReportProgress($"Looking for {UiSteps.Quote(t.Label)} in {t.AppName}…");
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
            throw new ActionException($"{UiSteps.Quote(t.Label)} in {t.AppName} stopped answering, so the step didn't finish. Check the app and run it again.", ex);
        }
    }
}

public sealed class ClickInAppAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = AppControlIds.Click, Title = "Click a Button in an App", Category = ActionCategories.AppsAndWindows, Icon = "CursorClick24",
        Keywords = new[] { "click", "press", "button", "control", "record", "repeat", "app", "ui automation", "check box", "tab", "menu" },
        Summary = "Click {label} in {app}",
        Description = "Clicks a button, check box, tab, list item or menu in another app's window, the way the app itself offers it to "
                      + "accessibility tools. It never moves the mouse, so it works even if the window has moved. If the control isn't there "
                      + "within the time you set, the shortcut stops and says which control it couldn't find. Record what you do to make these steps.",
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
        Id = AppControlIds.Type, Title = "Type into a Field in an App", Category = ActionCategories.AppsAndWindows, Icon = "TextField24",
        Keywords = new[] { "type", "fill", "field", "text box", "enter text", "form", "record", "repeat", "app", "ui automation" },
        Summary = "Type {text} into {label} in {app}",
        Description = "Puts text into a field in another app's window, replacing what was there, the way the app offers it to accessibility "
                      + "tools. It never presses keys. Choose Ask each time to be asked what to type whenever it runs. For a password box "
                      + "it always asks, hides what you type, and never stores it. If the field isn't there within the time you set, the "
                      + "shortcut stops and says which field it couldn't find.",
        HasOutput = false,
        ImportRisk = ImportRisk.ControlsApps,
        Parameters = AppControlHost.Common.Concat(new[]
        {
            AppControlHost.Label,
            ParameterDescriptor.Text("text", "Text"),
            ParameterDescriptor.Bool("secret", "It's a password box (always ask, hide what's typed)") with { AllowAskEachTime = false },
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
                secret ? $"Type the password for {UiSteps.Quote(t.Label)} in {t.AppName}:" : $"What should be typed into {UiSteps.Quote(t.Label)} in {t.AppName}?",
                secret ? InputKind.Secret : InputKind.Text, null, ctx.CancellationToken);
            text = answer.AsText();
        }
        else text = await ctx.GetTextAsync("text");

        await AppControlHost.WithElementAsync(ctx, t, e => { UiReplay.Type(e, text, t.Label); return true; });
        ctx.Log(secret ? $"Filled in {UiSteps.Quote(t.Label)} in {t.AppName}." : $"Typed into {UiSteps.Quote(t.Label)} in {t.AppName}.");
        return ctx.Input;
    }
}

public sealed class WaitForWindowAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = AppControlIds.WaitWindow, Title = "Wait for a Window", Category = ActionCategories.AppsAndWindows, Icon = "WindowNew24",
        Keywords = new[] { "wait", "window", "dialog", "appear", "open", "record", "repeat", "app" },
        Summary = "Wait for {app} window {window}",
        Description = "Waits until a window of the app is open (and its title fits, when you give one), then brings it forward. If it "
                      + "doesn't appear in time, the shortcut stops and says which window it was waiting for.",
        HasOutput = false,
        ImportRisk = ImportRisk.ControlsApps,
        Parameters = AppControlHost.Common.Concat(new[] { AppControlHost.Timeout }).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var t = await AppControlHost.ReadAsync(ctx, needsControl: false);
        var label = string.IsNullOrWhiteSpace(t.Selector.WindowTitle) ? $"a {t.AppName} window" : t.Selector.WindowTitle;
        await AppControlHost.WithElementAsync(ctx, t with { Label = label }, e => true);
        return ctx.Input;
    }
}
