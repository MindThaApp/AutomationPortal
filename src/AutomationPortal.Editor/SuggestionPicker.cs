namespace AutomationPortal.Editor;

/// <summary>Which suggested automation is the main choice in the draft note.</summary>
public static class SuggestionPicker
{
    /// <summary>True for the first suggestion when the draft was asked for from the Automations page.</summary>
    public static bool IsPrimary(int index, bool forAutomation) => forAutomation && index == 0;
}
