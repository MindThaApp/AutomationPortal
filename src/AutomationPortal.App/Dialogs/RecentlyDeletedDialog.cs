using System.Windows;
using System.Windows.Controls;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.Core;

namespace AutomationPortal.App.Dialogs;

/// <summary>Recently deleted: restore shortcuts, delete them for good, or empty the list.</summary>
public static class RecentlyDeletedDialog
{
    public static Task ShowAsync(Window? owner, JsonShortcutLibrary library)
    {
        var trash = library.RecentlyDeleted;
        var days = RecentlyDeleted.KeepFor.Days;
        var body = new StackPanel();
        body.Children.Add(Ui.Text($"Shortcuts you delete are kept on this PC for {days} days. After that they're removed for good. If your library is synced, other PCs don't have these copies."));
        var list = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        body.Children.Add(list);

        var dlg = new FlowDialog("Recently deleted", body, "Close", null, width: 580);
        Wpf.Ui.Controls.Button? emptyButton = null;

        void Refresh()
        {
            list.Children.Clear();
            var items = trash.List();
            if (emptyButton is not null) emptyButton.IsEnabled = items.Count > 0;
            if (items.Count == 0) { list.Children.Add(Ui.Text("There's nothing here.", secondary: true)); return; }
            foreach (var item in items) list.Children.Add(Row(item));
        }

        FrameworkElement Row(DeletedShortcut item)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Ui.Text(item.Name, weight: FontWeights.SemiBold));
            var detail = item.Damaged
                ? $"Its details are damaged ({item.Key}), so it's never removed automatically."
                  + (item.CanRestore ? "" : " Its saved copy can't be read as a shortcut, so it can't be restored.")
                : $"Deleted {item.DeletedAt.LocalDateTime:g}. Removed for good after {item.RemovedAt.LocalDateTime:d}.";
            if (item.AutomationCount > 0)
                detail += $" {(item.AutomationCount == 1 ? "An automation used" : item.AutomationCount + " automations used")} it.";
            text.Children.Add(Ui.Text(detail, secondary: true, size: 12));
            grid.Children.Add(text);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var restore = new Wpf.Ui.Controls.Button { Content = "Restore", MinWidth = 80, IsEnabled = item.CanRestore };
            restore.Click += async (_, _) => await RestoreAsync(item);
            var remove = new Wpf.Ui.Controls.Button { Content = "Delete permanently", Margin = new Thickness(8, 0, 0, 0) };
            remove.Click += async (_, _) => await DeleteAsync(item);
            buttons.Children.Add(restore);
            buttons.Children.Add(remove);
            Grid.SetColumn(buttons, 1);
            grid.Children.Add(buttons);
            return grid;
        }

        async Task RestoreAsync(DeletedShortcut item)
        {
            RestoreResult r;
            try { r = library.Restore(item); }
            catch (Exception ex) when (ex is LibraryException or IOException or UnauthorizedAccessException)
            {
                await Prompts.MessageAsync(dlg, "Couldn't restore the shortcut", ex.Message);
                Refresh();
                return;
            }
            Refresh();
            var notes = new List<string>();
            if (r.AsCopy)
                notes.Add($"A shortcut with the same ID is already in your library, so \"{r.Shortcut.Name}\" came back as a copy. Its keyboard shortcut, Quick Run name and Send To entry were left off. Automations that used the deleted shortcut stay linked to the one already in your library.");
            else if (library.Automations.Any(a => a.ShortcutId == r.Shortcut.Id))
                notes.Add("Automations that use this shortcut work again.");
            if (r.ClearedHotkey is not null)
                notes.Add($"Another shortcut now uses the keyboard shortcut {r.ClearedHotkey}, so it was left off the restored one.");
            if (r.ClearedAlias is not null)
                notes.Add($"Another shortcut now uses the Quick Run name \"{r.ClearedAlias}\", so it was left off the restored one.");
            if (r.RenamedFrom is not null)
                notes.Add($"A shortcut named \"{r.RenamedFrom}\" already exists, so the restored one is called \"{r.Shortcut.Name}\".");
            if (!r.RemovedFromDeleted)
                notes.Add("Its copy in Recently deleted couldn't be removed, so it's still listed there.");
            if (notes.Count > 0) await Prompts.MessageAsync(dlg, $"\"{r.Shortcut.Name}\" is back", string.Join("\n\n", notes));
        }

        async Task DeleteAsync(DeletedShortcut item)
        {
            if (!await Prompts.ConfirmAsync(dlg, "Delete permanently",
                    $"Delete \"{item.Name}\" permanently? 1 shortcut will be removed for good. This can't be undone.", "Delete permanently", danger: true)) return;
            try { trash.DeletePermanently(item); }
            catch (LibraryException ex) { await Prompts.MessageAsync(dlg, "Couldn't delete it", ex.Message); }
            Refresh();
        }

        // Bound to a button as an Action, so it must be async void: nothing may escape it or the app goes down.
        async void EmptyAll()
        {
            try
            {
                var items = trash.List();
                var count = items.Count;
                if (count == 0) return;
                var what = count == 1 ? "1 shortcut" : $"{count} shortcuts";
                if (!await Prompts.ConfirmAsync(dlg, "Empty Recently deleted",
                        $"Delete {what} permanently? {(count == 1 ? "It" : "They")} will be removed for good. This can't be undone.", "Empty", danger: true)) return;
                var removed = trash.Empty(items);
                Refresh();
                if (removed < count)
                    await Prompts.MessageAsync(dlg, "Some shortcuts are still here", $"{count - removed} couldn't be removed. They may be open in another program, or changed since you confirmed. Try again in a moment.");
            }
            catch (Exception ex)
            {
                try
                {
                    Refresh();
                    await Prompts.MessageAsync(dlg, "Couldn't empty Recently deleted", ex.Message);
                }
                catch (Exception) { }
            }
        }

        emptyButton = dlg.AddLeftButton("Empty", EmptyAll);
        Refresh();
        return dlg.ShowTopmostAsync(default, owner);
    }
}
