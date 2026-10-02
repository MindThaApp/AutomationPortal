using System.Reflection;

namespace Flowmate.Core;

internal static class RegistryScanner
{
    public static IEnumerable<T> Instantiate<T>(Assembly assembly) where T : class
    {
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }
        foreach (var type in types)
        {
            if (type.IsAbstract || !type.IsPublic && !type.IsNestedPublic || !typeof(T).IsAssignableFrom(type)) continue;
            if (type.GetConstructor(Type.EmptyTypes) is null) continue;
            yield return (T)Activator.CreateInstance(type)!;
        }
    }
}

/// <summary>All known actions. Create with <see cref="CreateWithBuiltIns"/>, then <see cref="RegisterAssembly"/> each action library.</summary>
public sealed class ActionRegistry
{
    private readonly Dictionary<string, ShortcutAction> _byId = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Problems found while registering (duplicate ids, broken descriptors). The first registration wins.</summary>
    public List<string> Diagnostics { get; } = new();

    public static ActionRegistry CreateWithBuiltIns()
    {
        var r = new ActionRegistry();
        r.RegisterAssembly(typeof(ActionRegistry).Assembly);
        return r;
    }

    public void Register(ShortcutAction action)
    {
        var id = action.Descriptor.Id;
        if (!_byId.TryAdd(id, action)) Diagnostics.Add($"Duplicate action id '{id}' ({action.GetType().FullName}) ignored.");
    }

    /// <summary>Registers every public non-abstract <see cref="ShortcutAction"/> with a public parameterless constructor.</summary>
    public int RegisterAssembly(Assembly assembly)
    {
        var n = 0;
        foreach (var a in RegistryScanner.Instantiate<ShortcutAction>(assembly))
        {
            try { Register(a); n++; }
            catch (Exception ex) { Diagnostics.Add($"{a.GetType().FullName}: {ex.Message}"); }
        }
        return n;
    }

    public IReadOnlyCollection<ShortcutAction> All => _byId.Values;
    public ShortcutAction? Get(string id) => _byId.TryGetValue(id, out var a) ? a : null;
    public bool TryGet(string id, out ShortcutAction action) => _byId.TryGetValue(id, out action!);
    public ActionDescriptor? Describe(string id) => Get(id)?.Descriptor;

    /// <summary>Categories that contain at least one (visible) action, in the standard display order.</summary>
    public IReadOnlyList<string> Categories(bool includeAdvanced = false) =>
        ActionCategories.All.Where(c => _byId.Values.Any(a => a.Descriptor.Category == c && (includeAdvanced || !a.Descriptor.IsAdvanced))).ToList();

    public IReadOnlyList<ActionDescriptor> ByCategory(string category, bool includeAdvanced = false) =>
        _byId.Values.Select(a => a.Descriptor)
            .Where(d => d.Category == category && (includeAdvanced || !d.IsAdvanced))
            .OrderBy(d => d.Title, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Ranked search over title, keywords, category and description. Empty query returns nothing.</summary>
    public IReadOnlyList<ActionDescriptor> Search(string query, bool includeAdvanced = false)
    {
        var q = query.Trim();
        if (q.Length == 0) return Array.Empty<ActionDescriptor>();
        var scored = new List<(ActionDescriptor D, int Score)>();
        foreach (var d in _byId.Values.Select(a => a.Descriptor))
        {
            if (d.IsAdvanced && !includeAdvanced) continue;
            var score = Score(d, q);
            if (score > 0) scored.Add((d, score));
        }
        return scored.OrderByDescending(s => s.Score).ThenBy(s => s.D.Title, StringComparer.CurrentCultureIgnoreCase).Select(s => s.D).ToList();
    }

    private static int Score(ActionDescriptor d, string q)
    {
        var cmp = StringComparison.CurrentCultureIgnoreCase;
        var best = 0;
        if (d.Title.Equals(q, cmp)) best = 100;
        else if (d.Title.StartsWith(q, cmp)) best = 80;
        else if (d.Title.Split(' ').Any(w => w.StartsWith(q, cmp))) best = 70;
        else if (d.Title.Contains(q, cmp)) best = 60;
        if (d.Keywords.Any(k => k.Equals(q, cmp))) best = Math.Max(best, 65);
        else if (d.Keywords.Any(k => k.StartsWith(q, cmp))) best = Math.Max(best, 55);
        else if (d.Keywords.Any(k => k.Contains(q, cmp))) best = Math.Max(best, 40);
        if (d.Category.Contains(q, cmp)) best = Math.Max(best, 30);
        if (d.Id.Contains(q, cmp)) best = Math.Max(best, 25);
        if (d.Description.Contains(q, cmp)) best = Math.Max(best, 10);
        return best;
    }
}

public sealed class TriggerRegistry
{
    private readonly Dictionary<string, TriggerProvider> _byId = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Diagnostics { get; } = new();

    public void Register(TriggerProvider provider)
    {
        var id = provider.Descriptor.Id;
        if (!_byId.TryAdd(id, provider)) Diagnostics.Add($"Duplicate trigger id '{id}' ({provider.GetType().FullName}) ignored.");
    }

    public int RegisterAssembly(Assembly assembly)
    {
        var n = 0;
        foreach (var p in RegistryScanner.Instantiate<TriggerProvider>(assembly))
        {
            try { Register(p); n++; }
            catch (Exception ex) { Diagnostics.Add($"{p.GetType().FullName}: {ex.Message}"); }
        }
        return n;
    }

    public IReadOnlyCollection<TriggerProvider> All => _byId.Values;
    public TriggerProvider? Get(string id) => _byId.TryGetValue(id, out var p) ? p : null;
    public IReadOnlyList<string> Categories => _byId.Values.Select(p => p.Descriptor.Category).Distinct().ToList();
    public IReadOnlyList<TriggerDescriptor> ByCategory(string category) =>
        _byId.Values.Select(p => p.Descriptor).Where(d => d.Category == category).OrderBy(d => d.Title).ToList();

    public IReadOnlyList<TriggerDescriptor> Search(string query)
    {
        var q = query.Trim();
        return _byId.Values.Select(p => p.Descriptor)
            .Where(d => q.Length == 0 || d.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase) || d.Description.Contains(q, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(d => d.Title).ToList();
    }
}

public sealed class ConditionRegistry
{
    private readonly Dictionary<string, ConditionProvider> _byId = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Diagnostics { get; } = new();

    public void Register(ConditionProvider provider)
    {
        var id = provider.Descriptor.Id;
        if (!_byId.TryAdd(id, provider)) Diagnostics.Add($"Duplicate condition id '{id}' ({provider.GetType().FullName}) ignored.");
    }

    public int RegisterAssembly(Assembly assembly)
    {
        var n = 0;
        foreach (var p in RegistryScanner.Instantiate<ConditionProvider>(assembly))
        {
            try { Register(p); n++; }
            catch (Exception ex) { Diagnostics.Add($"{p.GetType().FullName}: {ex.Message}"); }
        }
        return n;
    }

    public IReadOnlyCollection<ConditionProvider> All => _byId.Values;
    public ConditionProvider? Get(string id) => _byId.TryGetValue(id, out var p) ? p : null;
    public IReadOnlyList<ConditionDescriptor> Search(string query)
    {
        var q = query.Trim();
        return _byId.Values.Select(p => p.Descriptor)
            .Where(d => q.Length == 0 || d.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(d => d.Title).ToList();
    }
}
