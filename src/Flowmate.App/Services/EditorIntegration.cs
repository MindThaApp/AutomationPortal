using System.Windows;
using Flowmate.App.Dialogs;
using Flowmate.App.Integration;
using Flowmate.App.Logic;
using Flowmate.Core;
using Flowmate.Editor;

namespace Flowmate.App.Services;

/// <summary>Opens the real shortcut editor (Flowmate.Editor) and its icon picker.</summary>
public sealed class EditorLauncher : IShortcutEditorLauncher
{
    private readonly AppHost _host;
    private EditorContext? _context;

    public EditorLauncher(AppHost host) { _host = host; }

    private EditorContext Context => _context ??= new EditorContext(_host.Services, _host.Actions, new EditorHost(_host));

    private static Window? ActiveWindow() =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible)
        ?? Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible);

    public void Open(Guid shortcutId) => ShortcutEditor.Open(shortcutId, Context, _host.MainWindowOwner());

    public void OpenNew(Guid? folderId)
    {
        var library = _host.Library;
        var name = "New Shortcut";
        for (var n = 2; library.IsNameTaken(name); n++) name = $"New Shortcut {n}";
        var order = library.All.Count == 0 ? 0 : library.All.Max(s => s.SortOrder) + 1;
        ShortcutEditor.Open(new Shortcut { Name = name, FolderId = folderId, SortOrder = order }, Context, _host.MainWindowOwner());
    }

    public Task<IconChoice?> PickIconAsync(Window owner, string icon, string color)
    {
        var pick = IconPickerDialog.Show(owner, icon, color);
        return Task.FromResult(pick is { } p ? new IconChoice(p.Icon, p.Color) : null);
    }
}

/// <summary>What the editor needs from the app around it.</summary>
public sealed class EditorHost : IEditorHost
{
    private readonly AppHost _host;

    public EditorHost(AppHost host) { _host = host; }

    private static Window? ActiveWindow() =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible)
        ?? Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible);

    public IReadOnlyList<AutomationSummary> GetAutomationsFor(Guid shortcutId) =>
        _host.Library.Automations.Where(a => a.ShortcutId == shortcutId).OrderBy(a => a.CreatedAt)
            .Select(a =>
            {
                var model = AutomationSentence.Build(a, _host.Triggers, _host.Conditions, _host.Library);
                var sentence = string.Concat(model.Parts.TakeWhile(p => p.Kind != SentencePartKind.Plain || !p.Text.Contains('→')).Select(p => p.Text)).Trim();
                if (model.ConditionsText.Length > 0) sentence += ". " + model.ConditionsText;
                return new AutomationSummary(a.Id, sentence, a.Enabled);
            }).ToList();

    public void AddTrigger(Guid shortcutId) => _ = AutomationWizardWindow.ShowAsync(_host, ActiveWindow(), shortcutId);

    public void EditAutomation(Guid id)
    {
        if (_host.Library.Automations.FirstOrDefault(a => a.Id == id) is { } automation)
            _ = AutomationWizardWindow.ShowAsync(_host, ActiveWindow(), null, automation);
    }

    public void AddToDesktop(Shortcut shortcut) => Shell(() => _host.Shell.AddToDesktop(shortcut), "the desktop");
    public void AddToStartMenu(Shortcut shortcut) => Shell(() => _host.Shell.AddToStartMenu(shortcut), "the Start menu");

    private void Shell(Action add, string where)
    {
        try { add(); }
        catch (NotSupportedException ex) { _ = Prompts.MessageAsync(ActiveWindow(), "Not available yet", ex.Message); }
        catch (Exception ex) { _ = Prompts.MessageAsync(ActiveWindow(), $"Couldn't add it to {where}", ex.Message); }
    }

    public void Export(Shortcut shortcut) => _host.ExportShortcut(shortcut, ActiveWindow());

    public bool CanDescribeWithAi => false;
    public Task<Shortcut?> DescribeWithAiAsync(string description, Shortcut? current) => Task.FromResult<Shortcut?>(null);

    public void ShowRunDetails(string runId) => _host.OpenRunHistory(runId);

    public string? HotkeyProblem(string hotkey, Guid shortcutId) => _host.HotkeyProblemFor(hotkey, shortcutId);
}
