using AutomationPortal.App.Integration;

namespace AutomationPortal.App.Logic;

/// <summary>The two rows Quick Run adds at the bottom when the typed text matches none of the user's shortcuts.</summary>
public static class QuickRunFallbacks
{
    public const string AskId = "fallback-ask-ai", SetupId = "fallback-setup-ai", MakeId = "fallback-make-shortcut";
    public const int MinLength = 2;

    public static bool IsFallback(QuickRunItem item) => item.Id is AskId or SetupId or MakeId;

    public static bool IsAsk(QuickRunItem item) => item.Id == AskId;

    /// <summary>True when something was typed and no result is one of the user's own shortcuts (calculator or web rows don't count).</summary>
    public static bool Applies(string typed, IEnumerable<QuickRunItem> results) =>
        typed.Trim().Length >= MinLength && !results.Any(r => r.ShortcutId is not null);

    /// <param name="privacy">The provider's privacy line, shown under "Ask AI" while an AI is ready.</param>
    public static IReadOnlyList<QuickRunItem> Build(string typed, bool aiReady, string privacy, Func<Task> ask, Func<Task> setup, Func<Task> make)
    {
        var text = typed.Trim();
        var first = aiReady
            ? new QuickRunItem
            {
                Id = AskId, Title = string.Format(Strings.Get("QuickRunFallback_AskAi"), text), Subtitle = privacy.Length > 0 ? privacy : null, Icon = "Sparkle24", Color = "indigo",
                Score = -10, ActionLabel = Strings.Get("QuickRunFallback_Ask"), KeepsWindowOpen = true, ExecuteAsync = _ => ask(),
            }
            : new QuickRunItem
            {
                Id = SetupId, Title = Strings.Get("QuickRunFallback_SetUpAi"), Subtitle = Strings.Get("QuickRunFallback_SetUpAiHint"), Icon = "Sparkle24", Color = "indigo",
                Score = -10, ActionLabel = Strings.Get("QuickRunFallback_SetUp"), ExecuteAsync = _ => setup(),
            };
        var second = new QuickRunItem
        {
            Id = MakeId, Title = string.Format(Strings.Get("QuickRunFallback_MakeShortcut"), text), Subtitle = Strings.Get("QuickRunFallback_MakeShortcutHint"), Icon = "Add24", Color = "teal",
            Score = -20, ActionLabel = Strings.Get("QuickRunFallback_Describe"), ExecuteAsync = _ => make(),
        };
        return new[] { first, second };
    }
}
