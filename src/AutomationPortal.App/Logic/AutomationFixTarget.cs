namespace AutomationPortal.App.Logic;

/// <summary>The part of the automation sheet a problem is about.</summary>
public enum WizardFocus { None, Trigger, Shortcut }

public enum AutomationProblem { LastRunFailed, TriggerError, TriggerMissing, ShortcutMissing }

/// <summary>Where a problem line's fix button leads.</summary>
public static class AutomationFixTarget
{
    /// <summary>The section of the sheet to open at. A failed run is fixed in Run history, so it has none.</summary>
    public static WizardFocus For(AutomationProblem problem) => problem switch
    {
        AutomationProblem.TriggerError or AutomationProblem.TriggerMissing => WizardFocus.Trigger,
        AutomationProblem.ShortcutMissing => WizardFocus.Shortcut,
        _ => WizardFocus.None,
    };
}
