using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Flowmate.App.Dialogs;
using Flowmate.Core;
using Microsoft.Win32;

namespace Flowmate.App.Pages;

public partial class SettingsPage : UserControl, IAppPage
{
    private const string AiKeyName = "ai.apiKey";
    private readonly AppHost _host;
    private bool _loading = true;
    private CancellationTokenSource? _testCts;

    public SettingsPage(AppHost host)
    {
        _host = host;
        InitializeComponent();
        _host.Settings.Changed += (_, _) => Dispatcher.BeginInvoke(Load);
        _host.UiSettings.Changed += (_, _) => Dispatcher.BeginInvoke(Load);
        Load();
    }

    public void Navigated(NavTarget target) => Load();

    private Window? Owner => Window.GetWindow(this);

    private void Load()
    {
        _loading = true;
        var s = _host.Settings.Current;
        var u = _host.UiSettings.Current;
        StartToggle.IsChecked = s.StartWithWindows;
        TrayToggle.IsChecked = s.KeepRunningInTray;
        ThemeBox.SelectedIndex = s.Theme switch { ThemeSetting.Light => 1, ThemeSetting.Dark => 2, _ => 0 };
        QuickRunKey.Value = s.QuickRunHotkey;
        SelectionKey.Value = u.RunOnSelectionHotkey;
        PanelKey.Value = u.QuickPanelHotkey;
        LibraryPathText.Text = _host.Paths.LibraryFolder;
        AdvancedToggle.IsChecked = s.ShowAdvancedActions;
        if (TopicBox.Text.Trim() != (s.NtfyTopic ?? "")) TopicBox.Text = s.NtfyTopic ?? "";
        NotifyToggle.IsChecked = s.NotifyOnHotkeyRuns;
        LinksToggle.IsChecked = s.AllowFlowmateLinks;
        ModelFast.IsChecked = s.AiModel == AiModelTier.Fast;
        ModelSmart.IsChecked = s.AiModel == AiModelTier.Smart;
        ModelBest.IsChecked = s.AiModel == AiModelTier.Best;
        ModelDescription.Text = ModelText(s.AiModel);
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        VersionText.Text = "Version " + (version is null ? "1.0" : $"{version.Major}.{version.Minor}.{version.Build}");
        RefreshKeyState();
        _loading = false;
    }

    private static string ModelText(AiModelTier t) => t switch
    {
        AiModelTier.Fast => "Fast: quick answers at the lowest cost.",
        AiModelTier.Best => "Best: the most capable answers, but slower and more costly.",
        _ => "Smart: a good balance of quality and speed. Recommended.",
    };

    // ---- general ---------------------------------------------------------------------------

    private void OnStartToggle(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var on = StartToggle.IsChecked == true;
        try { _host.Shell.SetStartWithWindows(on); }
        catch (Exception ex) { _ = Prompts.MessageAsync(Owner, "Couldn't change that", ex.Message); }
        _host.Settings.Update(s => s.StartWithWindows = on);
    }

    private void OnTrayToggle(object sender, RoutedEventArgs e) { if (!_loading) _host.Settings.Update(s => s.KeepRunningInTray = TrayToggle.IsChecked == true); }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeBox.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        var theme = Enum.Parse<ThemeSetting>(tag);
        _host.Settings.Update(s => s.Theme = theme);   // Settings.Changed re-applies the theme live
    }

    // ---- keyboard --------------------------------------------------------------------------

    private void OnQuickRunKey(object? sender, EventArgs e) { if (!_loading) _host.Settings.Update(s => s.QuickRunHotkey = QuickRunKey.Value); }
    private void OnSelectionKey(object? sender, EventArgs e) { if (!_loading) _host.UiSettings.Update(u => u.RunOnSelectionHotkey = SelectionKey.Value); }
    private void OnPanelKey(object? sender, EventArgs e) { if (!_loading) _host.UiSettings.Update(u => u.QuickPanelHotkey = PanelKey.Value); }

    // ---- library ---------------------------------------------------------------------------

    private void OnOpenLibrary(object sender, RoutedEventArgs e) => OpenFolder(_host.Paths.LibraryFolder);

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception) { }
    }

    private async void OnChangeLibrary(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose where to keep your shortcuts" };
        if (dlg.ShowDialog(Owner) != true) return;
        await ChangeLibraryAsync(dlg.FolderName);
    }

    private async void OnMoveToOneDrive(object sender, RoutedEventArgs e)
    {
        var oneDrive = Environment.GetEnvironmentVariable("OneDrive") ?? Environment.GetEnvironmentVariable("OneDriveConsumer");
        if (string.IsNullOrWhiteSpace(oneDrive) || !Directory.Exists(oneDrive))
        {
            await Prompts.MessageAsync(Owner, "OneDrive wasn't found", "Flowmate couldn't find a OneDrive folder on this PC. Sign in to OneDrive first, or use Change… to pick any folder.");
            return;
        }
        await ChangeLibraryAsync(Path.Combine(oneDrive, "Flowmate Library"));
    }

    private async Task ChangeLibraryAsync(string target)
    {
        var current = Path.GetFullPath(_host.Paths.LibraryFolder).TrimEnd('\\');
        var next = Path.GetFullPath(target).TrimEnd('\\');
        if (string.Equals(current, next, StringComparison.OrdinalIgnoreCase)) return;
        var targetHasLibrary = Directory.Exists(Path.Combine(next, "shortcuts"));
        var message = targetHasLibrary
            ? $"Flowmate will use the shortcuts already in\n{next}\n\nYour current shortcuts stay where they are."
            : $"Flowmate will copy your shortcuts to\n{next}\n\nand use that folder from now on. Your current copy stays where it is as a backup.";
        if (!await Prompts.ConfirmAsync(Owner, "Change library location", message, "Continue")) return;
        try
        {
            if (!targetHasLibrary && Directory.Exists(current)) CopyDirectory(current, next);
            else Directory.CreateDirectory(next);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Prompts.MessageAsync(Owner, "Couldn't copy your shortcuts", $"{ex.Message}\n\nNothing was changed.");
            return;
        }
        _host.Settings.Update(s => s.LibraryFolder = next);
        await Prompts.MessageAsync(Owner, "Almost done", "Your shortcuts are ready in the new place. Close Flowmate from the tray icon and open it again to start using it.");
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: true);
    }

    // ---- actions / AI ----------------------------------------------------------------------

    private void OnAdvancedToggle(object sender, RoutedEventArgs e) { if (!_loading) _host.Settings.Update(s => s.ShowAdvancedActions = AdvancedToggle.IsChecked == true); }

    private void OnModelChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string tag }) return;
        var tier = Enum.Parse<AiModelTier>(tag);
        _host.Settings.Update(s => s.AiModel = tier);
    }

    private void OnTopicChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        var text = TopicBox.Text.Trim();
        _host.Settings.Update(s => s.NtfyTopic = text.Length == 0 ? null : text);
    }

    private bool HasSavedKey => !string.IsNullOrEmpty(_host.Secrets.Get(AiKeyName));

    private void RefreshKeyState()
    {
        var saved = HasSavedKey;
        RemoveKeyButton.IsEnabled = saved;
        TestKeyButton.IsEnabled = saved || KeyBox.Password.Length > 0;
        SaveKeyButton.IsEnabled = KeyBox.Password.Length > 0;
        if (KeyBox.Password.Length == 0 && KeyStatus.Tag is null)
            KeyStatus.Text = saved ? "A key is saved. Paste a new one to replace it." : "No key yet. Paste one above and press Save.";
    }

    private void OnKeyChanged(object sender, RoutedEventArgs e)
    {
        KeyStatus.Tag = null;
        RefreshKeyState();
    }

    private void OnSaveKey(object sender, RoutedEventArgs e)
    {
        var key = KeyBox.Password.Trim();
        if (key.Length == 0) return;
        try { _host.Secrets.Set(AiKeyName, key); }
        catch (Exception ex) { KeyStatus.Text = "Couldn't save the key. " + ex.Message; return; }
        KeyBox.Password = "";
        KeyStatus.Tag = "saved";
        KeyStatus.Text = "Key saved.";
        RefreshKeyState();
    }

    private void OnRemoveKey(object sender, RoutedEventArgs e)
    {
        _host.Secrets.Remove(AiKeyName);
        KeyBox.Password = "";
        KeyStatus.Tag = null;
        RefreshKeyState();
        KeyStatus.Text = "The key was removed.";
        KeyStatus.Tag = "removed";
    }

    private async void OnTestKey(object sender, RoutedEventArgs e)
    {
        var key = KeyBox.Password.Trim();
        if (key.Length == 0) key = _host.Secrets.Get(AiKeyName) ?? "";
        _testCts?.Cancel();
        var cts = _testCts = new CancellationTokenSource();
        KeyBusy.Visibility = Visibility.Visible;
        KeyStatus.Tag = "test";
        KeyStatus.Text = "Checking your key…";
        try
        {
            var result = await _host.AiKeyTester.TestAsync(key, _host.Settings.Current.AiModel, cts.Token);
            if (cts.IsCancellationRequested) return;
            KeyStatus.Text = (result.Ok ? "Your key works. " : "") + result.Message;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { KeyStatus.Text = "Couldn't check the key. " + ex.Message; }
        finally { KeyBusy.Visibility = Visibility.Collapsed; }
    }

    // ---- notifications / links / about ----------------------------------------------------

    private void OnNotifyToggle(object sender, RoutedEventArgs e) { if (!_loading) _host.Settings.Update(s => s.NotifyOnHotkeyRuns = NotifyToggle.IsChecked == true); }
    private void OnLinksToggle(object sender, RoutedEventArgs e) { if (!_loading) _host.Settings.Update(s => s.AllowFlowmateLinks = LinksToggle.IsChecked == true); }
    private void OnOpenData(object sender, RoutedEventArgs e) => OpenFolder(_host.Paths.RoamingRoot);
    private void OnOpenLogs(object sender, RoutedEventArgs e) => OpenFolder(_host.Paths.LogsFolder);
}
