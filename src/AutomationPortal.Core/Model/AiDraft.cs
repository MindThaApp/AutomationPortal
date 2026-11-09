namespace Flowmate.Core;

/// <summary>An automation the AI thinks fits a described shortcut ("every morning" → At a time of day). Never created without the user's say-so.</summary>
public sealed record SuggestedAutomation(string TriggerId, string Label, IReadOnlyDictionary<string, ParamValue> Options);

/// <summary>What "Describe it" / "Describe a change" hands back to the editor.</summary>
public sealed record AiDraftResult
{
    /// <summary>The drafted (or revised) shortcut, already checked by the validator. Null when it failed.</summary>
    public Shortcut? Shortcut { get; init; }
    /// <summary>Plain-English reason it failed (when <see cref="Shortcut"/> is null).</summary>
    public string? Error { get; init; }
    /// <summary>One or two sentences: what the draft does, or what changed.</summary>
    public string Summary { get; init; } = "";
    public IReadOnlyList<SuggestedAutomation> Automations { get; init; } = Array.Empty<SuggestedAutomation>();
    /// <summary>Problems the validator still found after the repair rounds (shown, not fatal).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public bool Success => Shortcut is not null;

    public static AiDraftResult Failed(string error) => new() { Error = error };
}
