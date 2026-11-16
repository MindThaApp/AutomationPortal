namespace AutomationPortal.App.Logic;

public enum SettingsSection { General, Keyboard, LibraryAndBackup, Notifications, AppsAndLinks, Models, Advanced, About }

/// <summary>One thing on the Settings page: its card title (or, for lists the page fills in, the name of its host panel) and the section it sits in.</summary>
public sealed record SettingsItem(string Name, SettingsSection Section);

/// <summary>A section of the Settings page: the heading and the name of the heading element the jump list scrolls to.</summary>
public sealed record SettingsSectionInfo(SettingsSection Section, string Title, string Anchor);

/// <summary>How Settings is grouped. The jump list at the top of the page and the order of the sections come from here.</summary>
public static class SettingsLayout
{
    public static IReadOnlyList<SettingsSectionInfo> Sections { get; } = new[]
    {
        new SettingsSectionInfo(SettingsSection.General, "General", "SecGeneral"),
        new SettingsSectionInfo(SettingsSection.Keyboard, "Keyboard", "SecKeyboard"),
        new SettingsSectionInfo(SettingsSection.LibraryAndBackup, "Library and backup", "SecLibrary"),
        new SettingsSectionInfo(SettingsSection.Notifications, "Notifications", "SecNotifications"),
        new SettingsSectionInfo(SettingsSection.AppsAndLinks, "Other apps and links", "SecApps"),
        new SettingsSectionInfo(SettingsSection.Models, "Models", "AiSection"),
        new SettingsSectionInfo(SettingsSection.Advanced, "Advanced", "SecAdvanced"),
        new SettingsSectionInfo(SettingsSection.About, "About", "SecAbout"),
    };

    public static IReadOnlyList<SettingsItem> Items { get; } = new[]
    {
        new SettingsItem("Start with Windows", SettingsSection.General),
        new SettingsItem("Keep running in the tray", SettingsSection.General),
        new SettingsItem("Theme", SettingsSection.General),
        new SettingsItem("Shortcut tiles", SettingsSection.General),
        new SettingsItem("Where I am", SettingsSection.General),
        new SettingsItem("Quick Run", SettingsSection.Keyboard),
        new SettingsItem("Run on selected text", SettingsSection.Keyboard),
        new SettingsItem("Quick Panel at the mouse pointer", SettingsSection.Keyboard),
        new SettingsItem("Where your shortcuts are saved", SettingsSection.LibraryAndBackup),
        new SettingsItem("Keep your shortcuts in OneDrive", SettingsSection.LibraryAndBackup),
        new SettingsItem("Back up your library", SettingsSection.LibraryAndBackup),
        new SettingsItem("Tell me when a shortcut started by hotkey finishes", SettingsSection.Notifications),
        new SettingsItem("Phone notifications topic", SettingsSection.Notifications),
        new SettingsItem("automationportal:// links", SettingsSection.AppsAndLinks),
        new SettingsItem("ConnectedHost", SettingsSection.AppsAndLinks),
        new SettingsItem("AllowedCallersHost", SettingsSection.AppsAndLinks),
        new SettingsItem("AiHost", SettingsSection.Models),
        new SettingsItem("Model level: Fast, Smart or Best", SettingsSection.Models),
        new SettingsItem("Show advanced actions", SettingsSection.Advanced),
        new SettingsItem("VersionText", SettingsSection.About),
    };

    public static SettingsSectionInfo Info(SettingsSection section) => Sections.Single(s => s.Section == section);

    /// <summary>The section a setting lives in, or null when it isn't one.</summary>
    public static SettingsSection? SectionOf(string name) => Items.FirstOrDefault(i => i.Name == name)?.Section;

    public static IEnumerable<SettingsItem> In(SettingsSection section) => Items.Where(i => i.Section == section);
}
