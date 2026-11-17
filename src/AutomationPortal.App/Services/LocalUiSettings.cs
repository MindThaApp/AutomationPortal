using AutomationPortal.Core;

namespace AutomationPortal.App.Services;

public sealed class LocalUiSettingsData
{
    /// <summary>Opens the Quick Panel at the mouse pointer (integration binds the hotkey). Empty = off.</summary>
    public string? QuickPanelHotkey { get; set; }
    /// <summary>Grabs the selected text and opens Quick Run with it (integration binds the hotkey).</summary>
    public string? RunOnSelectionHotkey { get; set; }
    /// <summary>Most recently run shortcut ids from Quick Run, newest first.</summary>
    public List<Guid> QuickRunRecents { get; set; } = new();
    /// <summary>Where the main window was last left, in physical pixels (null = never saved, open centred).</summary>
    public SavedWindow? MainWindow { get; set; }
    /// <summary>The page that was open when the main window was last hidden or closed (an AppPage name).</summary>
    public string? LastPage { get; set; }
    public Guid? LastFolderId { get; set; }
    /// <summary>True once the "That was your first shortcut" line has been shown.</summary>
    public bool FirstRunOfferShown { get; set; }
    /// <summary>True once the "just ran by itself for the first time" notification has been shown.</summary>
    public bool FirstAutomationRunShown { get; set; }
    /// <summary>Plays a soft sound when a run the person started finishes. Off by default.</summary>
    public bool PlayRunSounds { get; set; }
    /// <summary>Store ids of the add-ons last seen as owned (Plus, Pro, the upgrade). Null until the Store has been read once.</summary>
    public List<string>? OwnedAddOns { get; set; }
}

public sealed class SavedWindow
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }
}

/// <summary>
/// App-only settings that Core's AppSettings doesn't have yet (see docs/core-requests/d1-app.md).
/// Stored in <c>app-ui.json</c> next to settings.json.
/// </summary>
public sealed class LocalUiSettings
{
    private readonly string _file;
    private readonly object _gate = new();
    private LocalUiSettingsData _data = new();

    public LocalUiSettings(AutomationPortalPaths paths)
    {
        _file = Path.Combine(paths.RoamingRoot, "app-ui.json");
        try { if (File.Exists(_file)) _data = AutomationPortalJson.Deserialize<LocalUiSettingsData>(File.ReadAllText(_file)) ?? new(); }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException) { }
    }

    /// <summary>One-time move of the hotkeys D1 kept here into Core's AppSettings.</summary>
    public void MigrateHotkeys(ISettingsStore settings)
    {
        var d = Current;
        if (d.QuickPanelHotkey is null && d.RunOnSelectionHotkey is null) return;
        var panel = d.QuickPanelHotkey; var selection = d.RunOnSelectionHotkey;
        settings.Update(s =>
        {
            if (panel is not null) s.QuickPanelHotkey = panel;
            if (selection is not null) s.RunOnSelectionHotkey = selection;
        });
        Update(x => { x.QuickPanelHotkey = null; x.RunOnSelectionHotkey = null; });
    }

    public event EventHandler? Changed;

    public LocalUiSettingsData Current { get { lock (_gate) return _data; } }

    public void Update(Action<LocalUiSettingsData> change)
    {
        lock (_gate)
        {
            change(_data);
            try { AtomicFile.WriteAllText(_file, AutomationPortalJson.Serialize(_data, indented: true)); }
            catch (IOException) { }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void PushRecent(Guid shortcutId)
    {
        Update(d =>
        {
            d.QuickRunRecents.Remove(shortcutId);
            d.QuickRunRecents.Insert(0, shortcutId);
            if (d.QuickRunRecents.Count > 20) d.QuickRunRecents.RemoveRange(20, d.QuickRunRecents.Count - 20);
        });
    }
}
