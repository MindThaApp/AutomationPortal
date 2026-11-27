using AutomationPortal.Actions.Windows.Native;

namespace AutomationPortal.Actions.Windows.Clipboard;

public sealed class GetClipboardAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        SensitiveOutput = true, Id = "clip.get", Title = Strings.Get("GetClipboardAction_Title"), Category = ActionCategories.Clipboard, Icon = "ClipboardPaste24",
        Keywords = new[] { "paste", "copied", "clipboard" },
        Summary = Strings.Get("GetClipboardAction_Summary"),
        Description = Strings.Get("GetClipboardAction_Description"),
        OutputName = Strings.Get("GetClipboardAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("as", Strings.Get("GetClipboardAction_Param_As_Label"), "anything", new Choice("anything", Strings.Get("GetClipboardAction_Param_As_Choice_Anything")), new Choice("text", Strings.Get("GetClipboardAction_Param_As_Choice_Text"))),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var value = await ctx.Services.Clipboard.GetAsync().ConfigureAwait(false);
        if (value.IsEmpty) return ContentValue.Empty;
        return await ctx.GetChoiceAsync("as", "anything") == "text" ? ContentValue.Text(value.AsText()) : value;
    }
}

public sealed class CopyToClipboardAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "clip.set", Title = Strings.Get("CopyToClipboardAction_Title"), Category = ActionCategories.Clipboard, Icon = "Clipboard24",
        Keywords = new[] { "copy", "clipboard", "password", "temporary" },
        Summary = Strings.Get("CopyToClipboardAction_Summary"),
        Description = Strings.Get("CopyToClipboardAction_Description"),
        OutputName = Strings.Get("CopyToClipboardAction_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("content", Strings.Get("CopyToClipboardAction_Param_Content_Label"), isInput: true, required: true),
            ParameterDescriptor.Duration("clearAfter", Strings.Get("CopyToClipboardAction_Param_ClearAfter_Label"), 0),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var content = await ctx.GetContentAsync("content");
        if (content.IsEmpty) throw new ActionException(Strings.Get("CopyToClipboardAction_Error_There_S_Nothing_To_Copy_Choose"));
        var seconds = (await ctx.GetDurationAsync("clearAfter", TimeSpan.Zero)).TotalSeconds;
        var clipboard = ctx.Services.Clipboard;
        await clipboard.SetAsync(content).ConfigureAwait(false);

        if (seconds > 0)
        {
            var sequence = Win32.GetClipboardSequenceNumber();
            // Deliberately not tied to the run's cancellation: the wipe must still happen after the shortcut ends.
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds)).ConfigureAwait(false);
                try
                {
                    if (ShouldClear(sequence, Win32.GetClipboardSequenceNumber())) await clipboard.ClearAsync().ConfigureAwait(false);
                }
                catch (Exception ex) { ctx.Log($"Couldn't clear the clipboard: {ex.Message}"); }
            });
        }
        return content;
    }

    /// <summary>Only wipe the clipboard if it still holds what we put there.</summary>
    internal static bool ShouldClear(uint sequenceWhenSet, uint sequenceNow) => sequenceWhenSet == sequenceNow;
}

public sealed class ClearClipboardAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "clip.clear", Title = Strings.Get("ClearClipboardAction_Title"), Category = ActionCategories.Clipboard, Icon = "ClipboardTaskListLtr24",
        Keywords = new[] { "wipe", "empty", "clipboard", "privacy" },
        Summary = Strings.Get("ClearClipboardAction_Summary"),
        Description = Strings.Get("ClearClipboardAction_Description"),
        HasOutput = false,
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await ctx.Services.Clipboard.ClearAsync().ConfigureAwait(false);
        return ContentValue.Empty;
    }
}
