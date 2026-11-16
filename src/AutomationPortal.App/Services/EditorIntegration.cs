using System.Windows;
using AutomationPortal.App.Dialogs;
using AutomationPortal.App.Integration;
using AutomationPortal.App.Logic;
using AutomationPortal.App.Shell;
using AutomationPortal.Core;
using AutomationPortal.Editor;

namespace AutomationPortal.App.Services;

/// <summary>Opens the real shortcut editor (AutomationPortal.Editor) and its icon picker.</summary>
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

    public bool OpenAtStep(Guid shortcutId, string? stepUid)
    {
        ShortcutEditor.OpenAtStep(shortcutId, stepUid, Context, out var found, _host.MainWindowOwner());
        return found;
    }

    public void OpenNew(Guid? folderId, EditorOpenOptions? options = null, string? name = null) => OpenNewWindow(folderId, options, name);

    /// <summary>Opens a new, unsaved shortcut and returns its editor window.</summary>
    public Window OpenNewWindow(Guid? folderId, EditorOpenOptions? options = null, string? wantedName = null)
    {
        var library = _host.Library;
        var name = NameClash.Unique(string.IsNullOrWhiteSpace(wantedName) ? "New Shortcut" : wantedName, n => library.IsNameTaken(n));
        var order = library.All.Count == 0 ? 0 : library.All.Max(s => s.SortOrder) + 1;
        return ShortcutEditor.Open(new Shortcut { Name = name, FolderId = folderId, SortOrder = order }, Context, _host.MainWindowOwner(), options);
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
                var nextRun = a.Enabled ? NextRunText.For(a.Trigger.TriggerId, a.Trigger.Parameters, DateTime.Now) : null;
                return new AutomationSummary(a.Id, sentence, a.Enabled, nextRun);
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

    public bool CanDescribeWithAi => AutomationPortal.Actions.Documents.ShortcutAiService.IsReady(_host.Services);

    public void OpenAiSettings() => _host.OpenAiSettings();

    public Task<AiDraftResult> DescribeWithAiAsync(string description, Shortcut? current, CancellationToken ct = default) =>
        AutomationPortal.Actions.Documents.ShortcutAiService.RunAsync(_host.Services, _host.Actions, _host.Triggers, description, current, ct);

    public void AddSuggestedAutomation(Guid shortcutId, SuggestedAutomation suggestion) =>
        _ = AutomationWizardWindow.ShowAsync(_host, ActiveWindow(), shortcutId, null, suggestion.TriggerId, suggestion.Options);

    public bool CanRecord => true;

    /// <summary>
    /// Record and repeat: pick the app, count down, record the user's clicks and typing in it, then review the steps in plain words.
    /// Returns the steps to insert, an empty list when nothing was recorded, or null when the user cancelled or discarded.
    /// </summary>
    public async Task<IReadOnlyList<ActionInstance>?> RecordMacroAsync(Window editor)
    {
        var picked = await RecordAppPicker.PickAsync(editor);
        if (picked is null) return null;
        var target = new AutomationPortal.Triggers.RecordTarget(picked.ProcessId, picked.AppName, picked.Title);
        var source = _host.RecorderSourceFactory(target);
        var session = new RecordingSession(source);
        var bar = new RecordingBarWindow(session, picked.AppName);
        var previous = editor.WindowState;
        IReadOnlyList<ActionInstance> steps;
        try
        {
            editor.WindowState = WindowState.Minimized;
            try { AutomationPortal.Actions.Windows.Apps.RecordableWindows.Activate(picked); } catch { }
            bar.Show();
            await bar.CountdownAsync(3);
            if (!session.Start())
            {
                bar.Close();
                await Prompts.MessageAsync(editor, "Couldn't start recording", session.StartError ?? "Windows wouldn't let Automation Portal notice your clicks and typing. Try again.");
                return null;
            }
            steps = await session.Finished;
        }
        finally
        {
            if (bar.IsVisible) bar.Close();
            if (editor.WindowState == WindowState.Minimized) editor.WindowState = previous == WindowState.Minimized ? WindowState.Normal : previous;
            editor.Activate();
            (source as IDisposable)?.Dispose();
        }
        if (steps.Count == 0 && session.LeftOut.Count == 0) return steps;
        var review = new AutomationPortal.Core.AppControl.RecordingReview(steps, session.LeftOut);
        return await RecordingReviewDialog.ShowAsync(editor, review, picked.AppName, session.Elapsed);
    }

    public void ShowRunDetails(string runId) => _host.OpenRunHistory(runId);
    public bool CanUndoRun(string runId) => RunUndo.CanUndo(_host.Paths, runId);
    public bool? LinksRunWithoutAsking(Guid shortcutId) =>
        _host.Settings.Current.LinksMode == LinkMode.PerShortcut ? LinkPolicy.IsTrustedFor(_host.Settings.Current, shortcutId, StepsHashOf(shortcutId)) : null;
    /// <summary>Trusts the shortcut's saved steps; after its steps change, links ask again.</summary>
    public void SetLinksRunWithoutAsking(Guid shortcutId, bool on) => LinkPolicy.SetTrusted(_host.Settings, shortcutId, on, StepsHashOf(shortcutId));
    private string? StepsHashOf(Guid shortcutId) => _host.Library.GetById(shortcutId) is { } saved ? LinkPolicy.StepsHash(saved) : null;
    public Task<bool> UndoRunAsync(string runId, Window? owner) => RunUndo.RunAsync(owner ?? ActiveWindow(), _host.Paths, runId);

    public string? HotkeyProblem(string hotkey, Guid shortcutId) => _host.HotkeyProblemFor(hotkey, shortcutId);
}
