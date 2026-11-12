using System.Windows;
using System.Windows.Controls;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.Core;

namespace AutomationPortal.App.Dialogs;

/// <summary>Small reusable dialogs: ask for one line of text, confirm, show a message.</summary>
public static class Prompts
{
    public static async Task<string?> AskTextAsync(Window? owner, string title, string label, string initial = "", string okText = "OK",
        Func<string, string?>? validate = null, string? placeholder = null)
    {
        var body = new StackPanel();
        var box = new Wpf.Ui.Controls.TextBox { Text = initial, PlaceholderText = placeholder ?? "" };
        body.Children.Add(Ui.Labeled(label, box, bottom: 4));
        var error = Ui.Text("", size: 12);
        error.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        error.Visibility = Visibility.Collapsed;
        body.Children.Add(error);

        var dlg = new FlowDialog(title, body, okText, "Cancel", width: 440);
        dlg.Validate = () =>
        {
            var text = box.Text.Trim();
            var msg = text.Length == 0 ? "Type a name first." : validate?.Invoke(text);
            error.Text = msg ?? "";
            error.Visibility = msg is null ? Visibility.Collapsed : Visibility.Visible;
            return msg is null;
        };
        dlg.FocusWhenShown(box);
        dlg.ContentRendered += (_, _) => box.SelectAll();
        return await dlg.ShowTopmostAsync(default, owner) ? box.Text.Trim() : null;
    }

    public static Task<bool> ConfirmAsync(Window? owner, string title, string message, string okText = "OK", bool danger = false, string cancelText = "Cancel")
    {
        var body = new StackPanel();
        body.Children.Add(Ui.Text(message));
        var dlg = new FlowDialog(title, body, okText, cancelText, width: 440, danger: danger);
        return dlg.ShowTopmostAsync(default, owner);
    }

    /// <summary>
    /// Another app wants to run a shortcut: Allow once, Always allow or Don't run. Shown like the link prompts (topmost, in front), but
    /// Enter doesn't allow it, since it can appear while the user is typing. Cancelling <paramref name="ct"/> (time is up, or the
    /// app that asked left) closes it and throws <see cref="OperationCanceledException"/>.
    /// </summary>
    public static async Task<CallerDecision> AskCallerAsync(CallerPrompt prompt, TimeSpan timeout, CancellationToken ct)
    {
        var body = new StackPanel();
        body.Children.Add(Ui.Text(prompt.Headline + "."));
        if (prompt.Files.Count > 0)
        {
            var shown = string.Join("\n", prompt.Files.Take(8));
            if (prompt.Files.Count > 8) shown += $"\n…and {prompt.Files.Count - 8} more";
            var list = Ui.Text(shown, secondary: true, size: 12);
            list.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(list);
        }
        var note = Ui.Text($"Automation Portal can't check which app is really asking. Only allow it if you just did something in {prompt.CallerName} that should run this shortcut. " +
                           $"If you don't answer within {(int)Math.Round(timeout.TotalSeconds)} seconds, it won't run.", secondary: true, size: 12);
        note.Margin = new Thickness(0, 10, 0, 0);
        body.Children.Add(note);

        var always = false;
        var dlg = new FlowDialog("Run this shortcut?", body, "Allow once", "Don't run", width: 480);
        dlg.SetPrimaryDefault(false);
        dlg.AddLeftButton("Always allow", () => { always = true; dlg.Accept(); });
        var ok = await dlg.ShowTopmostAsync(ct);
        return !ok ? CallerDecision.DontRun : always ? CallerDecision.AlwaysAllow : CallerDecision.AllowOnce;
    }

    public static Task MessageAsync(Window? owner, string title, string message, string okText = "OK")
    {
        var body = new StackPanel();
        body.Children.Add(Ui.Text(message));
        var dlg = new FlowDialog(title, body, okText, null, width: 440);
        return dlg.ShowTopmostAsync(default, owner);
    }
}
