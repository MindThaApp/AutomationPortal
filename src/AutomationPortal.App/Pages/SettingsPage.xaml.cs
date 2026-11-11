using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using AutomationPortal.App.Dialogs;
using AutomationPortal.Core;
using Microsoft.Win32;

namespace AutomationPortal.App.Pages;

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
        LinksToggle.IsChecked = s.AllowAutomationPortalLinks;
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

    private async void OnBackUpLibrary(object sender, RoutedEventArgs e)
    {
        var initial = LibraryBackup.DefaultFolder;
        // The folder picker can only show a folder that exists, so the default one is made first (empty until a backup is saved).
        try { Directory.CreateDirectory(initial); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { initial = Path.GetDirectoryName(initial)!; }
        var dlg = new OpenFolderDialog { Title = "Choose where to save the backup", InitialDirectory = initial, FolderName = initial };
        if (dlg.ShowDialog(Owner) != true) return;
        try
        {
            var zip = LibraryBackup.Create(_host.Paths, dlg.FolderName, DateTime.Now);
            await Prompts.MessageAsync(Owner, "Library backed up", $"Your library was saved to:\n{zip}\n\nTo bring a shortcut back later, open the zip and import its file from the \"{LibraryBackup.ImportEntryFolder}\" folder.");
        }
        catch (LibraryException ex) { await Prompts.MessageAsync(Owner, "Couldn't back up your library", ex.Message); }
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
            await Prompts.MessageAsync(Owner, "OneDrive wasn't found", "Automation Portal couldn't find a OneDrive folder on this PC. Sign in to OneDrive first, or use Change… to pick any folder.");
            return;
        }
        await ChangeLibraryAsync(Path.Combine(oneDrive, "Automation Portal Library"));
    }

    private async Task ChangeLibraryAsync(string target)
    {
        var current = Path.GetFullPath(_host.Paths.LibraryFolder).TrimEnd('\\');
        var next = Path.GetFullPath(target).TrimEnd('\\');
        var kind = ClassifyLibraryTarget(current, next);
        switch (kind)
        {
            case LibraryTarget.Same: return;
            case LibraryTarget.InsideCurrent:
                await Prompts.MessageAsync(Owner, "Choose another folder", "That folder is inside your current library. Choose a folder somewhere else.");
                return;
            case LibraryTarget.NotEmpty:
                // Copying into a folder that already holds other files could replace them. Only empty folders or existing libraries.
                await Prompts.MessageAsync(Owner, "Choose an empty folder",
                    $"{next}\n\nalready has files in it. Choose an empty folder, or a folder that already holds an Automation Portal library.");
                return;
        }
        var targetHasLibrary = kind == LibraryTarget.ExistingLibrary;
        var message = targetHasLibrary
            ? $"Automation Portal will use the shortcuts already in\n{next}\n\nYour current shortcuts stay where they are. Automations are paused until you turn them back on in Automations, so you can check that library's automations first."
            : $"Automation Portal will copy your shortcuts to\n{next}\n\nand use that folder from now on. Your current copy stays where it is as a backup.";
        if (!await Prompts.ConfirmAsync(Owner, "Change library location", message, "Continue")) return;
        try
        {
            if (!targetHasLibrary && Directory.Exists(current)) CopyDirectory(current, next);
            else Directory.CreateDirectory(next);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Prompts.MessageAsync(Owner, "Couldn't copy your shortcuts", $"{ex.Message}\n\nAutomation Portal keeps using your current library.");
            return;
        }
        _host.Settings.Update(s =>
        {
            s.LibraryFolder = next;
            // An adopted library's automations never start on their own: the user turns them on after looking at them.
            if (targetHasLibrary) s.AutomationsPaused = true;
        });
        await Prompts.MessageAsync(Owner, "Automation Portal will restart", "Your shortcuts are ready in the new place. Automation Portal restarts now so everything uses it from the first moment.");
        _host.Restart();
    }

    internal enum LibraryTarget { Same, InsideCurrent, ExistingLibrary, NotEmpty, EmptyOrNew }

    /// <summary>What a chosen library folder is: an existing library is adopted, an empty or new one gets a copy, anything else is refused.</summary>
    internal static LibraryTarget ClassifyLibraryTarget(string current, string next)
    {
        current = Path.GetFullPath(current).TrimEnd('\\');
        next = Path.GetFullPath(next).TrimEnd('\\');
        if (string.Equals(current, next, StringComparison.OrdinalIgnoreCase)) return LibraryTarget.Same;
        if (next.StartsWith(current + "\\", StringComparison.OrdinalIgnoreCase)) return LibraryTarget.InsideCurrent;
        if (Directory.Exists(Path.Combine(next, "shortcuts"))) return LibraryTarget.ExistingLibrary;
        if (Directory.Exists(next) && Directory.EnumerateFileSystemEntries(next).Any()) return LibraryTarget.NotEmpty;
        return LibraryTarget.EmptyOrNew;
    }

    /// <summary>
    /// Copies a library into an empty or new folder. Never replaces a file: a name that already exists stops the copy.
    /// If the copy fails, the files and folders it made are removed again (only those, never recursively), so a retry
    /// doesn't take a half copy for an existing library.
    /// </summary>
    internal static void CopyDirectory(string from, string to)
    {
        var madeFolders = new List<string>();
        var madeFiles = new List<string>();
        void MakeFolder(string path)
        {
            if (Directory.Exists(path)) return;
            Directory.CreateDirectory(path);
            madeFolders.Add(path);
        }
        try
        {
            MakeFolder(to);
            foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories).ToList())
                MakeFolder(Path.Combine(to, Path.GetRelativePath(from, dir)));
            foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories).ToList())
            {
                var dest = Path.Combine(to, Path.GetRelativePath(from, file));
                // A name that was already there makes File.Copy fail without touching it: that file isn't ours. A copy that failed
                // part-way for another reason and left a file that wasn't there before is ours, and is removed with the rest.
                var existed = File.Exists(dest);
                try { File.Copy(file, dest, overwrite: false); }
                catch (IOException ex) when (!existed && (ex.HResult & 0xFFFF) is not (80 or 183))   // ERROR_FILE_EXISTS, ERROR_ALREADY_EXISTS
                {
                    if (File.Exists(dest)) madeFiles.Add(dest);
                    throw;
                }
                madeFiles.Add(dest);
            }
        }
        catch
        {
            foreach (var f in madeFiles)
                try { if (File.Exists(f)) File.Delete(f); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            for (var i = madeFolders.Count - 1; i >= 0; i--)
                try { Directory.Delete(madeFolders[i]); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
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
    private void OnLinksToggle(object sender, RoutedEventArgs e) { if (!_loading) _host.Settings.Update(s => s.AllowAutomationPortalLinks = LinksToggle.IsChecked == true); }
    private void OnOpenData(object sender, RoutedEventArgs e) => OpenFolder(_host.Paths.RoamingRoot);
    private void OnOpenLogs(object sender, RoutedEventArgs e) => OpenFolder(_host.Paths.LogsFolder);
}
