using System.Windows;
using System.Windows.Controls;
using Flowmate.App.Dialogs;
using Flowmate.App.Infrastructure;
using Flowmate.App.Integration;
using Flowmate.Core;

namespace Flowmate.App.Services;

/// <summary>
/// Temporary stand-in for the real shortcut editor (built separately in Flowmate.Editor). It lets you create a
/// shortcut and change its name, description, icon and colour. INTEGRATION(E): replace with a launcher that calls
/// ShortcutEditor.Open(...) and IconPickerDialog.
/// </summary>
public sealed class FallbackEditorLauncher : IShortcutEditorLauncher
{
    private readonly IShortcutLibrary _library;
    private readonly Func<Window?> _owner;

    public FallbackEditorLauncher(IShortcutLibrary library, Func<Window?> owner)
    {
        _library = library;
        _owner = owner;
    }

    public void Open(Guid shortcutId)
    {
        if (_library.GetById(shortcutId) is not { } s) return;
        _ = EditAsync(s.Clone(), isNew: false);
    }

    public void OpenNew(Guid? folderId)
    {
        var name = "New Shortcut";
        for (var n = 2; _library.IsNameTaken(name); n++) name = $"New Shortcut {n}";
        _ = EditAsync(new Shortcut { Name = name, FolderId = folderId, SortOrder = NextSortOrder() }, isNew: true);
    }

    private int NextSortOrder() => _library.All.Count == 0 ? 0 : _library.All.Max(s => s.SortOrder) + 1;

    public async Task<IconChoice?> PickIconAsync(Window owner, string icon, string color)
    {
        var panel = new IconColorPanel(icon, color);
        var dlg = new FlowDialog("Choose an icon", panel, "Choose", "Cancel", width: 460);
        return await dlg.ShowTopmostAsync(default, owner) ? new IconChoice(panel.Icon, panel.Color) : null;
    }

    private async Task EditAsync(Shortcut shortcut, bool isNew)
    {
        var body = new StackPanel();
        var note = Ui.Text("The full editor, where you add and arrange actions, arrives with the next update. For now you can set the name, description, icon and colour.", secondary: true, size: 12);
        note.Margin = new Thickness(0, 0, 0, 14);
        body.Children.Add(note);

        var name = new Wpf.Ui.Controls.TextBox { Text = shortcut.Name };
        body.Children.Add(Ui.Labeled("Name", name));
        var desc = new Wpf.Ui.Controls.TextBox { Text = shortcut.Description ?? "", PlaceholderText = "What does it do? (optional)" };
        body.Children.Add(Ui.Labeled("Description", desc));
        var panel = new IconColorPanel(shortcut.Icon, shortcut.Color);
        body.Children.Add(panel);
        var error = Ui.Text("", size: 12);
        error.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        error.Visibility = Visibility.Collapsed;
        body.Children.Add(error);

        var dlg = new FlowDialog(isNew ? "New shortcut" : "Edit shortcut", body, isNew ? "Create" : "Save", "Cancel", width: 480);
        dlg.Validate = () =>
        {
            var text = name.Text.Trim();
            string? msg = null;
            if (text.Length == 0) msg = "Give the shortcut a name first.";
            else if (_library.IsNameTaken(text, shortcut.Id)) msg = $"A shortcut named \"{text}\" already exists. Choose a different name.";
            error.Text = msg ?? "";
            error.Visibility = msg is null ? Visibility.Collapsed : Visibility.Visible;
            return msg is null;
        };
        dlg.FocusWhenShown(name);
        var owner = _owner();
        if (!await dlg.ShowTopmostAsync(default, owner)) return;

        shortcut.Name = name.Text.Trim();
        shortcut.Description = string.IsNullOrWhiteSpace(desc.Text) ? null : desc.Text.Trim();
        shortcut.Icon = panel.Icon;
        shortcut.Color = panel.Color;
        try { _library.Save(shortcut); }
        catch (LibraryException ex) { await Prompts.MessageAsync(owner, "Couldn't save", ex.Message); }
    }
}
