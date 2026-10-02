using System.Globalization;

namespace Flowmate.Core;

/// <summary>Receives trigger firings. The Triggers engine implements it (cooldown, conditions, run).</summary>
public interface ITriggerSink
{
    /// <summary>The trigger happened. <paramref name="output"/> becomes the shortcut's input; <paramref name="description"/> is shown in notifications ("Spotify opened").</summary>
    void Fire(ContentValue output, string? description = null);
}

/// <summary>
/// The literal parameter values of a trigger or condition (no variables). Getters fall back to the descriptor default,
/// then to the fallback you pass.
/// </summary>
public sealed class TriggerConfig
{
    private readonly IReadOnlyDictionary<string, ParamValue> _values;
    private readonly IReadOnlyList<ParameterDescriptor> _descriptors;

    public TriggerConfig(IReadOnlyDictionary<string, ParamValue>? values, IReadOnlyList<ParameterDescriptor>? descriptors = null)
    {
        _values = values ?? new Dictionary<string, ParamValue>();
        _descriptors = descriptors ?? Array.Empty<ParameterDescriptor>();
    }

    public static TriggerConfig From(TriggerInstance trigger, IReadOnlyList<ParameterDescriptor>? descriptors = null) => new(trigger.Parameters, descriptors);
    public static TriggerConfig From(ConditionInstance condition, IReadOnlyList<ParameterDescriptor>? descriptors = null) => new(condition.Parameters, descriptors);

    /// <summary>Quick construction for tests: <c>TriggerConfig.Of(("app", ParamValue.Text("Spotify")))</c>.</summary>
    public static TriggerConfig Of(params (string Key, ParamValue Value)[] values) =>
        new(values.ToDictionary(v => v.Key, v => v.Value));

    public IReadOnlyDictionary<string, ParamValue> Values => _values;

    public ParamValue? Get(string key) =>
        _values.TryGetValue(key, out var v) ? v : _descriptors.FirstOrDefault(d => d.Key == key)?.Default;

    public bool Has(string key) => Get(key) is not null;

    public string GetString(string key, string fallback = "") => Get(key)?.ToLiteralText() ?? fallback;

    public double GetNumber(string key, double fallback = 0) => Get(key) switch
    {
        NumberValue n => n.Value,
        DurationValue d => d.Seconds,
        { } other when double.TryParse(other.ToLiteralText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => fallback,
    };

    public int GetInt(string key, int fallback = 0) => (int)Math.Round(GetNumber(key, fallback));

    public bool GetBool(string key, bool fallback = false) => Get(key) switch
    {
        BoolValue b => b.Value,
        { } other => other.ToLiteralText().Trim().ToLowerInvariant() is "true" or "yes" or "1",
        _ => fallback,
    };

    public string GetChoice(string key, string fallback = "") => GetString(key, fallback);

    public TimeOnly? GetTime(string key) => Get(key) switch
    {
        TimeValue t => t.Value,
        { } other when TimeOnly.TryParse(other.ToLiteralText(), CultureInfo.InvariantCulture, out var t) => t,
        _ => null,
    };

    public DateTimeOffset? GetDate(string key) => Get(key) switch
    {
        DateValue d => d.Value,
        { } other when DateTimeOffset.TryParse(other.ToLiteralText(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) => d,
        _ => null,
    };

    public TimeSpan GetDuration(string key, TimeSpan fallback = default) => Get(key) switch
    {
        DurationValue d => d.ToTimeSpan(),
        NumberValue n => TimeSpan.FromSeconds(n.Value),
        _ => fallback,
    };

    /// <summary>A list parameter as strings (choice ids for multi-pick days, app names …). A single value becomes a list of one.</summary>
    public IReadOnlyList<string> GetStrings(string key) => Get(key) switch
    {
        ListValue l => l.Items.Select(i => i.ToLiteralText()).ToList(),
        null => Array.Empty<string>(),
        { } other => other.ToLiteralText() is { Length: > 0 } s ? new[] { s } : Array.Empty<string>(),
    };

    public AppValue? GetApp(string key) => Get(key) as AppValue;
    public string? GetFolder(string key) => Get(key)?.ToLiteralText() is { Length: > 0 } s ? s : null;
}

/// <summary>
/// A source of events that can start shortcuts ("When an app opens"). Public, non-abstract, parameterless constructor;
/// discovered by <see cref="TriggerRegistry.RegisterAssembly"/>.
/// </summary>
public abstract class TriggerProvider
{
    public abstract TriggerDescriptor Descriptor { get; }

    /// <summary>Starts watching. Call <c>sink.Fire(...)</c> when it happens; dispose the returned object to stop (and release hooks, timers, watchers).</summary>
    public abstract IDisposable Start(TriggerConfig config, ITriggerSink sink);
}

/// <summary>An "Only if…" check evaluated when a trigger fires.</summary>
public abstract class ConditionProvider
{
    public abstract ConditionDescriptor Descriptor { get; }
    public abstract Task<bool> IsMetAsync(TriggerConfig config, CancellationToken ct);
}
