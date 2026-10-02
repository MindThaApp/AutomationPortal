using Flowmate.Core;

namespace Flowmate.Editor;

/// <summary>One automation that runs a shortcut, as shown in the editor's Details sheet.</summary>
public sealed record AutomationSummary(Guid Id, string Sentence, bool Enabled);

/// <summary>Things the editor needs from the app around it. Implemented by Flowmate.App at integration.</summary>
public interface IEditorHost
{
    IReadOnlyList<AutomationSummary> GetAutomationsFor(Guid shortcutId);
    /// <summary>Starts the "new automation" flow with this shortcut preselected. The shortcut is saved before this is called.</summary>
    void AddTrigger(Guid shortcutId);
    void EditAutomation(Guid id);
    void AddToDesktop(Shortcut shortcut);
    void AddToStartMenu(Shortcut shortcut);
    void Export(Shortcut shortcut);
    /// <summary>True when an AI key is set up, so "Describe it" can be offered for new shortcuts.</summary>
    bool CanDescribeWithAi { get; }
    /// <summary>Builds a draft from a plain sentence (or changes <paramref name="current"/>). Null when it couldn't.</summary>
    Task<Shortcut?> DescribeWithAiAsync(string description, Shortcut? current);
    /// <summary>Opens the run details (Run History) for a run started from the editor. <paramref name="runId"/> is <c>RunResult.RunId</c> (Core uses a 12-character string, not a Guid).</summary>
    void ShowRunDetails(string runId);
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
    public Task<Shortcut?> DescribeWithAiAsync(string description, Shortcut? current) => Task.FromResult<Shortcut?>(null);
    public void ShowRunDetails(string runId) { }
}

/// <summary>Everything an editor window needs. Build one per app and pass it to <see cref="ShortcutEditor.Open(Guid, EditorContext, System.Windows.Window?)"/>.</summary>
public sealed class EditorContext
{
    public EditorContext(FlowmateServices services, ActionRegistry actions, IEditorHost? host = null,
        IShortcutLibrary? library = null, IShortcutRunner? runner = null)
    {
        Services = services;
        Actions = actions;
        Host = host ?? NullEditorHost.Instance;
        Library = library ?? services.Library;
        Runner = runner ?? services.Runner;
    }

    public FlowmateServices Services { get; }
    public ActionRegistry Actions { get; }
    public IShortcutLibrary Library { get; }
    public IShortcutRunner Runner { get; }
    public IEditorHost Host { get; }

    public AppSettings Settings => Services.Settings.Current;
}
