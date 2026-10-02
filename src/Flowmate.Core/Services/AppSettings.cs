namespace Flowmate.Core;

public enum ThemeSetting { System, Light, Dark }
public enum AiModelTier { Fast, Smart, Best }

/// <summary>Persisted app settings (settings.json). The AI key lives in <see cref="ISecretStore"/> as "ai.apiKey", not here.</summary>
public sealed class AppSettings
{
    // General
    public bool StartWithWindows { get; set; }
    public bool KeepRunningInTray { get; set; } = true;
    public ThemeSetting Theme { get; set; } = ThemeSetting.System;
    // Keyboard
    public string QuickRunHotkey { get; set; } = "Ctrl+Alt+Space";
    // Library (null = default %APPDATA%\Flowmate\Library)
    public string? LibraryFolder { get; set; }
    // Actions
    public bool ShowAdvancedActions { get; set; }
    public AiModelTier AiModel { get; set; } = AiModelTier.Smart;
    /// <summary>Action ids the user starred in the action panel.</summary>
    public List<string> FavoriteActionIds { get; set; } = new();
    /// <summary>Most-recently-used action ids, newest first.</summary>
    public List<string> RecentActionIds { get; set; } = new();
    // Notifications
    public bool NotifyOnHotkeyRuns { get; set; } = true;
    // Links
    public bool AllowFlowmateLinks { get; set; }
    // Automations
    public bool AutomationsPaused { get; set; }
    // Ntfy topic etc. for "Send to My Phone"
    public string? NtfyTopic { get; set; }
}
