using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

/// <summary>The harmless shortcut the welcome runs for "Try one now". It shows a notification and touches no files, apps or settings.</summary>
public static class SampleShortcut
{
    public static readonly Guid SampleId = new("5a1e0c6e-3f0b-4d57-9a53-6a1f5c2e8d11");

    public const string NotificationText = "It works. A shortcut is a small job Automation Portal does for you with one click.";

    public static Shortcut Create()
    {
        var b = new ShortcutBuilder(Strings.Get("WelcomeDialog_SayHello"), "Sparkle24", "blue");
        b.Shortcut.Id = SampleId;
        b.Shortcut.Description = Strings.Get("SampleShortcut_ShowsANotificationChanges");
        b.Add("ui.notify", ("title", ParamValue.Text(Strings.Get("App_AutomationPortal"))), ("body", ParamValue.Text(NotificationText)), ("wait", ParamValue.Bool(false)));
        return b.Build();
    }
}
