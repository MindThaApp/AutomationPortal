namespace AutomationPortal.Core;

public enum AiStepEffect { None, MakesCopies, ChangesFiles, ControlsApps }

public sealed record AiDraftStepLine(string Title, AiStepEffect Effect);

/// <summary>The plain step list shown before a described draft opens in the editor, using the same rule as the one-time file-change check.</summary>
public static class AiDraftSummary
{
    private static readonly HashSet<string> FlowOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        FlowIds.Comment, FlowIds.If, FlowIds.Menu, FlowIds.Repeat, FlowIds.RepeatEach,
    };

    private static readonly HashSet<string> Copies = new(StringComparer.OrdinalIgnoreCase) { "file.copy" };

    public static IReadOnlyList<AiDraftStepLine> Lines(Shortcut draft, ActionRegistry actions) =>
        draft.Actions
            .Where(a => a.Flow is not { Role: not FlowRole.Start })
            .Select(a =>
            {
                var d = actions.Describe(a.ActionId);
                return new AiDraftStepLine(d?.Title ?? a.ActionId, EffectOf(a, d));
            }).ToList();

    private static AiStepEffect EffectOf(ActionInstance a, ActionDescriptor? d)
    {
        if (d is not null && d.ImportRiskOf(a).HasFlag(ImportRisk.ControlsApps)) return AiStepEffect.ControlsApps;
        if (Copies.Contains(a.ActionId)) return AiStepEffect.MakesCopies;
        var changes = !FilePreview.ReadOnlyActionIds.Contains(a.ActionId) && !FilePreview.DoesNotChangeFiles(a.ActionId) && !FlowOnly.Contains(a.ActionId);
        return changes ? AiStepEffect.ChangesFiles : AiStepEffect.None;
    }
}
