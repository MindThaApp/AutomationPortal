using System.Windows;
using Flowmate.Core;

namespace Flowmate.App.Integration;

// Interfaces the integration phase (E) implements or swaps. Each has a working stub/fallback in the App.

/// <summary>Chosen icon (WPF-UI SymbolRegular name) and palette colour key.</summary>
public sealed record IconChoice(string Icon, string Color);

/// <summary>Opens the shortcut editor. The App ships a simple fallback; integration plugs in Flowmate.Editor.</summary>
public interface IShortcutEditorLauncher
{
    /// <summary>Opens (or focuses) the editor window for an existing shortcut.</summary>
    void Open(Guid shortcutId);
    /// <summary>Creates a new shortcut (optionally inside a folder) and opens it.</summary>
    void OpenNew(Guid? folderId);
    /// <summary>Shows the icon + colour picker. Null when cancelled.</summary>
    Task<IconChoice?> PickIconAsync(Window owner, string icon, string color);
}

/// <summary>Windows integration the App itself doesn't do (registry, shell links). Stub until integration.</summary>
public interface IShellIntegration
{
    bool IsStartWithWindowsEnabled { get; }
    void SetStartWithWindows(bool enabled);
    /// <summary>Puts a double-clickable shortcut on the desktop. Throws <see cref="NotSupportedException"/> when unavailable.</summary>
    void AddToDesktop(Shortcut shortcut);
}

public sealed record AiKeyTestResult(bool Ok, string Message);

/// <summary>Checks an AI key against the provider (Settings, "Test key").</summary>
public interface IAiKeyTester
{
    Task<AiKeyTestResult> TestAsync(string apiKey, AiModelTier model, CancellationToken ct = default);
}

/// <summary>A live form for a list of parameters. It writes straight into the dictionary it was created with.</summary>
public interface IParameterForm
{
    FrameworkElement Element { get; }
    /// <summary>Raised after any value changed.</summary>
    event EventHandler? Changed;
    /// <summary>Labels of required parameters that are visible but empty.</summary>
    IReadOnlyList<string> MissingRequired();
}

/// <summary>Builds the options form for triggers and conditions (literal values only). Integration may swap in the editor's form.</summary>
public interface IParameterFormFactory
{
    IParameterForm Create(IReadOnlyList<ParameterDescriptor> parameters, Dictionary<string, ParamValue> values, FlowmateServices services);
}

/// <summary>What a Quick Run result does when chosen.</summary>
public sealed class QuickRunInvocation
{
    /// <summary>The text in the search box when Enter was pressed.</summary>
    public string Query { get; init; } = "";
    /// <summary>Text to hand over as input (Tab mode, "Run on selection"). Null when there is none.</summary>
    public string? Input { get; init; }
}

public sealed class QuickRunItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    /// <summary>WPF-UI SymbolRegular name.</summary>
    public string Icon { get; init; } = "Flash24";
    /// <summary>Palette key used for the icon chip.</summary>
    public string Color { get; init; } = "blue";
    /// <summary>Higher shows first. Shortcut matches use 0..1100 (see FuzzyMatcher).</summary>
    public int Score { get; init; }
    /// <summary>Small hint at the right ("Run", "Open", "Search").</summary>
    public string ActionLabel { get; init; } = "Run";
    /// <summary>True when Tab can turn this result into "Run with input".</summary>
    public bool AcceptsInput { get; init; }
    /// <summary>Optional text to place on the clipboard-like "result" (calculator) for the Copy hint.</summary>
    public string? CopyText { get; init; }
    public Guid? ShortcutId { get; init; }
    public required Func<QuickRunInvocation, Task> ExecuteAsync { get; init; }
}

public sealed class QuickRunContext
{
    /// <summary>Set when Quick Run was opened with text already selected ("Run on selection").</summary>
    public string? PresetInput { get; init; }
}

/// <summary>A source of Quick Run results. Integration adds calculator, conversions, web search and Ask AI providers.</summary>
public interface IQuickRunProvider
{
    /// <summary>Results for the text typed so far. Empty query asks for the "start page" (recents). Must honour <paramref name="ct"/>.</summary>
    Task<IReadOnlyList<QuickRunItem>> QueryAsync(string query, QuickRunContext context, CancellationToken ct);
}
