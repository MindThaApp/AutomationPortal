namespace AutomationPortal.Core;

public enum ThemeSetting { System, Light, Dark }
public enum AiModelTier { Fast, Smart, Best }

/// <summary>Persisted app settings (settings.json). The AI key lives in <see cref="ISecretStore"/> as "ai.apiKey", not here.</summary>
public sealed class AppSettings
{
    // General
    public bool StartWithWindows { get; set; }
    /// <summary>Set once the "turn Start with Windows on again" notice has been shown (the packaged build starts with it off).</summary>
    public bool StartWithWindowsNoticeShown { get; set; }
    /// <summary>Set once the first-run welcome has been shown.</summary>
    public bool WelcomeShown { get; set; }
    public bool KeepRunningInTray { get; set; } = true;
    public ThemeSetting Theme { get; set; } = ThemeSetting.System;
    /// <summary>Bumped when a default changes so <see cref="JsonSettingsStore"/> can move users who never changed the old default.</summary>
    public int SettingsVersion { get; set; }
    public const int CurrentVersion = 1;
    public const string OldQuickRunHotkey = "Ctrl+Alt+Space";
    public const string DefaultQuickRunHotkey = "Win+Alt+Space";
    // Keyboard
    public string QuickRunHotkey { get; set; } = DefaultQuickRunHotkey;
    /// <summary>Grabs the selected text in the front app, then opens Quick Run with it.</summary>
    public string RunOnSelectionHotkey { get; set; } = "Ctrl+Alt+R";
    /// <summary>Opens the Quick Panel at the mouse pointer.</summary>
    public string QuickPanelHotkey { get; set; } = "Ctrl+Alt+P";
    // Where I am (sunrise / sunset, weather alerts). Null = ask Windows, then guess from the internet address.
    public double? HomeLatitude { get; set; }
    public double? HomeLongitude { get; set; }
    public string? HomeCity { get; set; }
    // Library (null = default %APPDATA%\Automation Portal\Library)
    public string? LibraryFolder { get; set; }
    // Actions
    public bool ShowAdvancedActions { get; set; }
    public AiModelTier AiModel { get; set; } = AiModelTier.Best;
    /// <summary>Action ids the user starred in the action panel.</summary>
    public List<string> FavoriteActionIds { get; set; } = new();
    /// <summary>Most-recently-used action ids, newest first.</summary>
    public List<string> RecentActionIds { get; set; } = new();
    // Notifications
    public bool NotifyOnHotkeyRuns { get; set; } = true;
    // Links
    [System.Text.Json.Serialization.JsonPropertyName("allowFlowmateLinks")] // key name kept so an existing setting still applies
    public bool AllowAutomationPortalLinks { get; set; }
    // Automations
    public bool AutomationsPaused { get; set; }
    // Ntfy topic etc. for "Send to My Phone"
    public string? NtfyTopic { get; set; }
}
