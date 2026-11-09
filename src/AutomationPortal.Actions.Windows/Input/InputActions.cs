using AutomationPortal.Actions.Windows.Apps;
using AutomationPortal.Actions.Windows.Native;

namespace AutomationPortal.Actions.Windows.Input;

public sealed class TypeTextAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "input.type", Title = "Type Text", Category = ActionCategories.KeyboardAndMouse, Icon = "Keyboard24",
        Keywords = new[] { "keyboard", "write", "enter text", "paste", "auto type", "sendkeys" },
        Summary = "Type {text}",
        Description = "Types text into whatever app is in front, one key at a time, as if you typed it. Click into the right box first (use Switch to App, then Click).",
        InputKinds = new[] { ContentKind.Text }, HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.MultilineText("text", "Text") with { IsInput = true, Required = true },
            ParameterDescriptor.Number("delayMs", "Pause between letters", 0, 0, 2000, "ms"),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var text = await ctx.GetTextAsync("text");
        if (text.Length == 0) throw new ActionException("There's no text to type. Enter some text first.");
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
        Id = "input.keys", Title = "Press Keys", Category = ActionCategories.KeyboardAndMouse, Icon = "KeyboardShift24",
        Keywords = new[] { "keyboard", "shortcut", "hotkey", "ctrl", "alt", "press", "key combination", "enter", "tab" },
        Summary = "Press {keys}",
        Description = "Presses a keyboard shortcut in the app that is in front, like Ctrl+C, Alt+Tab, Win+D, Enter or F5. Write several shortcuts with spaces between them, like Ctrl+K Ctrl+C.",
        HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.Text("keys", "Keys", null, "Ctrl+Shift+T") with { Required = true, Hint = "Use + between keys. Examples: Ctrl+C, Alt+Tab, Win+D, Enter, F5, Ctrl+Shift+Esc." },
            ParameterDescriptor.Number("times", "Times", 1, 1, 100),
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
        ParameterDescriptor.Number("x", "Across", null, null, null, "px"),
        ParameterDescriptor.Number("y", "Down", null, null, null, "px"),
        ParameterDescriptor.ChoiceOf("relativeTo", "Measured from", "screen",
            allowCurrent
                ? new[] { new Choice("screen", "the top-left of the screen"), new Choice("window", "the top-left of an app's window"), new Choice("mouse", "where the mouse is now") }
                : new[] { new Choice("screen", "the top-left of the screen"), new Choice("window", "the top-left of an app's window") }),
        P.App("app", "App", required: false) with { VisibleWhen = new VisibleWhen("relativeTo", "window") },
    };

    /// <summary>Resolves the target in physical screen pixels. Null when no coordinates were given (stay at the mouse position).</summary>
    public static async Task<(int X, int Y)?> ResolveAsync(ActionContext ctx)
    {
        var x = await ctx.GetNumberOrNullAsync("x");
        var y = await ctx.GetNumberOrNullAsync("y");
        if (x is null && y is null) return null;
        if (x is null || y is null) throw new ActionException("Enter both the 'Across' and 'Down' numbers, or leave both empty to use the mouse where it is.");
        var mode = await ctx.GetChoiceAsync("relativeTo", "screen");
        var ox = (int)Math.Round(x.Value);
        var oy = (int)Math.Round(y.Value);
        switch (mode)
        {
            case "window":
                {
                    var app = await ctx.GetAppAsync("app") ?? throw new ActionException("Choose which app's window the position is measured from.");
                    var window = await Task.Run(() => WindowService.WindowsOf(app).FirstOrDefault(), ctx.CancellationToken).ConfigureAwait(false)
                                 ?? throw new ActionException($"{SwitchToAppAction.DisplayName(app)} isn't open, so there's no window to click in. Open it first.");
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
        Id = "input.click", Title = "Click", Category = ActionCategories.KeyboardAndMouse, Icon = "CursorClick24",
        Keywords = new[] { "mouse", "tap", "press button", "double click", "right click", "automate" },
        Summary = "Click at {x}, {y}",
        Description = "Moves the mouse to a spot on the screen (or in an app's window) and clicks. Leave the numbers empty to click where the mouse is.",
        HasOutput = false,
        Parameters = MousePoints.Parameters(allowCurrent: false).Concat(new[]
        {
            ParameterDescriptor.ChoiceOf("button", "Button", "left", new Choice("left", "left"), new Choice("right", "right"), new Choice("middle", "middle")),
            ParameterDescriptor.Number("clicks", "Number of clicks", 1, 1, 3),
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
        Id = "input.moveMouse", Title = "Move Mouse", Category = ActionCategories.KeyboardAndMouse, Icon = "Cursor24",
        Keywords = new[] { "mouse", "pointer", "cursor", "position", "hover" },
        Summary = "Move the mouse to {x}, {y}",
        Description = "Moves the mouse pointer to a spot on the screen, in an app's window, or by a distance from where it is now.",
        HasOutput = false,
        Parameters = MousePoints.Parameters(allowCurrent: true),
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var point = await MousePoints.ResolveAsync(ctx) ?? throw new ActionException("Enter where to move the mouse: the 'Across' and 'Down' numbers.");
        InputSender.MoveTo(point.X, point.Y);
        return ContentValue.Empty;
    }
}

public sealed class ScrollAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "input.scroll", Title = "Scroll", Category = ActionCategories.KeyboardAndMouse, Icon = "ArrowSort24",
        Keywords = new[] { "mouse wheel", "scroll down", "scroll up", "page" },
        Summary = "Scroll {direction} by {amount}",
        Description = "Turns the mouse wheel over whatever is under the mouse pointer.",
        HasOutput = false,
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("direction", "Direction", "down", new Choice("down", "down"), new Choice("up", "up"), new Choice("left", "left"), new Choice("right", "right")),
            ParameterDescriptor.Number("amount", "Amount", 3, 1, 100, "notches"),
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
