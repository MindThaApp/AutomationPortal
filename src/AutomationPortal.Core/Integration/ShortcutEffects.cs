namespace AutomationPortal.Core;

/// <summary>What a whole shortcut can do to files, as shown on its badge and reported to other apps.</summary>
public enum ShortcutEffect
{
    /// <summary>No step changes files or clicks and types in other apps.</summary>
    None,
    /// <summary>The only steps that write save new files beside the originals or in a chosen folder; nothing is replaced.</summary>
    MakesCopies,
    /// <summary>Some step may create, change, move, rename, delete or replace files, or click and type in other apps.</summary>
    ChangesFiles,
}

/// <summary>What one step does, for previews and the request sheet.</summary>
public enum StepEffect
{
    /// <summary>Only shapes the flow (If, Repeat, Menu, a comment).</summary>
    Flow,
    /// <summary>Only reads.</summary>
    Reads,
    /// <summary>Doesn't change files (opens, shows or asks something).</summary>
    NoFileChanges,
    MakesCopies,
    ChangesFiles,
    ControlsApps,
}

/// <summary>
/// The file-change classification shared by the one-time check, the caller prompt, the badges and the pipe. A step changes files
/// unless it is known not to: anything new or unknown counts.
/// </summary>
public static class ShortcutEffects
{
    private static readonly HashSet<string> FlowOnlyIds = new(StringComparer.OrdinalIgnoreCase)
    {
        FlowIds.Comment, FlowIds.If, FlowIds.Menu, FlowIds.Repeat, FlowIds.RepeatEach,
    };

    /// <summary>True unless the step only reads, is marked as not touching files, or only shapes the flow.</summary>
    public static bool ChangesFiles(string actionId) =>
        !FilePreview.ReadOnlyActionIds.Contains(actionId) && !FilePreview.DoesNotChangeFiles(actionId) && !FlowOnlyIds.Contains(actionId);

    /// <summary>The steps that count: every step, and only the first row of an If, Menu or Repeat block.</summary>
    public static IEnumerable<ActionInstance> CountedSteps(Shortcut shortcut) =>
        shortcut.Actions.Where(x => x.Flow is not { Role: not FlowRole.Start });

    public static StepEffect Of(ActionInstance step, ActionRegistry? actions)
    {
        var d = actions?.Describe(step.ActionId);
        if (d is not null && d.ImportRiskOf(step).HasFlag(ImportRisk.ControlsApps)) return StepEffect.ControlsApps;
        if (FlowOnlyIds.Contains(step.ActionId)) return StepEffect.Flow;
        if (FilePreview.ReadOnlyActionIds.Contains(step.ActionId)) return StepEffect.Reads;
        if (FilePreview.DoesNotChangeFiles(step.ActionId)) return StepEffect.NoFileChanges;
        return d is not null && MakesNewFilesOnly(d, step) ? StepEffect.MakesCopies : StepEffect.ChangesFiles;
    }

    // Steps with the shared "Save result" / "Save in folder" settings only ever write new files ("name (2).ext" when taken).
    private static bool MakesNewFilesOnly(ActionDescriptor d, ActionInstance step) =>
        d.ImportRiskOf(step) == ImportRisk.None
        && d.Parameters.Any(p => p.Key == "saveTo") && d.Parameters.Any(p => p.Key == "saveFolder");

    public static ShortcutEffect Of(Shortcut shortcut, ActionRegistry? actions)
    {
        var effect = ShortcutEffect.None;
        foreach (var step in CountedSteps(shortcut))
            switch (Of(step, actions))
            {
                case StepEffect.ChangesFiles or StepEffect.ControlsApps: return ShortcutEffect.ChangesFiles;
                case StepEffect.MakesCopies: effect = ShortcutEffect.MakesCopies; break;
            }
        return effect;
    }

    /// <summary>Everything the shortcut's steps can do that a person should know about (runs programs, downloads, sends data, controls apps).</summary>
    public static ImportRisk Risks(Shortcut shortcut, ActionRegistry? actions) =>
        actions is null ? ImportRisk.None
            : CountedSteps(shortcut).Aggregate(ImportRisk.None, (all, x) => all | (actions.Describe(x.ActionId)?.ImportRiskOf(x) ?? ImportRisk.None));

    /// <summary>"none", "makesCopies" or "changesFiles": the pipe's <c>effects</c> field.</summary>
    public static string WireName(ShortcutEffect effect) => effect switch
    {
        ShortcutEffect.MakesCopies => "makesCopies",
        ShortcutEffect.ChangesFiles => "changesFiles",
        _ => "none",
    };

    /// <summary>The badge text: "No file changes", "Makes copies" or "Changes files".</summary>
    public static string Badge(ShortcutEffect effect) => effect switch
    {
        ShortcutEffect.MakesCopies => "Makes copies",
        ShortcutEffect.ChangesFiles => "Changes files",
        _ => "No file changes",
    };

    public static string WireName(StepEffect effect) => effect switch
    {
        StepEffect.Flow => "flow",
        StepEffect.Reads => "reads",
        StepEffect.NoFileChanges => "none",
        StepEffect.MakesCopies => "makesCopies",
        StepEffect.ControlsApps => "controlsApps",
        _ => "changesFiles",
    };
}
