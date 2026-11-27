using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

public enum WizardStep { Trigger = 0, Options = 1, Shortcut = 2, Settings = 3 }

public sealed class ConditionDraft
{
    public required string ConditionId { get; init; }
    public Dictionary<string, ParamValue> Values { get; } = new();
}

/// <summary>State and rules of the New Automation wizard (no UI, so it can be tested).</summary>
public sealed class AutomationWizardState
{
    public const WizardStep First = WizardStep.Trigger;
    public const WizardStep Last = WizardStep.Settings;

    public WizardStep Step { get; private set; } = First;

    // Step 1 + 2
    public string? TriggerId { get; private set; }
    public Dictionary<string, ParamValue> TriggerValues { get; private set; } = new();
    public List<ConditionDraft> Conditions { get; } = new();

    // Step 3
    public Guid? ExistingShortcutId { get; set; }
    public bool CreateNewShortcut { get; set; }
    public string NewShortcutName { get; set; } = "";

    // Step 4
    public RunMode RunMode { get; set; } = RunMode.Immediately;
    public bool NotifyWhenRun { get; set; }
    public int CooldownMinutes { get; set; }
    public bool AllowFrequentRuns { get; set; }
    public bool RunIfMissed { get; set; }

    /// <summary>The schedule triggers that can catch up a missed time.</summary>
    public static bool IsScheduleTrigger(string? triggerId) => triggerId is "time.daily" or "time.monthly" or "time.interval" or "time.once";

    public void SelectTrigger(string triggerId)
    {
        if (string.Equals(TriggerId, triggerId, StringComparison.OrdinalIgnoreCase)) return;
        TriggerId = triggerId;
        TriggerValues = new Dictionary<string, ParamValue>();
    }

    /// <summary>Goes back to "choose what starts it" (the options of the old trigger are dropped).</summary>
    public void ClearTrigger()
    {
        TriggerId = null;
        TriggerValues = new Dictionary<string, ParamValue>();
    }

    /// <summary>Picks a trigger for the one-sentence flow and fills in its starting values (a daily schedule starts at 08:00).</summary>
    public void ChooseTrigger(string triggerId)
    {
        if (string.Equals(TriggerId, triggerId, StringComparison.OrdinalIgnoreCase)) return;
        SelectTrigger(triggerId);
        if (string.Equals(triggerId, "time.daily", StringComparison.OrdinalIgnoreCase))
            TriggerValues["time"] = ParamValue.Time(WhenSummary.DefaultTime.ToString("HH':'mm", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Why the one-sentence screen can't be turned on yet (null = it can): the trigger, its required options and the shortcut.</summary>
    public string? ReadyMessage(TriggerRegistry triggers, ConditionRegistry conditions, IShortcutLibrary? library = null)
    {
        var t = triggers.Get(TriggerId ?? "")?.Descriptor;
        if (t is null) return Strings.Get("AutomationWizardState_ChooseWhatShouldStart");
        var missing = LiteralFormatter.MissingRequired(t.Parameters, TriggerValues).ToList();
        foreach (var c in Conditions)
        {
            var cd = conditions.Get(c.ConditionId)?.Descriptor;
            if (cd is not null) missing.AddRange(LiteralFormatter.MissingRequired(cd.Parameters, c.Values));
        }
        if (missing.Count > 0) return Strings.Get("AutomationWizardState_FillIn") + string.Join(", ", missing.Distinct()) + ".";
        if (CreateNewShortcut)
        {
            if (string.IsNullOrWhiteSpace(NewShortcutName)) return Strings.Get("AutomationWizardState_GiveTheNewShortcut");
            if (library?.IsNameTaken(NewShortcutName) == true) return Strings.Get("AutomationWizardState_AShortcutWithThat");
            return null;
        }
        return ExistingShortcutId is null ? Strings.Get("AutomationWizardState_ChooseAShortcutTo") : null;
    }

    public ConditionDraft AddCondition(string conditionId)
    {
        var c = new ConditionDraft { ConditionId = conditionId };
        Conditions.Add(c);
        return c;
    }

    /// <summary>Why the user can't go on from the current step (null = they can).</summary>
    public string? BlockingMessage(TriggerRegistry triggers, ConditionRegistry conditions, IShortcutLibrary? library = null)
    {
        switch (Step)
        {
            case WizardStep.Trigger:
                return TriggerId is null ? Strings.Get("AutomationWizardState_PickWhatShouldStart") : null;
            case WizardStep.Options:
            {
                var t = triggers.Get(TriggerId ?? "")?.Descriptor;
                if (t is null) return Strings.Get("AutomationWizardState_PickWhatShouldStart");
                var missing = LiteralFormatter.MissingRequired(t.Parameters, TriggerValues).ToList();
                foreach (var c in Conditions)
                {
                    var cd = conditions.Get(c.ConditionId)?.Descriptor;
                    if (cd is not null) missing.AddRange(LiteralFormatter.MissingRequired(cd.Parameters, c.Values));
                }
                return missing.Count == 0 ? null : Strings.Get("AutomationWizardState_FillIn") + string.Join(", ", missing.Distinct()) + ".";
            }
            case WizardStep.Shortcut:
                if (CreateNewShortcut)
                {
                    if (string.IsNullOrWhiteSpace(NewShortcutName)) return Strings.Get("AutomationWizardState_GiveTheNewShortcut");
                    if (library?.IsNameTaken(NewShortcutName) == true) return Strings.Get("AutomationWizardState_AShortcutWithThat");
                    return null;
                }
                return ExistingShortcutId is null ? Strings.Get("AutomationWizardState_ChooseAShortcutTo") : null;
            default:
                return null;
        }
    }

    public bool TryNext(TriggerRegistry triggers, ConditionRegistry conditions, IShortcutLibrary? library = null)
    {
        if (BlockingMessage(triggers, conditions, library) is not null || Step == Last) return false;
        Step = (WizardStep)((int)Step + 1);
        return true;
    }

    public bool TryBack()
    {
        if (Step == First) return false;
        Step = (WizardStep)((int)Step - 1);
        return true;
    }

    public Automation Build(Guid shortcutId) => new()
    {
        Enabled = true,
        Trigger = new TriggerInstance { TriggerId = TriggerId ?? "", Parameters = new Dictionary<string, ParamValue>(TriggerValues) },
        Conditions = Conditions.Select(c => new ConditionInstance { ConditionId = c.ConditionId, Parameters = new Dictionary<string, ParamValue>(c.Values) }).ToList(),
        ShortcutId = shortcutId,
        RunMode = RunMode,
        NotifyWhenRun = NotifyWhenRun,
        CooldownSeconds = Math.Max(0, CooldownMinutes) * 60,
        AllowFrequentRuns = AllowFrequentRuns,
        RunIfMissed = IsScheduleTrigger(TriggerId) && RunIfMissed,
    };

    public static AutomationWizardState FromAutomation(Automation a)
    {
        var s = new AutomationWizardState { ExistingShortcutId = a.ShortcutId, RunMode = a.RunMode, NotifyWhenRun = a.NotifyWhenRun, CooldownMinutes = a.CooldownSeconds / 60, AllowFrequentRuns = a.AllowFrequentRuns, RunIfMissed = a.RunIfMissed };
        s.SelectTrigger(a.Trigger.TriggerId);
        s.TriggerValues = new Dictionary<string, ParamValue>(a.Trigger.Parameters);
        foreach (var c in a.Conditions)
        {
            var d = s.AddCondition(c.ConditionId);
            foreach (var (k, v) in c.Parameters) d.Values[k] = v;
        }
        return s;
    }
}
