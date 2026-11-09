using Flowmate.Actions.Windows.Native;

namespace Flowmate.Actions.Windows.Clipboard;

public sealed class GetClipboardAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        SensitiveOutput = true, Id = "clip.get", Title = "Get Clipboard", Category = ActionCategories.Clipboard, Icon = "ClipboardPaste24",
        Keywords = new[] { "paste", "copied", "clipboard" },
        Summary = "Get what is on the clipboard",
        Description = "Gets whatever you last copied (text, a picture or files). If you'd rather have it as plain text, choose that.",
        OutputName = "Clipboard",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("as", "Get it as", "anything", new Choice("anything", "whatever it is"), new Choice("text", "plain text")),
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
        Id = "clip.set", Title = "Copy to Clipboard", Category = ActionCategories.Clipboard, Icon = "Clipboard24",
        Keywords = new[] { "copy", "clipboard", "password", "temporary" },
        Summary = "Copy {content} to the clipboard",
        Description = "Puts something on the clipboard so you can paste it. You can have it wiped after a few seconds (handy for passwords): it is only cleared if you haven't copied something else since.",
        OutputName = "Copied Content",
        Parameters = new[]
        {
            ParameterDescriptor.Content("content", "Content", isInput: true, required: true),
            ParameterDescriptor.Duration("clearAfter", "Clear after (0 = never)", 0),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var content = await ctx.GetContentAsync("content");
        if (content.IsEmpty) throw new ActionException("There's nothing to copy. Choose some content first.");
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
        Id = "clip.clear", Title = "Clear Clipboard", Category = ActionCategories.Clipboard, Icon = "ClipboardTaskListLtr24",
        Keywords = new[] { "wipe", "empty", "clipboard", "privacy" },
        Summary = "Clear the clipboard",
        Description = "Empties the clipboard so nothing can be pasted.",
        HasOutput = false,
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        await ctx.Services.Clipboard.ClearAsync().ConfigureAwait(false);
        return ContentValue.Empty;
    }
}
