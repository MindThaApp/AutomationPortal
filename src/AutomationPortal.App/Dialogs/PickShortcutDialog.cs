using System.Windows;
using System.Windows.Controls;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.Core;

namespace AutomationPortal.App.Dialogs;

/// <summary>"Choose a shortcut to run on these items": lists the shortcuts and returns the one the user picks, or null.</summary>
public static class PickShortcutDialog
{
    private const int NamesShown = 5;

    /// <summary>The item count and the first few names, for the top of the picker.</summary>
    public static string Describe(IReadOnlyList<string> items)
    {
        var names = items.Take(NamesShown).Select(p => Path.GetFileName(p.TrimEnd('\\')) is { Length: > 0 } n ? n : p).ToList();
        var text = $"{items.Count} item{(items.Count == 1 ? "" : "s")}: {string.Join(", ", names)}";
        return items.Count > NamesShown ? text + $" and {items.Count - NamesShown} more" : text;
    }

    public static async Task<Shortcut?> ShowAsync(Window? owner, IReadOnlyList<string> items, IEnumerable<Shortcut> shortcuts)
    {
        var list = shortcuts.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var body = new StackPanel();
        body.Children.Add(Ui.Text("A link or another app wants to run one of your shortcuts on these files and folders. Nothing runs until you choose one."));
        body.Children.Add(Ui.Text(Describe(items), secondary: true, size: 12));
        var box = new ListBox { MaxHeight = 280, Margin = new Thickness(0, 12, 0, 0), DisplayMemberPath = nameof(Shortcut.Name), ItemsSource = list };
        if (list.Count == 0) body.Children.Add(Ui.Text("You don't have any shortcuts yet.", secondary: true));
        else { body.Children.Add(box); box.SelectedIndex = 0; }

        var dlg = new FlowDialog("Run a shortcut on these items?", body, "Run", "Cancel", width: 460);
        dlg.Validate = () => box.SelectedItem is not null;
        dlg.FocusWhenShown(box);
        box.MouseDoubleClick += (_, _) => { if (box.SelectedItem is not null) dlg.Accept(); };
        return await dlg.ShowTopmostAsync(default, owner) ? box.SelectedItem as Shortcut : null;
    }
}
