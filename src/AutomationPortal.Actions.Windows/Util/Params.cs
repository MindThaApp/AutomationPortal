using AutomationPortal.Actions.Windows.Apps;

namespace AutomationPortal.Actions.Windows;

/// <summary>Parameter descriptors that several actions share.</summary>
internal static class P
{
    public static ParameterDescriptor App(string key = "app", string? label = null, bool required = true) =>
        new() { Key = key, Label = label ?? Strings.Get("P_Param_App_Label"), Type = ParamType.App, ChoicesProvider = InstalledApps.ChoicesProvider, Required = required };

    public static ParameterDescriptor OnOffToggle(string key, string label, string on, string off, string defaultId = "toggle") =>
        ParameterDescriptor.ChoiceOf(key, label, defaultId, new Choice("on", on), new Choice("off", off), new Choice("toggle", Strings.Get("P_Param_X_Choice_Toggle")));

    public static ParameterDescriptor Percent(string key, string label, double def) =>
        ParameterDescriptor.Number(key, label, def, 0, 100, "%") with { Step = 1 };

    /// <summary>Which window an action works on: the front window, an app's window, or a window from the previous action.</summary>
    public static ParameterDescriptor[] WindowTarget(string defaultTarget = "front") => new[]
    {
        ParameterDescriptor.ChoiceOf("target", Strings.Get("P_Param_Target_Label"), defaultTarget,
            new Choice("front", Strings.Get("P_Param_Target_Choice_Front")), new Choice("app", Strings.Get("P_Param_Target_Choice_App")), new Choice("input", Strings.Get("P_Param_Target_Choice_Input"))),
        App() with { VisibleWhen = new VisibleWhen("target", "app") },
        ParameterDescriptor.Content("window", Strings.Get("P_Param_Window_Label")) with { VisibleWhen = new VisibleWhen("target", "input"), IsInput = true },
    };
}

/// <summary>Reads "on / off / toggle" style choices.</summary>
internal static class Toggle
{
    /// <summary>Resolves a choice id to a final on/off, given the current state.</summary>
    public static bool Resolve(string choice, bool currentlyOn) => choice.Trim().ToLowerInvariant() switch
    {
        "on" or "true" or "yes" or "mute" or "lock" or "enable" => true,
        "off" or "false" or "no" or "unmute" or "disable" => false,
        _ => !currentlyOn,
    };
}
