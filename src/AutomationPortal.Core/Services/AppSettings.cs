using System.Text.Json.Serialization;

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
    /// <summary>The newest Store version whose "update ready" notice the user chose Later for.</summary>
    public string? DismissedUpdateVersion { get; set; }
    /// <summary>Set once the first-run welcome has been shown.</summary>
    public bool WelcomeShown { get; set; }
    /// <summary>The newest app version whose What's new window was shown, or passed over on a fresh install. Null means never recorded.</summary>
    public string? WhatsNewSeenVersion { get; set; }
    public bool KeepRunningInTray { get; set; } = true;
    /// <summary>
    /// What the user chose for crash reports (first-run welcome or Settings). Null = never chose, which counts as on.
    /// The old "SendCrashReports" key is deliberately not read: it was opt-in and written as false for everyone, so a stored false never meant a choice,
    /// and the build that wrote it was never released.
    /// </summary>
    public bool? CrashReportsChoice { get; set; }
    /// <summary>Crash reports are on unless the user turned them off.</summary>
    [JsonIgnore]
    public bool SendCrashReports { get => CrashReportsChoice ?? true; set => CrashReportsChoice = value; }
    public ThemeSetting Theme { get; set; } = ThemeSetting.System;
    public TileStyleSetting TileStyle { get; set; } = TileStyleSetting.Neutral;
    // Appearance: every default equals the look the app had before these settings existed.
    [JsonConverter(typeof(TolerantEnumConverter<AccentMode>))]
    public AccentMode AccentMode { get; set; }
    /// <summary>The category colour (Blue, Indigo ... Slate) used when <see cref="AccentMode"/> is Preset.</summary>
    public string AccentPreset { get; set; } = "Blue";
    [JsonConverter(typeof(TolerantEnumConverter<MaterialSetting>))]
    public MaterialSetting Material { get; set; }
    [JsonConverter(typeof(TolerantEnumConverter<CornerStyle>))]
    public CornerStyle CornerStyle { get; set; }
    [JsonConverter(typeof(TolerantEnumConverter<Density>))]
    public Density Density { get; set; }
    /// <summary>Extra text size in percent on top of Windows text scaling (90 to 150).</summary>
    public int TextScale { get; set; } = AppearanceMath.DefaultTextScale;
    [JsonConverter(typeof(TolerantEnumConverter<MotionSetting>))]
    public MotionSetting MotionMode { get; set; }
    [JsonConverter(typeof(TolerantEnumConverter<TileLook>))]
    public TileLook TileLook { get; set; }

    /// <summary>Puts every Appearance setting (theme included) back to its default. Nothing else changes.</summary>
    public void ResetAppearance()
    {
        Theme = ThemeSetting.System;
        AccentMode = AccentMode.Windows;
        AccentPreset = "Blue";
        Material = MaterialSetting.Mica;
        CornerStyle = CornerStyle.Rounded;
        Density = Density.Standard;
        TextScale = AppearanceMath.DefaultTextScale;
        MotionMode = MotionSetting.FollowWindows;
        TileLook = TileLook.Cards;
    }

    /// <summary>The "Start here" row on the library has been hidden.</summary>
    public bool HideStartHere { get; set; }
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
    [System.Text.Json.Serialization.JsonPropertyName("allowFlowmateLinks")] // key name kept so an existing setting still reads
    public bool AllowAutomationPortalLinks { get; set; }
    /// <summary>What automationportal:// links may do. Replaces <see cref="AllowAutomationPortalLinks"/>, which ran every shortcut without asking.</summary>
    public LinkMode LinksMode { get; set; } = LinkMode.PerShortcut;
    /// <summary>Shortcuts whose Details say "Links can run this without asking" (used when <see cref="LinksMode"/> is Per shortcut).</summary>
    public List<Guid> LinkTrustedShortcutIds { get; set; } = new();
    /// <summary>What links may do for one shortcut (key: shortcut id, "D" format). A shortcut with no entry follows <see cref="LinksMode"/>. Kept here, not in the shortcut file, so an imported shortcut never brings it along.</summary>
    public Dictionary<string, LinkMode> ShortcutLinkModes { get; set; } = new();
    /// <summary>Caller ids of apps the user chose "Always allow" for, so they run shortcuts without being asked.</summary>
    public List<string> AllowedCallers { get; set; } = new();
    /// <summary>Caller and shortcut pairs ("filesportal/&lt;shortcut id&gt;") the user ticked "Don't ask again" for.</summary>
    public List<string> AllowedCallerShortcuts { get; set; } = new();
    /// <summary>
    /// For each trusted shortcut ("&lt;shortcut id&gt;" for links, "caller/&lt;shortcut id&gt;" for callers), a hash of its steps when it was
    /// trusted. When the steps change, the trust no longer applies and the user is asked again.
    /// </summary>
    public Dictionary<string, string> TrustedStepHashes { get; set; } = new();
    // Automations
    public bool AutomationsPaused { get; set; }
    /// <summary>When a timed pause ends. Null means paused until the user resumes.</summary>
    public DateTimeOffset? AutomationsPausedUntil { get; set; }
    // Ntfy topic etc. for "Send to My Phone"
    public string? NtfyTopic { get; set; }
}

