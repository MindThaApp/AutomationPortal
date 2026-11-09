using System.Windows;
using AutomationPortal.Core;

namespace AutomationPortal.Editor;

/// <summary>One automation that runs a shortcut, as shown in the editor's Details sheet.</summary>
public sealed record AutomationSummary(Guid Id, string Sentence, bool Enabled);

/// <summary>Things the editor needs from the app around it. Implemented by AutomationPortal.App at integration.</summary>
public interface IEditorHost
{
    IReadOnlyList<AutomationSummary> GetAutomationsFor(Guid shortcutId);
    /// <summary>Starts the "new automation" flow with this shortcut preselected. The shortcut is saved before this is called.</summary>
    void AddTrigger(Guid shortcutId);
    void EditAutomation(Guid id);
    void AddToDesktop(Shortcut shortcut);
    void AddToStartMenu(Shortcut shortcut);
    void Export(Shortcut shortcut);
    /// <summary>True when the default AI provider is set up, so "Describe it" and "Describe a change" can be offered.</summary>
    bool CanDescribeWithAi { get; }
    /// <summary>Opens Settings › AI (the hint shown when no AI is set up links here).</summary>
    void OpenAiSettings() { }
    /// <summary>
    /// Builds a draft from a plain sentence, or (when <paramref name="current"/> is given) a revised copy of that shortcut.
    /// Never throws for AI trouble: the failure comes back in <see cref="AiDraftResult.Error"/> in plain words. Nothing is run or saved.
    /// </summary>
    Task<AiDraftResult> DescribeWithAiAsync(string description, Shortcut? current, CancellationToken ct = default);
    /// <summary>The user accepted an automation the AI suggested: open the new-automation wizard prefilled. The shortcut is saved before this is called.</summary>
    void AddSuggestedAutomation(Guid shortcutId, SuggestedAutomation suggestion) { }
    /// <summary>True when "Record" can be offered (the host owns the recorder and its floating bar).</summary>
    bool CanRecord => false;
    /// <summary>
    /// Records clicks and typing while showing the host's floating recording bar. The editor window is minimised meanwhile and
    /// restored afterwards. Returns the recorded steps, or null when recording couldn't start or nothing was kept.
    /// </summary>
    Task<IReadOnlyList<ActionInstance>?> RecordMacroAsync(Window editor) => Task.FromResult<IReadOnlyList<ActionInstance>?>(null);
    /// <summary>Opens the run details (Run History) for a run started from the editor. <paramref name="runId"/> is <c>RunResult.RunId</c> (Core uses a 12-character string, not a Guid).</summary>
    void ShowRunDetails(string runId);
    /// <summary>Why this keyboard shortcut can't work for the shortcut (already used by Automation Portal, another shortcut or another app), or null when it is fine.</summary>
    string? HotkeyProblem(string hotkey, Guid shortcutId) => null;
}

/// <summary>Host that does nothing; handy for tests and the harness.</summary>
public sealed class NullEditorHost : IEditorHost
{
    public static NullEditorHost Instance { get; } = new();
    public IReadOnlyList<AutomationSummary> GetAutomationsFor(Guid shortcutId) => Array.Empty<AutomationSummary>();
    public void AddTrigger(Guid shortcutId) { }
    public void EditAutomation(Guid id) { }
    public void AddToDesktop(Shortcut shortcut) { }
    public void AddToStartMenu(Shortcut shortcut) { }
    public void Export(Shortcut shortcut) { }
    public bool CanDescribeWithAi => false;
    public Task<AiDraftResult> DescribeWithAiAsync(string description, Shortcut? current, CancellationToken ct = default) =>
        Task.FromResult(AiDraftResult.Failed("AI isn't available here."));
    public void ShowRunDetails(string runId) { }
}

/// <summary>Everything an editor window needs. Build one per app and pass it to <see cref="ShortcutEditor.Open(Guid, EditorContext, System.Windows.Window?)"/>.</summary>
public sealed class EditorContext
{
    public EditorContext(AutomationPortalServices services, ActionRegistry actions, IEditorHost? host = null,
        IShortcutLibrary? library = null, IShortcutRunner? runner = null)
    {
        Services = services;
        Actions = actions;
        Host = host ?? NullEditorHost.Instance;
        Library = library ?? services.Library;
        Runner = runner ?? services.Runner;
    }

    public AutomationPortalServices Services { get; }
    public ActionRegistry Actions { get; }
    public IShortcutLibrary Library { get; }
    public IShortcutRunner Runner { get; }
    public IEditorHost Host { get; }

    public AppSettings Settings => Services.Settings.Current;
}
