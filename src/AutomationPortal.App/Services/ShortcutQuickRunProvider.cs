using AutomationPortal.App.Integration;
using AutomationPortal.App.Logic;
using AutomationPortal.Core;

namespace AutomationPortal.App.Services;

/// <summary>Quick Run results for the user's own shortcuts: fuzzy match on name and alias, recents first.</summary>
public sealed class ShortcutQuickRunProvider : IQuickRunProvider
{
    public const int MaxResults = 8;

    private readonly IShortcutLibrary _library;
    private readonly LocalUiSettings _ui;
    private readonly Func<ShortcutRunService?> _runs;

    public ShortcutQuickRunProvider(IShortcutLibrary library, LocalUiSettings ui, Func<ShortcutRunService?>? runs = null)
    {
        _library = library;
        _ui = ui;
        _runs = runs ?? (() => AppHost.Current?.Runs);
    }

    public Task<IReadOnlyList<QuickRunItem>> QueryAsync(string query, QuickRunContext context, CancellationToken ct)
    {
        query = query.Trim();
        var recents = _ui.Current.QuickRunRecents;
        var shortcuts = _library.All.Where(s => s.ShowInQuickRun).ToList();
        var hasPreset = !string.IsNullOrEmpty(context.PresetInput);
        var items = new List<QuickRunItem>();

        if (query.Length == 0)
        {
            // Start page: what you ran lately, then everything else in library order.
            var ordered = recents.Select(id => shortcuts.FirstOrDefault(s => s.Id == id)).Where(s => s is not null).Cast<Shortcut>()
                .Concat(shortcuts).Distinct().Take(MaxResults).ToList();
            for (var i = 0; i < ordered.Count; i++)
                items.Add(Make(ordered[i], 1000 - i, ordered[i].Id is var id && recents.Contains(id) ? "Recent" : null, context, null));
            return Task.FromResult<IReadOnlyList<QuickRunItem>>(items);
        }

        // "gm hello": an exact alias followed by text runs that shortcut with the text as input.
        var space = query.IndexOf(' ');
        if (space > 0)
        {
            var first = query[..space];
            var rest = query[(space + 1)..].Trim();
            var byAlias = shortcuts.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Alias) && s.Alias!.Equals(first, StringComparison.CurrentCultureIgnoreCase));
            if (byAlias is not null && rest.Length > 0)
                items.Add(Make(byAlias, 2000, $"Run with input: {rest}", context, rest, titleOverride: byAlias.Name));
        }

        foreach (var s in shortcuts)
        {
            var score = FuzzyMatcher.ScoreNameAndAlias(query, s.Name, s.Alias);
            if (score is null) continue;
            var bonus = recents.IndexOf(s.Id) is var r and >= 0 ? Math.Max(0, 25 - r) : 0;
            if (hasPreset && s.AcceptsInput.Count > 0) bonus += 40;
            items.Add(Make(s, score.Value + bonus, null, context, null));
        }
        var result = items.OrderByDescending(i => i.Score).Take(MaxResults).ToList();
        return Task.FromResult<IReadOnlyList<QuickRunItem>>(result);
    }

    private QuickRunItem Make(Shortcut s, int score, string? subtitle, QuickRunContext context, string? presetFromAlias, string? titleOverride = null)
    {
        var sub = subtitle ?? (!string.IsNullOrWhiteSpace(s.Alias) ? $"Quick Run name: {s.Alias}" : s.Description);
        return new QuickRunItem
        {
            Id = "shortcut:" + s.Id,
            Title = titleOverride ?? s.Name,
            Subtitle = sub,
            Icon = s.Icon, Color = s.Color, Score = score,
            ActionLabel = "Run",
            AcceptsInput = s.AcceptsInput.Count > 0,
            ShortcutId = s.Id,
            ExecuteAsync = inv =>
            {
                var text = presetFromAlias ?? inv.Input;
                _ui.PushRecent(s.Id);
                var runs = _runs();
                if (runs is null) return Task.CompletedTask;
                return runs.RunAsync(s, string.IsNullOrEmpty(text) ? null : ContentValue.Text(text), RunSource.QuickRun);
            },
        };
    }
}
