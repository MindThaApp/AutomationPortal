using AutomationPortal.Actions.Windows.Apps;
using AutomationPortal.Actions.Windows.Native;

namespace AutomationPortal.Actions.Windows.Input;

public sealed class TypeTextAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "input.type", Title = Strings.Get("TypeTextAction_Title"), Category = ActionCategories.KeyboardAndMouse, Icon = "Keyboard24",
        Keywords = new[] { "keyboard", "write", "enter text", "paste", "auto type", "sendkeys" },
        Summary = Strings.Get("TypeTextAction_Summary"),
        Description = Strings.Get("TypeTextAction_Description"),
        InputKinds = new[] { ContentKind.Text }, HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.MultilineText("text", Strings.Get("TypeTextAction_Param_Text_Label")) with { IsInput = true, Required = true },
            ParameterDescriptor.Number("delayMs", Strings.Get("TypeTextAction_Param_DelayMs_Label"), 0, 0, 2000, "ms"),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var text = await ctx.GetTextAsync("text");
        if (text.Length == 0) throw new ActionException(Strings.Get("TypeTextAction_Error_There_S_No_Text_To_Type"));
        var delay = (int)Math.Clamp(await ctx.GetNumberAsync("delayMs", 0), 0, 2000);
        await InputSender.ReleaseHeldModifiersAsync(400, ctx.CancellationToken).ConfigureAwait(false);
        await InputSender.TypeAsync(text, delay, ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Empty;
    }
}

public sealed class PressKeysAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "input.keys", Title = Strings.Get("PressKeysAction_Title"), Category = ActionCategories.KeyboardAndMouse, Icon = "KeyboardShift24",
        Keywords = new[] { "keyboard", "shortcut", "hotkey", "ctrl", "alt", "press", "key combination", "enter", "tab" },
        Summary = Strings.Get("PressKeysAction_Summary"),
        Description = Strings.Get("PressKeysAction_Description"),
        HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.Text("keys", Strings.Get("PressKeysAction_Param_Keys_Label"), null, "Ctrl+Shift+T") with { Required = true, Hint = Strings.Get("PressKeysAction_Param_Keys_Hint") },
            ParameterDescriptor.Number("times", Strings.Get("PressKeysAction_Param_Times_Label"), 1, 1, 100),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var text = await ctx.GetTextAsync("keys");
        IReadOnlyList<KeyChord> chords;
        try { chords = KeyCombo.Parse(text); }
        catch (FormatException ex) { throw new ActionException(ex.Message, ex); }
        var times = (int)Math.Clamp(await ctx.GetNumberAsync("times", 1), 1, 100);
        await InputSender.ReleaseHeldModifiersAsync(400, ctx.CancellationToken).ConfigureAwait(false);
        await InputSender.PressAsync(chords, times, ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Empty;
    }
}

/// <summary>Finds screen coordinates for the click and move actions.</summary>
internal static class MousePoints
{
    public static ParameterDescriptor[] Parameters(bool allowCurrent) => new[]
    {
        ParameterDescriptor.Number("x", Strings.Get("MousePoints_Param_X_Label"), null, null, null, "px"),
        ParameterDescriptor.Number("y", Strings.Get("MousePoints_Param_Y_Label"), null, null, null, "px"),
        ParameterDescriptor.ChoiceOf("relativeTo", Strings.Get("MousePoints_Param_RelativeTo_Label"), "screen",
            allowCurrent
                ? new[] { new Choice("screen", Strings.Get("MousePoints_Param_RelativeTo_Choice_Screen")), new Choice("window", Strings.Get("MousePoints_Param_RelativeTo_Choice_Window")), new Choice("mouse", Strings.Get("MousePoints_Param_RelativeTo_Choice_Mouse")) }
                : new[] { new Choice("screen", Strings.Get("MousePoints_Param_RelativeTo_Choice_Screen")), new Choice("window", Strings.Get("MousePoints_Param_RelativeTo_Choice_Window")) }),
        P.App("app", Strings.Get("MousePoints_Param_App_Label"), required: false) with { VisibleWhen = new VisibleWhen("relativeTo", "window") },
    };

    /// <summary>Resolves the target in physical screen pixels. Null when no coordinates were given (stay at the mouse position).</summary>
    public static async Task<(int X, int Y)?> ResolveAsync(ActionContext ctx)
    {
        var x = await ctx.GetNumberOrNullAsync("x");
        var y = await ctx.GetNumberOrNullAsync("y");
        if (x is null && y is null) return null;
        if (x is null || y is null) throw new ActionException(Strings.Get("MousePoints_Error_Enter_Both_The_Across_And"));
        var mode = await ctx.GetChoiceAsync("relativeTo", "screen");
        var ox = (int)Math.Round(x.Value);
        var oy = (int)Math.Round(y.Value);
        switch (mode)
        {
            case "window":
                {
                    var app = await ctx.GetAppAsync("app") ?? throw new ActionException(Strings.Get("MousePoints_Error_Choose_Which_App_S_Window_The"));
                    var window = await Task.Run(() => WindowService.WindowsOf(app).FirstOrDefault(), ctx.CancellationToken).ConfigureAwait(false)
                                 ?? throw new ActionException(Strings.Format("MousePoints_Error_0_Isn_T_Open_So_There_S", SwitchToAppAction.DisplayName(app)));
                    await Task.Run(() => WindowService.BringToFront(window.Handle), ctx.CancellationToken).ConfigureAwait(false);
                    await Task.Delay(150, ctx.CancellationToken).ConfigureAwait(false);
                    RECT r;
                    using (DpiScope.PerMonitorV2()) r = WindowService.GetRect(window.Handle);
                    return (r.Left + ox, r.Top + oy);
                }
            case "mouse":
                {
                    var (cx, cy) = InputSender.CursorPosition();
                    return (cx + ox, cy + oy);
                }
            default: return (ox, oy);
        }
    }
}

public sealed class ClickAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "input.click", Title = Strings.Get("ClickAction_Title"), Category = ActionCategories.KeyboardAndMouse, Icon = "CursorClick24",
        Keywords = new[] { "mouse", "tap", "press button", "double click", "right click", "automate" },
        Summary = Strings.Get("ClickAction_Summary"),
        Description = Strings.Get("ClickAction_Description"),
        HasOutput = false,
        Parameters = MousePoints.Parameters(allowCurrent: false).Concat(new[]
        {
            ParameterDescriptor.ChoiceOf("button", Strings.Get("ClickAction_Param_Button_Label"), "left", new Choice("left", Strings.Get("ClickAction_Param_Button_Choice_Left")), new Choice("right", Strings.Get("ClickAction_Param_Button_Choice_Right")), new Choice("middle", Strings.Get("ClickAction_Param_Button_Choice_Middle"))),
            ParameterDescriptor.Number("clicks", Strings.Get("ClickAction_Param_Clicks_Label"), 1, 1, 3),
        }).ToArray(),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var point = await MousePoints.ResolveAsync(ctx);
        var button = await ctx.GetChoiceAsync("button", "left") switch { "right" => MouseButton.Right, "middle" => MouseButton.Middle, _ => MouseButton.Left };
        var clicks = (int)Math.Clamp(await ctx.GetNumberAsync("clicks", 1), 1, 3);
        if (point is { } p) InputSender.MoveTo(p.X, p.Y);
        await InputSender.ClickAsync(button, clicks, ctx.CancellationToken).ConfigureAwait(false);
        return ContentValue.Empty;
    }
}

public sealed class MoveMouseAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "input.moveMouse", Title = Strings.Get("MoveMouseAction_Title"), Category = ActionCategories.KeyboardAndMouse, Icon = "Cursor24",
        Keywords = new[] { "mouse", "pointer", "cursor", "position", "hover" },
        Summary = Strings.Get("MoveMouseAction_Summary"),
        Description = Strings.Get("MoveMouseAction_Description"),
        HasOutput = false,
        Parameters = MousePoints.Parameters(allowCurrent: true),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var point = await MousePoints.ResolveAsync(ctx) ?? throw new ActionException(Strings.Get("MoveMouseAction_Error_Enter_Where_To_Move_The"));
        InputSender.MoveTo(point.X, point.Y);
        return ContentValue.Empty;
    }
}

public sealed class ScrollAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "input.scroll", Title = Strings.Get("ScrollAction_Title"), Category = ActionCategories.KeyboardAndMouse, Icon = "ArrowSort24",
        Keywords = new[] { "mouse wheel", "scroll down", "scroll up", "page" },
        Summary = Strings.Get("ScrollAction_Summary"),
        Description = Strings.Get("ScrollAction_Description"),
        HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("direction", Strings.Get("ScrollAction_Param_Direction_Label"), "down", new Choice("down", Strings.Get("ScrollAction_Param_Direction_Choice_Down")), new Choice("up", Strings.Get("ScrollAction_Param_Direction_Choice_Up")), new Choice("left", Strings.Get("ScrollAction_Param_Direction_Choice_Left")), new Choice("right", Strings.Get("ScrollAction_Param_Direction_Choice_Right"))),
            ParameterDescriptor.Number("amount", Strings.Get("ScrollAction_Param_Amount_Label"), 3, 1, 100, "notches"),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var direction = await ctx.GetChoiceAsync("direction", "down");
        var amount = (int)Math.Clamp(await ctx.GetNumberAsync("amount", 3), 1, 100);
        var horizontal = direction is "left" or "right";
        var notches = direction is "up" or "right" ? amount : -amount;
        for (var i = 0; i < Math.Abs(notches); i++)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            InputSender.Scroll(Math.Sign(notches), horizontal);
            if (i < Math.Abs(notches) - 1) await Task.Delay(15, ctx.CancellationToken).ConfigureAwait(false);
        }
        return ContentValue.Empty;
    }
}
