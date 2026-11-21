namespace AutomationPortal.App.Logic;

public enum SettingsSection { Appearance, General, Keyboard, LibraryAndBackup, Notifications, AppsAndLinks, Models, Advanced, About }

/// <summary>One thing on the Settings page: its card title (or, for lists the page fills in, the name of its host panel) and the section it sits in.</summary>
public sealed record SettingsItem(string Name, SettingsSection Section);

/// <summary>A section of the Settings page: the heading and the name of the heading element the jump list scrolls to.</summary>
public sealed record SettingsSectionInfo(SettingsSection Section, string Title, string Anchor);

/// <summary>How Settings is grouped. The jump list at the top of the page and the order of the sections come from here.</summary>
public static class SettingsLayout
{
    public static IReadOnlyList<SettingsSectionInfo> Sections { get; } = new[]
    {
        new SettingsSectionInfo(SettingsSection.Appearance, Strings.Get("SettingsPage_Appearance"), "SecAppearance"),
        new SettingsSectionInfo(SettingsSection.General, "General", "SecGeneral"),
        new SettingsSectionInfo(SettingsSection.Keyboard, "Keyboard", "SecKeyboard"),
        new SettingsSectionInfo(SettingsSection.LibraryAndBackup, Strings.Get("SettingsLayout_LibraryAndBackup"), "SecLibrary"),
        new SettingsSectionInfo(SettingsSection.Notifications, "Notifications", "SecNotifications"),
        new SettingsSectionInfo(SettingsSection.AppsAndLinks, Strings.Get("SettingsLayout_OtherAppsAndLinks"), "SecApps"),
        new SettingsSectionInfo(SettingsSection.Models, "Models", "AiSection"),
        new SettingsSectionInfo(SettingsSection.Advanced, "Advanced", "SecAdvanced"),
        new SettingsSectionInfo(SettingsSection.About, "About", "SecAbout"),
    };

    public static IReadOnlyList<SettingsItem> Items { get; } = new[]
    {
        new SettingsItem("Theme", SettingsSection.Appearance),
        new SettingsItem(Strings.Get("SettingsPage_AccentColour"), SettingsSection.Appearance),
        new SettingsItem(Strings.Get("SettingsPage_WindowBackground"), SettingsSection.Appearance),
        new SettingsItem(Strings.Get("SettingsPage_Corners"), SettingsSection.Appearance),
        new SettingsItem(Strings.Get("SettingsPage_Density"), SettingsSection.Appearance),
        new SettingsItem(Strings.Get("SettingsPage_TextSize"), SettingsSection.Appearance),
        new SettingsItem(Strings.Get("SettingsPage_Motion"), SettingsSection.Appearance),
        new SettingsItem(Strings.Get("SettingsPage_TileLook"), SettingsSection.Appearance),
        new SettingsItem(Strings.Get("SettingsPage_ResetAppearance"), SettingsSection.Appearance),
        new SettingsItem(Strings.Get("WelcomeDialog_StartWithWindows"), SettingsSection.General),
        new SettingsItem(Strings.Get("SettingsLayout_KeepRunningInThe"), SettingsSection.General),
        new SettingsItem(Strings.Get("SettingsLayout_WhereIAm"), SettingsSection.General),
        new SettingsItem(Strings.Get("AppHost_QuickRun"), SettingsSection.Keyboard),
        new SettingsItem(Strings.Get("AppHost_RunOnSelectedText"), SettingsSection.Keyboard),
        new SettingsItem(Strings.Get("SettingsLayout_QuickPanelAtThe"), SettingsSection.Keyboard),
        new SettingsItem(Strings.Get("SettingsLayout_WhereYourShortcutsAre"), SettingsSection.LibraryAndBackup),
        new SettingsItem(Strings.Get("SettingsLayout_KeepYourShortcutsIn"), SettingsSection.LibraryAndBackup),
        new SettingsItem(Strings.Get("SettingsLayout_BackUpYourLibrary"), SettingsSection.LibraryAndBackup),
        new SettingsItem(Strings.Get("SettingsLayout_TellMeWhenA"), SettingsSection.Notifications),
        new SettingsItem(Strings.Get("SettingsLayout_PlayASoundWhen"), SettingsSection.Notifications),
        new SettingsItem(Strings.Get("SettingsLayout_PhoneNotificationsTopic"), SettingsSection.Notifications),
        new SettingsItem("automationportal:// links", SettingsSection.AppsAndLinks),
        new SettingsItem("ConnectedHost", SettingsSection.AppsAndLinks),
        new SettingsItem("AllowedCallersHost", SettingsSection.AppsAndLinks),
        new SettingsItem("AiHost", SettingsSection.Models),
        new SettingsItem("Model level: Fast, Smart or Best", SettingsSection.Models),
        new SettingsItem(Strings.Get("SettingsLayout_ShowAdvancedActions"), SettingsSection.Advanced),
        new SettingsItem("Send crash reports to help fix problems", SettingsSection.Advanced),
        new SettingsItem("VersionText", SettingsSection.About),
    };

    public static SettingsSectionInfo Info(SettingsSection section) => Sections.Single(s => s.Section == section);

    /// <summary>The section a setting lives in, or null when it isn't one.</summary>
    public static SettingsSection? SectionOf(string name) => Items.FirstOrDefault(i => i.Name == name)?.Section;

    public static IEnumerable<SettingsItem> In(SettingsSection section) => Items.Where(i => i.Section == section);
}

