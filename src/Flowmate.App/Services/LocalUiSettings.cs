using Flowmate.Core;

namespace Flowmate.App.Services;

public sealed class LocalUiSettingsData
{
    /// <summary>Opens the Quick Panel at the mouse pointer (integration binds the hotkey). Empty = off.</summary>
    public string QuickPanelHotkey { get; set; } = "Ctrl+Alt+P";
    /// <summary>Grabs the selected text and opens Quick Run with it (integration binds the hotkey).</summary>
    public string RunOnSelectionHotkey { get; set; } = "Ctrl+Alt+R";
    /// <summary>Most recently run shortcut ids from Quick Run, newest first.</summary>
    public List<Guid> QuickRunRecents { get; set; } = new();
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

    public LocalUiSettings(FlowmatePaths paths)
    {
        _file = Path.Combine(paths.RoamingRoot, "app-ui.json");
        try { if (File.Exists(_file)) _data = FlowmateJson.Deserialize<LocalUiSettingsData>(File.ReadAllText(_file)) ?? new(); }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException) { }
    }

    public event EventHandler? Changed;

    public LocalUiSettingsData Current { get { lock (_gate) return _data; } }

    public void Update(Action<LocalUiSettingsData> change)
    {
        lock (_gate)
        {
            change(_data);
            try { AtomicFile.WriteAllText(_file, FlowmateJson.Serialize(_data, indented: true)); }
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
