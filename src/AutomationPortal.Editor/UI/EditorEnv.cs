using System.Windows;
using AutomationPortal.Core;
using AutomationPortal.Editor.Logic;

namespace AutomationPortal.Editor.UI;

/// <summary>What value editors need to know about their surroundings: variables in scope, services for dynamic choices, display rules.</summary>
internal sealed class EditorEnv : IValueDisplayContext
{
    private readonly Dictionary<ParameterDescriptor, IReadOnlyList<Choice>> _choices = new();
    private readonly HashSet<ParameterDescriptor> _loading = new();
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;

    public EditorEnv(AutomationPortalServices? services, ActionRegistry registry, Shortcut shortcut, bool withVariables, EditorDocument? doc = null)
    {
        Services = services;
        Registry = registry;
        Shortcut = shortcut;
        WithVariables = withVariables;
        Doc = doc;
    }

    public AutomationPortalServices? Services { get; }
    public ActionRegistry Registry { get; }
    public Shortcut Shortcut { get; }
    public EditorDocument? Doc { get; }
    public bool WithVariables { get; }
    public event Action? ChoicesLoaded;

    public IReadOnlyList<VariableOption> VariablesAt(int index) =>
        WithVariables ? VariableCatalog.Available(Shortcut, Registry, index) : Array.Empty<VariableOption>();

    public VarDisplay Describe(VarRef reference) => VariableCatalog.Describe(reference, Shortcut, Registry);

    public string? ChoiceLabel(ParameterDescriptor parameter, string id)
    {
        var list = TryGetChoices(parameter);
        return list?.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase))?.Label;
    }

    /// <summary>The choices now, or null while a dynamic list is still loading (<see cref="ChoicesLoaded"/> fires when it is ready).</summary>
    public IReadOnlyList<Choice>? TryGetChoices(ParameterDescriptor p)
    {
        if (p.Choices is not null) return p.Choices;
        if (_choices.TryGetValue(p, out var cached)) return cached;
        if (p.Type is not (ParamType.Choice or ParamType.Shortcut or ParamType.App)) return null;
        var appProvider = p.Type == ParamType.App ? p.ChoicesProvider ?? AppPickerHooks.Provider : p.ChoicesProvider;
        if (appProvider is null && p.Type != ParamType.Shortcut) return null;
        if (!_loading.Add(p)) return null;
        var services = Services;
        _ = Task.Run(async () =>
        {
            IReadOnlyList<Choice> list;
            try
            {
                if (appProvider is { } provider && services is not null) list = await provider(services, CancellationToken.None).ConfigureAwait(false);
                else if (services?.LibraryOrNull is { } lib) list = lib.All.Select(s => new Choice(s.Id.ToString("D"), s.Name, s.Icon)).ToList();
                else list = Array.Empty<Choice>();
            }
            catch { list = Array.Empty<Choice>(); }
            void Done() { _choices[p] = list; _loading.Remove(p); ChoicesLoaded?.Invoke(); }
            if (_ui is not null) _ui.Post(_ => Done(), null);
            else Application.Current?.Dispatcher.BeginInvoke(new Action(Done));
        });
        return null;
    }
}
