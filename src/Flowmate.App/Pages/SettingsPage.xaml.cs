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
    private readonly AppHost _host;
    private bool _loading = true;

    public SettingsPage(AppHost host)
    {
        _host = host;
        InitializeComponent();
        _host.Settings.Changed += (_, _) => Dispatcher.BeginInvoke(Load);
        _host.HotkeyProblemsChanged += (_, _) => Dispatcher.BeginInvoke(ShowHotkeyProblems);
        AiHost.Children.Add(new AiSettingsPanel(host));
        Load();
    }

    public void Navigated(NavTarget target)
    {
        Load();
        if (target.Item == "ai") AiSection.BringIntoView();
    }

    private void ShowHotkeyProblems()
    {
        void Show(System.Windows.Controls.TextBlock box, string which)
        {
            var p = _host.HotkeyProblem(which);
            box.Text = p ?? "";
            box.Visibility = p is null ? Visibility.Collapsed : Visibility.Visible;
        }
        Show(QuickRunKeyProblem, "quickrun"); Show(SelectionKeyProblem, "selection"); Show(PanelKeyProblem, "panel");
    }

    private void OnHomeChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        static double? Num(string t) => double.TryParse(t.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) || double.TryParse(t.Trim(), out d) ? d : null;
        var lat = Num(HomeLatBox.Text); var lon = Num(HomeLonBox.Text);
        var city = HomeCityBox.Text.Trim();
        var both = lat is >= -90 and <= 90 && lon is >= -180 and <= 180;
        _host.Settings.Update(x => { x.HomeCity = city.Length == 0 ? null : city; x.HomeLatitude = both ? lat : null; x.HomeLongitude = both ? lon : null; });
    }

    private Window? Owner => Window.GetWindow(this);

    private void Load()
    {
        _loading = true;
        var s = _host.Settings.Current;
        StartToggle.IsChecked = s.StartWithWindows;
        TrayToggle.IsChecked = s.KeepRunningInTray;
        ThemeBox.SelectedIndex = s.Theme switch { ThemeSetting.Light => 1, ThemeSetting.Dark => 2, _ => 0 };
        QuickRunKey.Value = s.QuickRunHotkey;
        SelectionKey.Value = s.RunOnSelectionHotkey;
        PanelKey.Value = s.QuickPanelHotkey;
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
        VersionText.Text = "Version " + (version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}");
        ShowHotkeyProblems();
        if (HomeCityBox.Text != (s.HomeCity ?? "")) HomeCityBox.Text = s.HomeCity ?? "";
        HomeLatBox.Text = s.HomeLatitude?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        HomeLonBox.Text = s.HomeLongitude?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
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
    private void OnSelectionKey(object? sender, EventArgs e) { if (!_loading) _host.Settings.Update(x => x.RunOnSelectionHotkey = SelectionKey.Value); }
    private void OnPanelKey(object? sender, EventArgs e) { if (!_loading) _host.Settings.Update(x => x.QuickPanelHotkey = PanelKey.Value); }

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

    // ---- notifications / links / about ----------------------------------------------------

    private void OnNotifyToggle(object sender, RoutedEventArgs e) { if (!_loading) _host.Settings.Update(s => s.NotifyOnHotkeyRuns = NotifyToggle.IsChecked == true); }
    private void OnLinksToggle(object sender, RoutedEventArgs e) { if (!_loading) _host.Settings.Update(s => s.AllowFlowmateLinks = LinksToggle.IsChecked == true); }
    private void OnOpenData(object sender, RoutedEventArgs e) => OpenFolder(_host.Paths.RoamingRoot);
    private void OnOpenLogs(object sender, RoutedEventArgs e) => OpenFolder(_host.Paths.LogsFolder);
}
