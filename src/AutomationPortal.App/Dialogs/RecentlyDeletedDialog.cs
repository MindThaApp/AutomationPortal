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
        body.Children.Add(Ui.Text(Strings.Format("RecentlyDeletedDialog_ShortcutsYouDeleteAre", days)));
        var list = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        body.Children.Add(list);

        var dlg = new FlowDialog(Strings.Get("RecentlyDeletedDialog_RecentlyDeleted"), body, "Close", null, width: 580);
        Wpf.Ui.Controls.Button? emptyButton = null;

        void Refresh()
        {
            list.Children.Clear();
            var items = trash.List();
            if (emptyButton is not null) emptyButton.IsEnabled = items.Count > 0;
            if (items.Count == 0) { list.Children.Add(Ui.Text(Strings.Get("RecentlyDeletedDialog_ThereSNothingHere"), secondary: true)); return; }
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
                ? Strings.Format("RecentlyDeletedDialog_ItsDetailsAreDamaged", item.Key)
                  + (item.CanRestore ? "" : Strings.Get("RecentlyDeletedDialog_ItsSavedCopyCan"))
                : Strings.Format("RecentlyDeletedDialog_Deleted0GRemoved", item.DeletedAt.LocalDateTime, item.RemovedAt.LocalDateTime);
            if (item.AutomationCount > 0)
                detail += $" {(item.AutomationCount == 1 ? "An automation used" : item.AutomationCount + " automations used")} it.";
            text.Children.Add(Ui.Text(detail, secondary: true, size: 12));
            grid.Children.Add(text);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var restore = new Wpf.Ui.Controls.Button { Content = Strings.Get("RecentlyDeletedDialog_Restore"), MinWidth = 80, IsEnabled = item.CanRestore };
            restore.Click += async (_, _) => await RestoreAsync(item);
            var remove = new Wpf.Ui.Controls.Button { Content = Strings.Get("RecentlyDeletedDialog_DeletePermanently"), Margin = new Thickness(8, 0, 0, 0) };
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
                await Prompts.MessageAsync(dlg, Strings.Get("RecentlyDeletedDialog_CouldnTRestoreThe"), ex.Message);
                Refresh();
                return;
            }
            Refresh();
            var notes = new List<string>();
            if (r.AsCopy)
                notes.Add(Strings.Format("RecentlyDeletedDialog_AShortcutWithThe", r.Shortcut.Name));
            else if (library.Automations.Any(a => a.ShortcutId == r.Shortcut.Id))
                notes.Add(Strings.Get("RecentlyDeletedDialog_AutomationsThatUseThis"));
            if (r.ClearedHotkey is not null)
                notes.Add(Strings.Format("RecentlyDeletedDialog_AnotherShortcutNowUses", r.ClearedHotkey));
            if (r.ClearedAlias is not null)
                notes.Add(Strings.Format("RecentlyDeletedDialog_AnotherShortcutNowUses2", r.ClearedAlias));
            if (r.RenamedFrom is not null)
                notes.Add(Strings.Format("RecentlyDeletedDialog_AShortcutNamed0", r.RenamedFrom, r.Shortcut.Name));
            if (!r.RemovedFromDeleted)
                notes.Add(Strings.Get("RecentlyDeletedDialog_ItsCopyInRecently"));
            if (notes.Count > 0) await Prompts.MessageAsync(dlg, Strings.Format("RecentlyDeletedDialog_0IsBack", r.Shortcut.Name), string.Join("\n\n", notes));
        }

        async Task DeleteAsync(DeletedShortcut item)
        {
            if (!await Prompts.ConfirmAsync(dlg, Strings.Get("RecentlyDeletedDialog_DeletePermanently"),
                    Strings.Format("RecentlyDeletedDialog_Delete0Permanently1", item.Name), Strings.Get("RecentlyDeletedDialog_DeletePermanently"), danger: true)) return;
            try { trash.DeletePermanently(item); }
            catch (LibraryException ex) { await Prompts.MessageAsync(dlg, Strings.Get("RecentlyDeletedDialog_CouldnTDeleteIt"), ex.Message); }
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
                if (!await Prompts.ConfirmAsync(dlg, Strings.Get("RecentlyDeletedDialog_EmptyRecentlyDeleted"),
                        Strings.Format("RecentlyDeletedDialog_Delete0Permanently12", what, (count == 1 ? "It" : "They")), Strings.Get("RecentlyDeletedDialog_Empty"), danger: true)) return;
                var removed = trash.Empty(items);
                Refresh();
                if (removed < count)
                    await Prompts.MessageAsync(dlg, Strings.Get("RecentlyDeletedDialog_SomeShortcutsAreStill"), Strings.Format("RecentlyDeletedDialog_0CouldnTBe", count - removed));
            }
            catch (Exception ex)
            {
                try
                {
                    Refresh();
                    await Prompts.MessageAsync(dlg, Strings.Get("RecentlyDeletedDialog_CouldnTEmptyRecently"), ex.Message);
                }
                catch (Exception) { }
            }
        }

        emptyButton = dlg.AddLeftButton("Empty", EmptyAll);
        Refresh();
        return dlg.ShowTopmostAsync(default, owner);
    }
}
