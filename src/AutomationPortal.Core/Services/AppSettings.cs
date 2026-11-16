namespace AutomationPortal.Core;

public enum ThemeSetting { System, Light, Dark }
/// <summary>How shortcut tiles look: a neutral card with a coloured icon chip, or the full-colour gradient.</summary>
public enum TileStyleSetting { Neutral, Gradients }
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
    public TileStyleSetting TileStyle { get; set; } = TileStyleSetting.Neutral;
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
    /// <summary>Ask once before a shortcut that changes files runs from the library. "Don't show again" turns it off.</summary>
    public bool AskBeforeFileChangingRuns { get; set; } = true;
    /// <summary>Shortcuts whose first run was already checked.</summary>
    public List<Guid> FileChangeCheckedShortcutIds { get; set; } = new();
    // Links
    [System.Text.Json.Serialization.JsonPropertyName("allowFlowmateLinks")] // key name kept so an existing setting still applies
    public bool AllowAutomationPortalLinks { get; set; }
    /// <summary>Caller ids of apps the user chose "Always allow" for, so they run shortcuts without being asked.</summary>
    public List<string> AllowedCallers { get; set; } = new();
    // Automations
    public bool AutomationsPaused { get; set; }
    /// <summary>When a timed pause ends. Null means paused until the user resumes.</summary>
    public DateTimeOffset? AutomationsPausedUntil { get; set; }
    // Ntfy topic etc. for "Send to My Phone"
    public string? NtfyTopic { get; set; }
}
