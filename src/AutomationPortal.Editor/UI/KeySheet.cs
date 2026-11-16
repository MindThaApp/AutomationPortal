using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace AutomationPortal.Editor.UI;

/// <summary>The "Keyboard shortcuts" cheat sheet, opened with Ctrl+/ or F1 in the main window and in the editor.</summary>
public static class KeySheet
{
    public sealed record Group(string Title, IReadOnlyList<(string Keys, string What)> Rows);

    /// <summary>Every key the two windows answer to, in the order the sheet shows them. Keep in step with the key handlers.</summary>
    public static IReadOnlyList<Group> Groups { get; } =
    [
        new("Anywhere in the app",
        [
            ("Ctrl+K", "Command palette: find a shortcut, a setting or a command"),
            ("Ctrl+/ or F1", "Show this sheet"),
            ("Ctrl+F", "Search shortcuts and the Gallery"),
            ("Ctrl+N", "New shortcut"),
            ("Ctrl+,", "Settings"),
            ("Ctrl+1 to Ctrl+6", "All Shortcuts, Pinned, Automations, Snippets, Gallery, Run History"),
        ]),
        new("In the library",
        [
            ("Enter", "Run the selected shortcut"),
            ("Ctrl+E", "Edit the selected shortcut"),
            ("Ctrl+D", "Duplicate it"),
            ("F2", "Rename it"),
            ("Delete", "Delete it (Undo brings it back)"),
            ("Ctrl+Z", "Undo the last delete"),
        ]),
        new("In the editor",
        [
            ("F5", "Run the shortcut"),
            ("Shift+F5", "Stop it"),
            ("Ctrl+F", "Search the actions"),
            ("Ctrl+C, Ctrl+X, Ctrl+V", "Copy, cut or paste steps"),
            ("Ctrl+D", "Duplicate the selected steps"),
            ("Ctrl+A", "Select all steps"),
            ("Ctrl+Z, Ctrl+Y", "Undo, redo"),
            ("Delete", "Delete the selected steps"),
            ("Alt+Up, Alt+Down", "Move the selected steps"),
            ("Shift+F10", "Open the menu of the selected step"),
            ("Esc", "Clear the selection"),
        ]),
        new("In Quick Run",
        [
            ("Enter", "Run"),
            ("Tab", "Give the shortcut some text first"),
            ("Esc", "Close"),
        ]),
    ];

    /// <summary>True for Ctrl+/ and F1, the keys that open the sheet.</summary>
    public static bool IsOpenKey(Key key, ModifierKeys modifiers) =>
        (key == Key.F1 && modifiers == ModifierKeys.None) || (key == Key.OemQuestion && modifiers == ModifierKeys.Control) || (key == Key.Divide && modifiers == ModifierKeys.Control);

    public static void Show(Window? owner)
    {
        var win = new FluentWindow
        {
            Title = "Keyboard shortcuts", Width = 560, Height = 640, MinWidth = 380, ShowInTaskbar = false, ResizeMode = ResizeMode.CanResize,
            WindowBackdropType = WindowBackdropType.Mica, ExtendsContentIntoTitleBar = true, UseLayoutRounding = true,
        };
        win.SetResourceReference(Control.BackgroundProperty, "ApplicationBackgroundBrush");
        win.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");
        win.FontFamily = Application.Current?.TryFindResource("FlowFont") as System.Windows.Media.FontFamily ?? Ui.Font;
        if (owner is { IsVisible: true }) { win.Owner = owner; win.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        else win.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(new TitleBar { Title = "Keyboard shortcuts", ShowMinimize = false, ShowMaximize = false, CanMaximize = false });

        var stack = new StackPanel { Margin = new Thickness(24, 8, 24, 20) };
        foreach (var g in Groups)
        {
            var h = Ui.Text(g.Title, Ui.Primary, 16, FontWeights.SemiBold);
            h.Margin = new Thickness(0, 14, 0, 6);
            stack.Children.Add(h);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var r = 0;
            foreach (var (keys, what) in g.Rows)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var k = Ui.Text(keys, Ui.Primary, 13, FontWeights.SemiBold, wrap: true);
                k.Margin = new Thickness(0, 3, 12, 3);
                var w = Ui.Text(what, Ui.Secondary, 13, wrap: true);
                w.Margin = new Thickness(0, 3, 0, 3);
                Grid.SetRow(k, r); Grid.SetRow(w, r); Grid.SetColumn(w, 1);
                grid.Children.Add(k); grid.Children.Add(w);
                r++;
            }
            stack.Children.Add(grid);
        }
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        win.Content = root;
        win.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape || KeySheet.IsOpenKey(e.Key, Keyboard.Modifiers)) { e.Handled = true; win.Close(); } };
        win.Show();
    }
}
