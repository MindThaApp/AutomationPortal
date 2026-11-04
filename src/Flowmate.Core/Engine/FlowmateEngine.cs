using System.Reflection;

namespace Flowmate.Core;

/// <summary>
/// One-call hosting: the action registry, the host services and a <see cref="ShortcutRunner"/>, built by
/// <see cref="FlowmateEngine.CreateBuilder"/>. Any service the host leaves unset fails with the usual "This app can't … here" error
/// when an action needs it.
/// </summary>
public sealed class FlowmateEngineBuilder
{
    private readonly List<Assembly> _assemblies = new();
    private readonly List<Action<FlowmateServices>> _configure = new();

    /// <summary>The services the engine will use. Set what the host has; leave the rest.</summary>
    public FlowmateServices Services { get; } = new();

    /// <summary>Adds every action in the assembly (public, non-abstract, public parameterless constructor). Flowmate's libraries and the host's own.</summary>
    public FlowmateEngineBuilder AddActions(Assembly assembly)
    {
        if (!_assemblies.Contains(assembly)) _assemblies.Add(assembly);
        return this;
    }

    /// <summary>Adds the actions in the assembly that contains <typeparamref name="T"/>.</summary>
    public FlowmateEngineBuilder AddActionsFrom<T>() => AddActions(typeof(T).Assembly);

    /// <summary>Where per-run temp folders, saved values and history live (default <see cref="FlowmatePaths.Default"/>).</summary>
    public FlowmateEngineBuilder UsePaths(FlowmatePaths paths)
    {
        Services.Paths = paths;
        return this;
    }

    public FlowmateEngineBuilder ConfigureServices(Action<FlowmateServices> configure)
    {
        _configure.Add(configure);
        return this;
    }

    /// <summary>Creates the registry (Core's built-in actions plus every added assembly), applies the service setup and creates the runner.</summary>
    public FlowmateEngine Build()
    {
        var actions = ActionRegistry.CreateWithBuiltIns();
        foreach (var a in _assemblies) actions.RegisterAssembly(a);
        foreach (var c in _configure) c(Services);
        return new FlowmateEngine(actions, Services);
    }
}

/// <summary>A built engine: registry, services and runner. Thread-safe for concurrent runs.</summary>
public sealed class FlowmateEngine
{
    internal FlowmateEngine(ActionRegistry actions, FlowmateServices services)
    {
        Actions = actions;
        Services = services;
        Runner = new ShortcutRunner(actions, services);
    }

    public static FlowmateEngineBuilder CreateBuilder() => new();

    public ActionRegistry Actions { get; }
    public FlowmateServices Services { get; }
    public ShortcutRunner Runner { get; }

    /// <summary>Runs a shortcut. <paramref name="input"/> is the shortcut input (e.g. <c>ContentValue.Files(paths)</c>); named variables go in <see cref="RunOptions.SeedVariables"/>.</summary>
    public Task<RunResult> RunAsync(Shortcut shortcut, ContentValue? input = null, RunOptions? options = null) =>
        Runner.RunAsync(shortcut, input ?? ContentValue.Empty, options);

    /// <summary>Problems that would stop the shortcut running here (unknown actions, missing required values, broken blocks).</summary>
    public IReadOnlyList<ValidationIssue> Validate(Shortcut shortcut) => ShortcutValidator.Validate(shortcut, Actions);

    public Shortcut LoadShortcut(string path) => ShortcutPackage.Load(path);
    public Shortcut LoadShortcut(Stream stream) => ShortcutPackage.Load(stream);
    public void SaveShortcut(Shortcut shortcut, string path) => ShortcutPackage.Save(shortcut, path);
    public void SaveShortcut(Shortcut shortcut, Stream stream) => ShortcutPackage.Save(shortcut, stream);
}
