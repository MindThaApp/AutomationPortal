using System.Windows;
using System.Windows.Controls;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.Core;

namespace AutomationPortal.App.Dialogs;

/// <summary>Small reusable dialogs: ask for one line of text, confirm, show a message.</summary>
public static class Prompts
{
    /// <summary>How long Run stays disabled after a request or link sheet appears.</summary>
    public static readonly TimeSpan RunButtonDelay = TimeSpan.FromSeconds(1);

    public static async Task<string?> AskTextAsync(Window? owner, string title, string label, string initial = "", string okText = "OK",
        Func<string, string?>? validate = null, string? placeholder = null)
    {
        var body = new StackPanel();
        var box = new Wpf.Ui.Controls.TextBox { Text = initial, PlaceholderText = placeholder ?? "" };
        body.Children.Add(Ui.Labeled(label, box, bottom: 4));
        var error = Ui.Text("", size: 12);
        error.SetResourceReference(TextBlock.ForegroundProperty, "DsCriticalBrush");
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
    /// Another app wants to run a shortcut. The headline is the question itself, then the file-change badge, the items and text it
    /// would get (full paths) and what it would do. Buttons: Run, and Don't run with the seconds left in its label (no answer means
    /// no). Don't run is the default and has the focus, so Enter and Esc both press it: the sheet can appear while the user is typing.
    /// The tick box "Don't ask again" stands for Always allow and is never shown for an AI. Shown topmost, in front. Cancelling <paramref name="ct"/> (time is up, or the app that asked left) closes it and throws
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    public static async Task<CallerDecision> AskCallerAsync(CallerPrompt prompt, TimeSpan timeout, CancellationToken ct, Action? manage = null)
    {
        var body = new StackPanel();
        body.Children.Add(Ui.Text(prompt.Question, size: 16, weight: FontWeights.SemiBold));

        var badge = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            Child = Ui.Text(ShortcutEffects.Badge(prompt.Effect), size: 12) };
        badge.SetResourceReference(Border.BackgroundProperty, prompt.Effect == ShortcutEffect.ChangesFiles ? "DsCautionBackgroundBrush" : "DsHoverBrush");
        body.Children.Add(badge);

        void Line(string text, double top = 6)
        {
            var line = Ui.Text(text);
            line.Margin = new Thickness(0, top, 0, 0);
            body.Children.Add(line);
        }
        void Box(string text)
        {
            var block = Ui.Text(text, secondary: true, size: 12);
            block.TextWrapping = TextWrapping.Wrap;
            var scroll = new ScrollViewer { Content = block, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 4, 0, 0) };
            body.Children.Add(scroll);
        }
        if (prompt.AiLine is { } ai) Line(ai, 8);
        if (prompt.ChangesFilesLine is { } changes) Line(changes);
        if (prompt.Files.Count > 0)
        {
            Line(prompt.Files.Count == 1 ? "It would get this item:" : $"It would get these {prompt.Files.Count} items:", 10);
            Box(string.Join("\n", prompt.Files));
        }
        if (prompt.InputText is { } input)
        {
            Line("It would get this text:", 10);
            Box(input.Length > 2000 ? input[..2000] + "…" : input);
        }
        if (prompt.PreviewText is { } preview)
        {
            Line("What it would do:", 10);
            Box(preview);
        }
        var dontAsk = new CheckBox { Content = prompt.DontAskAgainText, Margin = new Thickness(0, 14, 0, 0) };
        if (prompt.CanRemember && !prompt.IsAi) body.Children.Add(dontAsk);
        if (manage is not null)
        {
            var link = new Wpf.Ui.Controls.HyperlinkButton { Content = Strings.Get("Prompts_ManageInSettings"), Padding = new Thickness(0, 4, 0, 4), Margin = new Thickness(0, 6, 0, 0) };
            link.Click += (_, _) => manage();
            body.Children.Add(link);
        }

        var seconds = (int)Math.Round(timeout.TotalSeconds);
        var dlg = new FlowDialog(Strings.Get("Prompts_RunThisShortcut"), body, "Run", CallerPrompt.DontRunLabel(seconds), width: 520);
        dlg.MakeSecondaryDefault();
        dlg.ProtectPrimary(RunButtonDelay);
        var started = DateTime.UtcNow;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => dlg.SetSecondaryText(CallerPrompt.DontRunLabel(seconds - (int)(DateTime.UtcNow - started).TotalSeconds));
        dlg.Closed += (_, _) => timer.Stop();
        timer.Start();
        var ok = await dlg.ShowTopmostAsync(ct);
        return !ok ? CallerDecision.DontRun : dontAsk.IsChecked == true && prompt.CanRemember && !prompt.IsAi ? CallerDecision.AlwaysAllow : CallerDecision.AllowOnce;
    }

    /// <summary>A link wants to run a shortcut: Run or Cancel, with "Don't ask again for this shortcut" when that is allowed.</summary>
    public static async Task<(bool Run, bool Remember)> AskLinkAsync(string message, bool offerRemember)
    {
        var body = new StackPanel();
        body.Children.Add(Ui.Text(message));
        var remember = new CheckBox { Content = Strings.Get("Prompts_DontAskAgainFor"), Margin = new Thickness(0, 14, 0, 0) };
        if (offerRemember) body.Children.Add(remember);
        var dlg = new FlowDialog(Strings.Get("Prompts_RunThisShortcut"), body, "Run", "Don't run", width: 480);
        dlg.MakeSecondaryDefault();
        dlg.ProtectPrimary(RunButtonDelay);
        var ok = await dlg.ShowTopmostAsync();
        return (ok, ok && offerRemember && remember.IsChecked == true);
    }

    public static Task MessageAsync(Window? owner, string title, string message, string okText = "OK")
    {
        var body = new StackPanel();
        body.Children.Add(Ui.Text(message));
        var dlg = new FlowDialog(title, body, okText, null, width: 440);
        return dlg.ShowTopmostAsync(default, owner);
    }
}
