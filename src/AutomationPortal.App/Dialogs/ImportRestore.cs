using System.Windows;
using System.Windows.Controls;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.Core;
using Microsoft.Win32;

namespace AutomationPortal.App.Dialogs;

/// <summary>
/// "Import…" and "Restore from a backup…". Both only add: every shortcut that comes in gets a new id and a free name
/// ("Name (2)"), so nothing in the library is replaced, and nothing in the file or the zip is changed.
/// </summary>
public static class ImportRestore
{
    public const string FileFilter = "Automation Portal shortcut (*.autoportal;*.flowmate)|*.autoportal;*.flowmate";

    /// <summary>Asks for a shortcut file and sends it through the usual review (what it does, then Add).</summary>
    public static async Task ImportFileAsync(AppHost host, Window? owner)
    {
        var dlg = new OpenFileDialog { Title = Strings.Get("ImportRestore_ImportAShortcut"), Filter = FileFilter };
        if (dlg.ShowDialog(owner) != true) return;
        await host.ImportFromFileAsync(dlg.FileName, fromLink: false);
    }

    /// <summary>Picks a backup zip, lists the shortcuts in it with tick boxes, and adds the ticked ones as new shortcuts.</summary>
    public static async Task RestoreAsync(AppHost host, Window? owner)
    {
        var picker = new OpenFileDialog { Title = Strings.Get("ImportRestore_ChooseABackup"), Filter = "Automation Portal backup (*.zip)|*.zip", InitialDirectory = ExistingFolder(LibraryBackup.DefaultFolder) };
        if (picker.ShowDialog(owner) != true) return;

        IReadOnlyList<Shortcut> found;
        try { found = LibraryBackup.ReadShortcuts(picker.FileName); }
        catch (LibraryException ex) { await Prompts.MessageAsync(owner, "Couldn't open the backup", ex.Message); return; }
        if (found.Count == 0)
        {
            await Prompts.MessageAsync(owner, Strings.Get("ImportRestore_NothingToRestore"), "That zip has no shortcuts in it. Choose a backup made with \"Back up library now\".");
            return;
        }

        var body = new StackPanel();
        body.Children.Add(Ui.Text("Ticked shortcuts are added next to the ones you have now. A name you already use gets a number, like \"Name (2)\". Nothing is replaced.", secondary: true, size: 13));
        var boxes = new List<(CheckBox Box, Shortcut Shortcut)>();
        var list = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var s in found.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var cb = new CheckBox { Content = s.Name, IsChecked = true, Margin = new Thickness(0, 2, 0, 2) };
            boxes.Add((cb, s));
            list.Children.Add(cb);
        }
        body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var dlg = new FlowDialog(Strings.Get("ImportRestore_RestoreFromABackup"), body, Strings.Get("ImportRestore_AddThem"), "Cancel", width: 480);
        if (!await dlg.ShowTopmostAsync(default, owner)) return;

        var added = 0;
        foreach (var (box, shortcut) in boxes)
        {
            if (box.IsChecked != true) continue;
            try
            {
                var result = host.Library.Import(shortcut);
                await host.FinishImportAsync(result, owner, warningsShown: false);
                added++;
            }
            catch (LibraryException ex) { await Prompts.MessageAsync(owner, "Couldn't add one", $"\"{shortcut.Name}\" wasn't added. {ex.Message}"); }
        }
        if (added > 0) host.Notifications.Show("Backup restored", added == 1 ? "1 shortcut was added to your library." : $"{added} shortcuts were added to your library.");
    }

    private static string? ExistingFolder(string folder) => Directory.Exists(folder) ? folder : null;
}
