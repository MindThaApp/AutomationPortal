namespace Flowmate.Actions.Windows.Devices;

public sealed record SettingsPage(string Id, string Label, string Uri);

/// <summary>The Windows Settings pages the Open Settings Page action can jump to.</summary>
public static class SettingsPages
{
    public static IReadOnlyList<SettingsPage> All { get; } = new SettingsPage[]
    {
        new("about", "About this PC", "ms-settings:about"),
        new("accessibility", "Accessibility", "ms-settings:easeofaccess"),
        new("accounts", "Your account", "ms-settings:yourinfo"),
        new("activation", "Activation", "ms-settings:activation"),
        new("airplane", "Airplane mode", "ms-settings:network-airplanemode"),
        new("apps", "Installed apps", "ms-settings:appsfeatures"),
        new("autoplay", "AutoPlay", "ms-settings:autoplay"),
        new("background", "Background (wallpaper)", "ms-settings:personalization-background"),
        new("battery", "Battery saver", "ms-settings:batterysaver"),
        new("bluetooth", "Bluetooth & devices", "ms-settings:bluetooth"),
        new("camera", "Camera privacy", "ms-settings:privacy-webcam"),
        new("clipboard", "Clipboard", "ms-settings:clipboard"),
        new("colors", "Colors", "ms-settings:personalization-colors"),
        new("dateandtime", "Date & time", "ms-settings:dateandtime"),
        new("defaultapps", "Default apps", "ms-settings:defaultapps"),
        new("developers", "For developers", "ms-settings:developers"),
        new("display", "Display", "ms-settings:display"),
        new("email", "Email & accounts", "ms-settings:emailandaccounts"),
        new("ethernet", "Ethernet", "ms-settings:network-ethernet"),
        new("focus", "Focus", "ms-settings:quiethours"),
        new("fonts", "Fonts", "ms-settings:fonts"),
        new("gamebar", "Game Bar", "ms-settings:gaming-gamebar"),
        new("gamemode", "Game Mode", "ms-settings:gaming-gamemode"),
        new("hdr", "HDR & color", "ms-settings:display-advancedcolor"),
        new("hotspot", "Mobile hotspot", "ms-settings:network-mobilehotspot"),
        new("language", "Language & region", "ms-settings:regionlanguage"),
        new("location", "Location privacy", "ms-settings:privacy-location"),
        new("lockscreen", "Lock screen", "ms-settings:lockscreen"),
        new("microphone", "Microphone privacy", "ms-settings:privacy-microphone"),
        new("mouse", "Mouse", "ms-settings:mousetouchpad"),
        new("multitasking", "Multitasking (snap windows)", "ms-settings:multitasking"),
        new("narrator", "Narrator", "ms-settings:easeofaccess-narrator"),
        new("network", "Network & internet", "ms-settings:network"),
        new("nightlight", "Night light", "ms-settings:nightlight"),
        new("notifications", "Notifications", "ms-settings:notifications"),
        new("optionalfeatures", "Optional features", "ms-settings:optionalfeatures"),
        new("personalization", "Personalization", "ms-settings:personalization"),
        new("phone", "Mobile devices (phone link)", "ms-settings:mobile-devices"),
        new("power", "Power & sleep", "ms-settings:powersleep"),
        new("printers", "Printers & scanners", "ms-settings:printers"),
        new("privacy", "Privacy & security", "ms-settings:privacy"),
        new("proxy", "Proxy", "ms-settings:network-proxy"),
        new("recovery", "Recovery", "ms-settings:recovery"),
        new("security", "Windows Security", "windowsdefender:"),
        new("signin", "Sign-in options", "ms-settings:signinoptions"),
        new("sound", "Sound", "ms-settings:sound"),
        new("speech", "Speech", "ms-settings:speech"),
        new("startmenu", "Start menu", "ms-settings:personalization-start"),
        new("startup", "Startup apps", "ms-settings:startupapps"),
        new("storage", "Storage", "ms-settings:storagesense"),
        new("taskbar", "Taskbar", "ms-settings:taskbar"),
        new("textsize", "Text size", "ms-settings:easeofaccess-display"),
        new("themes", "Themes", "ms-settings:themes"),
        new("touchpad", "Touchpad", "ms-settings:devices-touchpad"),
        new("troubleshoot", "Troubleshoot", "ms-settings:troubleshoot"),
        new("typing", "Typing", "ms-settings:typing"),
        new("update", "Windows Update", "ms-settings:windowsupdate"),
        new("usb", "USB", "ms-settings:usb"),
        new("vpn", "VPN", "ms-settings:network-vpn"),
        new("wifi", "Wi-Fi", "ms-settings:network-wifi"),
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
