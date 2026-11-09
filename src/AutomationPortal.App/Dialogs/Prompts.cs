using System.Windows;
using System.Windows.Controls;
using AutomationPortal.App.Infrastructure;

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

    public static Task MessageAsync(Window? owner, string title, string message, string okText = "OK")
    {
        var body = new StackPanel();
        body.Children.Add(Ui.Text(message));
        var dlg = new FlowDialog(title, body, okText, null, width: 440);
        return dlg.ShowTopmostAsync(default, owner);
    }
}
