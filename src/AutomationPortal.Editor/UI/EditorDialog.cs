using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace AutomationPortal.Editor.UI;

/// <summary>Which button closed an <see cref="EditorDialog"/>. Closing the window any other way counts as <see cref="Cancel"/>.</summary>
public enum DialogChoice { Primary, Secondary, Cancel }

/// <summary>
/// The editor's questions, in the same style as the app's own dialogs: a title bar, plain text, and buttons that say what they do.
/// Modal, because some questions are asked while the window is closing.
/// </summary>
public sealed class EditorDialog : FluentWindow
{
    private DialogChoice _choice = DialogChoice.Cancel;

    private EditorDialog(Window? owner, string title, string message, string primary, string? secondary, string? cancel, bool danger, DialogChoice enter)
    {
        Title = title;
        Width = 460;
        MinWidth = 320;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowBackdropType = WindowBackdropType.Mica;
        ExtendsContentIntoTitleBar = true;
        UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
        SetResourceReference(ForegroundProperty, "DsTextPrimaryBrush");
        FontFamily = Application.Current?.TryFindResource("FlowFont") as System.Windows.Media.FontFamily ?? Ui.Font;
        if (owner is { IsVisible: true })
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TitleBar { Title = title, ShowMinimize = false, ShowMaximize = false, CanMaximize = false });

        var body = Ui.Text(message, Ui.Primary, 14, wrap: true);
        body.Margin = new Thickness(24, 8, 24, 16);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body, MaxHeight = Math.Max(200, SystemParameters.WorkArea.Height * 0.6) };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 0, 24, 20) };
        var first = new Wpf.Ui.Controls.Button { Content = primary, MinWidth = 96, Appearance = danger ? ControlAppearance.Danger : ControlAppearance.Primary, IsDefault = enter == DialogChoice.Primary };
        first.Click += (_, _) => Finish(DialogChoice.Primary);
        buttons.Children.Add(first);
        if (secondary is not null)
        {
            var second = new Wpf.Ui.Controls.Button { Content = secondary, MinWidth = 96, Margin = new Thickness(8, 0, 0, 0), IsDefault = enter == DialogChoice.Secondary };
            second.Click += (_, _) => Finish(DialogChoice.Secondary);
            buttons.Children.Add(second);
        }
        if (cancel is not null)
        {
            var third = new Wpf.Ui.Controls.Button { Content = cancel, MinWidth = 96, Margin = new Thickness(8, 0, 0, 0), IsCancel = true, IsDefault = enter == DialogChoice.Cancel };
            third.Click += (_, _) => Finish(DialogChoice.Cancel);
            buttons.Children.Add(third);
        }
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Finish(DialogChoice.Cancel); e.Handled = true; } };
    }

    private void Finish(DialogChoice choice)
    {
        _choice = choice;
        Close();
    }

    /// <summary>
    /// Asks a question. Enter presses the button <paramref name="enter"/> names, which should be the safe answer; Escape or closing
    /// the window is <see cref="DialogChoice.Cancel"/>.
    /// </summary>
    public static DialogChoice Ask(Window? owner, string title, string message, string primary, string? secondary, string? cancel = null,
        bool danger = false, DialogChoice enter = DialogChoice.Secondary)
    {
        var dlg = new EditorDialog(owner, title, message, primary, secondary, cancel, danger, enter);
        dlg.ShowDialog();
        return dlg._choice;
    }

    /// <summary>Shows a message with one button.</summary>
    public static void Tell(Window? owner, string title, string message, string ok = "OK")
    {
        var dlg = new EditorDialog(owner, title, message, ok, null, null, false, DialogChoice.Primary);
        dlg.ShowDialog();
    }
}
