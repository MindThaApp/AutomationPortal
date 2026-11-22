using AutomationPortal.Core;

namespace AutomationPortal.Editor;

/// <summary>Lets the host app say more about an action than its descriptor can, for example where an AI action's text goes.</summary>
public static class ActionNotes
{
    /// <summary>Replaces the "Needs an internet connection" note on the action's Help card when it returns a sentence.</summary>
    public static Func<ActionDescriptor, string?>? PrivacyNote { get; set; }
}
