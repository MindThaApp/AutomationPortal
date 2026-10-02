using Flowmate.Core;

namespace Flowmate.Triggers;

/// <summary>The text to type for a snippet and where the caret should end up.</summary>
internal sealed record ExpansionPlan(string Text, int CursorBack);

/// <summary>
/// Text expansion: keeps the snippets from the library registered with <see cref="TypedPhraseService"/> and expands them as
/// they are typed. Placeholders come from Core's <see cref="SnippetExpander"/>; {input:Label} fill-ins are asked in one form;
/// Backspace right after an expansion undoes it (handled by the typing service).
/// </summary>
public sealed class SnippetExpansionService : IDisposable
{
    private readonly FlowmateServices _services;
    private readonly TypedPhraseService _phrases;
    private readonly object _gate = new();
    private IDisposable? _registration;
    private Timer? _debounce;
    private bool _started, _disposed;

    public SnippetExpansionService(FlowmateServices services, TypedPhraseService phrases)
    {
        _services = services;
        _phrases = phrases;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
        }
        _services.Library.Changed += OnLibraryChanged;
        Rebuild();
    }

    private void OnLibraryChanged(object? sender, LibraryChangedEventArgs e)
    {
        if (!e.What.HasFlag(LibraryChange.Snippets)) return;
        lock (_gate)
        {
            if (_disposed) return;
            _debounce?.Dispose();
            _debounce = new Timer(_ => TriggerLog.Guard(Rebuild, "Snippet rebuild"), null, 250, Timeout.Infinite);
        }
    }

    private void Rebuild()
    {
        lock (_gate)
        {
            if (_disposed) return;
            var old = _registration;
            var snippets = _services.Library.Snippets.Where(s => s.Enabled && !string.IsNullOrEmpty(s.Abbreviation)).ToList();
            _registration = _phrases.RegisterMany(snippets.Select(s =>
                (s.Abbreviation, s.MatchCase, (Func<PhraseMatch, Task>)(m => ExpandAsync(s, m)))));
            old?.Dispose(); // after registering the new set, so the keyboard hook never flickers off and on
        }
    }

    /// <summary>Pure: placeholders filled in, typed case applied (unless the snippet is case-sensitive), caret offset worked out.</summary>
    internal static ExpansionPlan Plan(Snippet snippet, string typed, DateTimeOffset now, string? clipboardText, IReadOnlyDictionary<string, string>? fillIns)
    {
        var (text, cursor) = SnippetExpander.Expand(snippet.Text.Replace("\r\n", "\n"), now, clipboardText, fillIns);
        if (!snippet.MatchCase && !string.Equals(typed, snippet.Abbreviation, StringComparison.Ordinal))
            text = SnippetExpander.ApplyTypedCase(typed, text);
        return new ExpansionPlan(text, cursor ?? 0);
    }

    private async Task ExpandAsync(Snippet snippet, PhraseMatch match)
    {
        Dictionary<string, string>? fillIns = null;
        var labels = SnippetExpander.FillInLabels(snippet.Text);
        if (labels.Count > 0)
        {
            var ui = _services.UserInteractionOrNull;
            if (ui is null) return;
            try
            {
                var form = await ui.AskForFormAsync($"Fill in \"{snippet.Abbreviation}\"",
                    labels.Select(l => new FormField(l, FormFieldKind.Text)).ToList());
                fillIns = new Dictionary<string, string>(StringComparer.Ordinal);
                if (form.FirstItem is DictionaryItem dict)
                    foreach (var kv in dict.Entries) fillIns[kv.Key] = kv.Value.AsText();
            }
            catch (UserCancelledException) { return; }
            if (!await TypedPhraseService.EnsureWindowAsync(match.Window)) return;
        }

        string? clipboard = null;
        if (snippet.Text.Contains("{clipboard", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                clipboard = _services.ClipboardOrNull is { } c ? (await c.GetAsync()).AsText() : await ClipboardAccess.ReadTextAsync();
            }
            catch { }
        }

        var plan = Plan(snippet, match.Typed, DateTimeOffset.Now, clipboard, fillIns);
        await _phrases.ReplaceTypedAsync(match, plan.Text, plan.CursorBack);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _debounce?.Dispose();
            _registration?.Dispose();
            _registration = null;
        }
        _services.Library.Changed -= OnLibraryChanged;
    }
}
