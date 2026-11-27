namespace AutomationPortal.Actions.Windows.Devices;

public sealed record SettingsPage(string Id, string Label, string Uri);

/// <summary>The Windows Settings pages the Open Settings Page action can jump to.</summary>
public static class SettingsPages
{
    public static IReadOnlyList<SettingsPage> All { get; } = new SettingsPage[]
    {
        new("about", Strings.Get("SettingsPages_Choice_About"), "ms-settings:about"),
        new("accessibility", Strings.Get("SettingsPages_Choice_Accessibility"), "ms-settings:easeofaccess"),
        new("accounts", Strings.Get("SettingsPages_Choice_Accounts"), "ms-settings:yourinfo"),
        new("activation", Strings.Get("SettingsPages_Choice_Activation"), "ms-settings:activation"),
        new("airplane", Strings.Get("SettingsPages_Choice_Airplane"), "ms-settings:network-airplanemode"),
        new("apps", Strings.Get("SettingsPages_Choice_Apps"), "ms-settings:appsfeatures"),
        new("autoplay", Strings.Get("SettingsPages_Choice_Autoplay"), "ms-settings:autoplay"),
        new("background", Strings.Get("SettingsPages_Choice_Background"), "ms-settings:personalization-background"),
        new("battery", Strings.Get("SettingsPages_Choice_Battery"), "ms-settings:batterysaver"),
        new("bluetooth", Strings.Get("SettingsPages_Choice_Bluetooth"), "ms-settings:bluetooth"),
        new("camera", Strings.Get("SettingsPages_Choice_Camera"), "ms-settings:privacy-webcam"),
        new("clipboard", Strings.Get("SettingsPages_Choice_Clipboard"), "ms-settings:clipboard"),
        new("colors", Strings.Get("SettingsPages_Choice_Colors"), "ms-settings:personalization-colors"),
        new("dateandtime", Strings.Get("SettingsPages_Choice_Dateandtime"), "ms-settings:dateandtime"),
        new("defaultapps", Strings.Get("SettingsPages_Choice_Defaultapps"), "ms-settings:defaultapps"),
        new("developers", Strings.Get("SettingsPages_Choice_Developers"), "ms-settings:developers"),
        new("display", Strings.Get("SettingsPages_Choice_Display"), "ms-settings:display"),
        new("email", Strings.Get("SettingsPages_Choice_Email"), "ms-settings:emailandaccounts"),
        new("ethernet", Strings.Get("SettingsPages_Choice_Ethernet"), "ms-settings:network-ethernet"),
        new("focus", Strings.Get("SettingsPages_Choice_Focus"), "ms-settings:quiethours"),
        new("fonts", Strings.Get("SettingsPages_Choice_Fonts"), "ms-settings:fonts"),
        new("gamebar", Strings.Get("SettingsPages_Choice_Gamebar"), "ms-settings:gaming-gamebar"),
        new("gamemode", Strings.Get("SettingsPages_Choice_Gamemode"), "ms-settings:gaming-gamemode"),
        new("hdr", Strings.Get("SettingsPages_Choice_Hdr"), "ms-settings:display-advancedcolor"),
        new("hotspot", Strings.Get("SettingsPages_Choice_Hotspot"), "ms-settings:network-mobilehotspot"),
        new("language", Strings.Get("SettingsPages_Choice_Language"), "ms-settings:regionlanguage"),
        new("location", Strings.Get("SettingsPages_Choice_Location"), "ms-settings:privacy-location"),
        new("lockscreen", Strings.Get("SettingsPages_Choice_Lockscreen"), "ms-settings:lockscreen"),
        new("microphone", Strings.Get("SettingsPages_Choice_Microphone"), "ms-settings:privacy-microphone"),
        new("mouse", Strings.Get("SettingsPages_Choice_Mouse"), "ms-settings:mousetouchpad"),
        new("multitasking", Strings.Get("SettingsPages_Choice_Multitasking"), "ms-settings:multitasking"),
        new("narrator", Strings.Get("SettingsPages_Choice_Narrator"), "ms-settings:easeofaccess-narrator"),
        new("network", Strings.Get("SettingsPages_Choice_Network"), "ms-settings:network"),
        new("nightlight", Strings.Get("SettingsPages_Choice_Nightlight"), "ms-settings:nightlight"),
        new("notifications", Strings.Get("SettingsPages_Choice_Notifications"), "ms-settings:notifications"),
        new("optionalfeatures", Strings.Get("SettingsPages_Choice_Optionalfeatures"), "ms-settings:optionalfeatures"),
        new("personalization", Strings.Get("SettingsPages_Choice_Personalization"), "ms-settings:personalization"),
        new("phone", Strings.Get("SettingsPages_Choice_Phone"), "ms-settings:mobile-devices"),
        new("power", Strings.Get("SettingsPages_Choice_Power"), "ms-settings:powersleep"),
        new("printers", Strings.Get("SettingsPages_Choice_Printers"), "ms-settings:printers"),
        new("privacy", Strings.Get("SettingsPages_Choice_Privacy"), "ms-settings:privacy"),
        new("proxy", Strings.Get("SettingsPages_Choice_Proxy"), "ms-settings:network-proxy"),
        new("recovery", Strings.Get("SettingsPages_Choice_Recovery"), "ms-settings:recovery"),
        new("security", Strings.Get("SettingsPages_Choice_Security"), "windowsdefender:"),
        new("signin", Strings.Get("SettingsPages_Choice_Signin"), "ms-settings:signinoptions"),
        new("sound", Strings.Get("SettingsPages_Choice_Sound"), "ms-settings:sound"),
        new("speech", Strings.Get("SettingsPages_Choice_Speech"), "ms-settings:speech"),
        new("startmenu", Strings.Get("SettingsPages_Choice_Startmenu"), "ms-settings:personalization-start"),
        new("startup", Strings.Get("SettingsPages_Choice_Startup"), "ms-settings:startupapps"),
        new("storage", Strings.Get("SettingsPages_Choice_Storage"), "ms-settings:storagesense"),
        new("taskbar", Strings.Get("SettingsPages_Choice_Taskbar"), "ms-settings:taskbar"),
        new("textsize", Strings.Get("SettingsPages_Choice_Textsize"), "ms-settings:easeofaccess-display"),
        new("themes", Strings.Get("SettingsPages_Choice_Themes"), "ms-settings:themes"),
        new("touchpad", Strings.Get("SettingsPages_Choice_Touchpad"), "ms-settings:devices-touchpad"),
        new("troubleshoot", Strings.Get("SettingsPages_Choice_Troubleshoot"), "ms-settings:troubleshoot"),
        new("typing", Strings.Get("SettingsPages_Choice_Typing"), "ms-settings:typing"),
        new("update", Strings.Get("SettingsPages_Choice_Update"), "ms-settings:windowsupdate"),
        new("usb", Strings.Get("SettingsPages_Choice_Usb"), "ms-settings:usb"),
        new("vpn", Strings.Get("SettingsPages_Choice_Vpn"), "ms-settings:network-vpn"),
        new("wifi", Strings.Get("SettingsPages_Choice_Wifi"), "ms-settings:network-wifi"),
    };

    /// <summary>Finds a page by id or label, or accepts a raw "ms-settings:…" address. Null when unknown.</summary>
    public static string? ResolveUri(string idOrUri)
    {
        var key = idOrUri.Trim();
        if (key.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase)) return key;
        var page = All.FirstOrDefault(p => p.Id.Equals(key, StringComparison.OrdinalIgnoreCase) || p.Label.Equals(key, StringComparison.OrdinalIgnoreCase));
        return page?.Uri;
    }
}
