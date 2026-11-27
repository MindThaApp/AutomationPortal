using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using AutomationPortal.App.Dialogs;
using AutomationPortal.App.Logic;
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
        foreach (var step in AppearanceMath.TextScaleSteps)
            TextSizeBox.Items.Add(new ComboBoxItem { Content = step + "%", Tag = step.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        _host.Settings.Changed += (_, _) => Dispatcher.BeginInvoke(Load);
        _host.HotkeyProblemsChanged += (_, _) => Dispatcher.BeginInvoke(ShowHotkeyProblems);
        _host.Plan.Changed += (_, _) => Dispatcher.BeginInvoke(ShowTrialLine);
        AiHost.Children.Add(new AiSettingsPanel(host));
        ConnectedHost.Children.Add(new ConnectedAppsPanel());
        PlanHost.Children.Add(new PlanSettingsPanel(host));
        BuildSectionJump();
        Load();
    }

    /// <summary>The row of section names under the title; each scrolls the page to its section.</summary>
    private void BuildSectionJump()
    {
        foreach (var info in SettingsLayout.Sections)
        {
            var b = new Wpf.Ui.Controls.Button { Content = info.Title, Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent, Margin = new Thickness(0, 0, 4, 4), Tag = info.Anchor };
            System.Windows.Automation.AutomationProperties.SetName(b, "Go to " + info.Title);
            b.Click += (_, _) => JumpTo(info.Anchor);
            SectionJump.Items.Add(b);
        }
    }

    private void JumpTo(string anchor)
    {
        if (FindName(anchor) is not FrameworkElement target) return;
        UpdateLayout();
        var y = target.TransformToAncestor(SettingsStack).Transform(new Point(0, 0)).Y;
        SettingsScroll.ScrollToVerticalOffset(Math.Max(0, y - 8));
    }

    private void OnEnterCoordinates(object sender, RoutedEventArgs e)
    {
        ShowCoordinates();
        HomeLatBox.Focus();
    }

    private void ShowCoordinates()
    {
        CoordinatesPanel.Visibility = Visibility.Visible;
        EnterCoordinatesButton.Visibility = Visibility.Collapsed;
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
        var (lat, lon, problem) = HomeLocationInput.Check(HomeLatBox.Text, HomeLonBox.Text);
        ShowHomeProblem(problem);
        var city = HomeCityBox.Text.Trim();
        if (problem is not null) return;   // keep the saved location until the boxes are valid
        _host.Settings.Update(x => { x.HomeCity = city.Length == 0 ? null : city; x.HomeLatitude = lat; x.HomeLongitude = lon; });
    }

    private void ShowHomeProblem(string? problem)
    {
        HomeProblem.Text = problem ?? "";
        HomeProblem.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnUseLocation(object sender, RoutedEventArgs e)
    {
        UseLocationButton.IsEnabled = false;
        try
        {
            var access = await Windows.Devices.Geolocation.Geolocator.RequestAccessAsync();
            if (access != Windows.Devices.Geolocation.GeolocationAccessStatus.Allowed)
            {
                ShowHomeProblem(Strings.Get("SettingsPage_WindowsIsntLettingAutomation"));
                return;
            }
            var locator = new Windows.Devices.Geolocation.Geolocator { DesiredAccuracyInMeters = 5000 };
            var pos = (await locator.GetGeopositionAsync(TimeSpan.FromHours(1), TimeSpan.FromSeconds(10))).Coordinate.Point.Position;
            ShowHomeProblem(null);
            HomeLatBox.Text = pos.Latitude.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            HomeLonBox.Text = pos.Longitude.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            OnHomeChanged(sender, e);
        }
        catch (Exception ex)
        {
            AppLog.Write("Use my location: " + ex.Message);
            ShowHomeProblem("Couldn't find your location just now. Try again, or type the numbers here.");
        }
        finally { UseLocationButton.IsEnabled = true; }
    }

    private Window? Owner => Window.GetWindow(this);

    private void Load()
    {
        _loading = true;
        var s = _host.Settings.Current;
        StartToggle.IsChecked = s.StartWithWindows;
        TrayToggle.IsChecked = s.KeepRunningInTray;
        ThemeBox.SelectedIndex = s.Theme switch { ThemeSetting.Light => 1, ThemeSetting.Dark => 2, _ => 0 };
        ShowAppearance(s);
        QuickRunKey.Value = s.QuickRunHotkey;
        SelectionKey.Value = s.RunOnSelectionHotkey;
        PanelKey.Value = s.QuickPanelHotkey;
        LibraryPathText.Text = _host.Paths.LibraryFolder;
        AdvancedToggle.IsChecked = s.ShowAdvancedActions;
        CrashReportsToggle.IsChecked = s.SendCrashReports;
        if (TopicBox.Text.Trim() != (s.NtfyTopic ?? "")) TopicBox.Text = s.NtfyTopic ?? "";
        NotifyToggle.IsChecked = s.NotifyOnHotkeyRuns;
        SoundDescription.Text = Strings.Get("SettingsPage_PlaySoundDescription");
        SoundToggle.IsChecked = _host.UiSettings.Current.PlayRunSounds;
        LinksModeBox.SelectedIndex = (int)s.LinksMode;
        ModelFast.IsChecked = s.AiModel == AiModelTier.Fast;
        ModelSmart.IsChecked = s.AiModel == AiModelTier.Smart;
        ModelBest.IsChecked = s.AiModel == AiModelTier.Best;
        ModelDescription.Text = ModelText(s.AiModel);
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        VersionText.Text = "Version " + (version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}");
        ShowTrialLine();
        ShowHotkeyProblems();
        ShowAllowedCallers(s.AllowedCallers ?? new List<string>(), s.AllowedCallerShortcuts ?? new List<string>());
        if (HomeCityBox.Text != (s.HomeCity ?? "")) HomeCityBox.Text = s.HomeCity ?? "";
        HomeLatBox.Text = s.HomeLatitude?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        HomeLonBox.Text = s.HomeLongitude?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        if (s.HomeLatitude is not null || s.HomeLongitude is not null) ShowCoordinates();
        _loading = false;
    }

    /// <summary>About: "Pro trial: N days left" while a trial runs and Pro was not bought.</summary>
    private void ShowTrialLine()
    {
        var left = _host.Plan.TrialDaysLeft;
        TrialText.Text = left is { } d ? PlanText.TrialLine(d) : "";
        TrialText.Visibility = left is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Apps that run shortcuts without asking (all of them, or one each after "Don't ask again"), each with a Remove button.</summary>
    private void ShowAllowedCallers(IReadOnlyList<string> callers, IReadOnlyList<string> pairs)
    {
        AllowedCallersHost.Children.Clear();
        AllowedCallersHost.Children.Add(Infrastructure.Ui.Text(
            "When another app asks to run one of your shortcuts, Automation Portal asks you first. When you tick \"Don't ask again\", that app runs that shortcut without asking.",
            secondary: true, size: 12));
        if (callers.Count == 0 && pairs.Count == 0)
        {
            var none = Infrastructure.Ui.Text(Strings.Get("SettingsPage_NoAppsYet"), secondary: true, size: 12);
            none.Margin = new Thickness(0, 6, 0, 0);
            AllowedCallersHost.Children.Add(none);
            return;
        }
        foreach (var id in callers)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = Callers.FriendlyName(id);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Infrastructure.Ui.Text(name, size: 14));
            if (name != id) text.Children.Add(Infrastructure.Ui.Text(id, secondary: true, size: 12));
            grid.Children.Add(text);
            var remove = new Wpf.Ui.Controls.Button { Content = "Remove", VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(remove, "Remove " + name + " from the apps allowed to run shortcuts");
            remove.Click += (_, _) => CallerConsent.Forget(_host.Settings, id);   // Settings.Changed reloads this list
            AddAllowedRow(grid, remove);
        }
        // "Don't ask again when Files Portal runs this shortcut": one app with one shortcut.
        foreach (var key in pairs)
        {
            if (!CallerConsent.TryParsePair(key, out var callerId, out var shortcutId)) continue;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var app = Callers.FriendlyName(callerId);
            var shortcut = _host.Library.GetById(shortcutId)?.Name ?? "a deleted shortcut";
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Infrastructure.Ui.Text($"{app} runs \"{shortcut}\"", size: 14));
            text.Children.Add(Infrastructure.Ui.Text(Strings.Get("SettingsPage_OnlyThisShortcutWithout"), secondary: true, size: 12));
            grid.Children.Add(text);
            var remove = new Wpf.Ui.Controls.Button { Content = "Remove", VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(remove, $"Ask again when {app} runs {shortcut}");
            var k = key;
            remove.Click += (_, _) => CallerConsent.ForgetPair(_host.Settings, k);
            AddAllowedRow(grid, remove);
        }
    }

    private void AddAllowedRow(Grid grid, FrameworkElement remove)
    {
        Grid.SetColumn(remove, 1);
        grid.Children.Add(remove);
        var card = new Border { Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(0, 6, 0, 0), CornerRadius = AutomationPortal.Editor.Radius.Card, BorderThickness = new Thickness(1), Child = grid };
        card.SetResourceReference(Border.BackgroundProperty, "DsCardBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "DsStrokeBrush");
        AllowedCallersHost.Children.Add(card);
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

    // ---- appearance ------------------------------------------------------------------------

    /// <summary>Selects the choice with this tag by index (the first choice when none matches), so the box shows it.</summary>
    internal static void Select(ComboBox box, string tag)
    {
        var index = box.Items.OfType<ComboBoxItem>().ToList().FindIndex(i => (string?)i.Tag == tag);
        box.SelectedIndex = index < 0 ? 0 : index;
    }

    /// <summary>The tag each Appearance box must show for these settings.</summary>
    internal static (string Accent, string Material, string Corners, string Density, string TextSize, string Motion, string Tiles) AppearanceTags(AppSettings s) =>
        (s.AccentMode == AccentMode.Preset ? (AutomationPortal.Editor.Palette.CategoryNameOrNull(s.AccentPreset) ?? "Windows") : "Windows",
         s.Material.ToString(), s.CornerStyle.ToString(), s.Density.ToString(),
         NearestTextStep(s.TextScale).ToString(System.Globalization.CultureInfo.InvariantCulture), s.MotionMode.ToString(), s.TileLook.ToString());

    private void ShowAppearance(AppSettings s)
    {
        var tags = AppearanceTags(s);
        Select(AccentBox, tags.Accent); Select(MaterialBox, tags.Material); Select(CornerBox, tags.Corners); Select(DensityBox, tags.Density);
        Select(TextSizeBox, tags.TextSize); Select(MotionBox, tags.Motion); Select(TileLookBox, tags.Tiles);
    }

    private static string? TagOf(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;

    private static int NearestTextStep(int percent) => AppearanceMath.TextScaleSteps.OrderBy(x => Math.Abs(x - percent)).First();

    private static T Pick<T>(ComboBox box, T fallback) where T : struct, Enum => Enum.TryParse<T>(TagOf(box), out var v) ? v : fallback;

    private void OnAppearanceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || e.OriginalSource != sender) return;
        var accent = TagOf(AccentBox);
        var preset = accent is not null && accent != "Windows";
        var material = Pick(MaterialBox, MaterialSetting.Mica);
        var corners = Pick(CornerBox, CornerStyle.Rounded);
        var density = Pick(DensityBox, Density.Standard);
        var motion = Pick(MotionBox, MotionSetting.FollowWindows);
        var tiles = Pick(TileLookBox, TileLook.Cards);
        var text = int.TryParse(TagOf(TextSizeBox), out var pct) ? AppearanceMath.ClampTextScale(pct) : AppearanceMath.DefaultTextScale;
        _host.Settings.Update(s =>
        {
            s.AccentMode = preset ? AccentMode.Preset : AccentMode.Windows;
            if (preset) s.AccentPreset = accent!;
            s.Material = material; s.CornerStyle = corners; s.Density = density; s.TextScale = text; s.MotionMode = motion; s.TileLook = tiles;
        });   // Settings.Changed applies it live
    }

    private void OnResetAppearance(object sender, RoutedEventArgs e) => _host.Settings.Update(s => s.ResetAppearance());
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
        // The picker can only show a folder that exists; the default one isn't made here, so cancelling leaves nothing behind.
        var initial = LibraryBackup.DefaultFolder;
        if (!Directory.Exists(initial)) initial = Path.GetDirectoryName(initial)!;
        if (PickerHost.Current.PickPath(new PickRequest(PickKind.Folder, Strings.Get("SettingsPage_ChooseWhereToSave"), InitialDirectory: initial, Owner: Owner)) is not { } backupFolder) return;
        try
        {
            TestRootGuard.Check(backupFolder);
            var zip = LibraryBackup.Create(_host.Paths, backupFolder, DateTime.Now);
            await Prompts.MessageAsync(Owner, Strings.Get("SettingsPage_LibraryBackedUp"), $"Your library was saved to:\n{zip}\n\nTo bring shortcuts back later, use \"Restore from a backup\" in Settings.");
        }
        catch (LibraryException ex) { await Prompts.MessageAsync(Owner, "Couldn't back up your library", ex.Message); }
    }

    private async void OnRestoreBackup(object sender, RoutedEventArgs e) => await ImportRestore.RestoreAsync(_host, Owner);

    private async void OnChangeLibrary(object sender, RoutedEventArgs e)
    {
        if (PickerHost.Current.PickPath(new PickRequest(PickKind.Folder, Strings.Get("SettingsPage_ChooseWhereToKeep"), Owner: Owner)) is not { } folder) return;
        await ChangeLibraryAsync(folder);
    }

    private async void OnMoveToOneDrive(object sender, RoutedEventArgs e)
    {
        var oneDrive = UserFolders.Map("onedrive") ?? Environment.GetEnvironmentVariable("OneDrive") ?? Environment.GetEnvironmentVariable("OneDriveConsumer");
        if (string.IsNullOrWhiteSpace(oneDrive) || !Directory.Exists(oneDrive))
        {
            await Prompts.MessageAsync(Owner, "OneDrive wasn't found", Strings.Get("SettingsPage_AutomationPortalCouldntFind"));
            return;
        }
        await ChangeLibraryAsync(Path.Combine(oneDrive, "Automation Portal Library"));
    }

    private async Task ChangeLibraryAsync(string target)
    {
        if (!UserFolders.IsInsideRoot(target))
        {
            await Prompts.MessageAsync(Owner, "Not allowed in a test run", "This run uses a test data root, so the library can only be moved to a folder inside it.");
            return;
        }
        var current = Path.GetFullPath(_host.Paths.LibraryFolder).TrimEnd('\\');
        var next = Path.GetFullPath(target).TrimEnd('\\');
        var kind = ClassifyLibraryTarget(current, next);
        switch (kind)
        {
            case LibraryTarget.Same: return;
            case LibraryTarget.InsideCurrent:
                await Prompts.MessageAsync(Owner, Strings.Get("SettingsPage_ChooseAnotherFolder"), Strings.Get("SettingsPage_ThatFolderIsInside"));
                return;
            case LibraryTarget.NotEmpty:
                // Copying into a folder that already holds other files could replace them. Only empty folders or existing libraries.
                await Prompts.MessageAsync(Owner, Strings.Get("SettingsPage_ChooseAnEmptyFolder"),
                    $"{next}\n\nalready has files in it. Choose an empty folder, or a folder that already holds an Automation Portal library.");
                return;
        }
        var targetHasLibrary = kind == LibraryTarget.ExistingLibrary;
        var message = targetHasLibrary
            ? $"Automation Portal will use the shortcuts already in\n{next}\n\nYour current shortcuts stay where they are. Automations are paused until you turn them back on in Automations, so you can check that library's automations first."
            : $"Automation Portal will copy your shortcuts to\n{next}\n\nand use that folder from now on. Your current copy stays where it is as a backup.";
        if (!await Prompts.ConfirmAsync(Owner, Strings.Get("SettingsPage_ChangeLibraryLocation"), message, "Continue")) return;
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
        await Prompts.MessageAsync(Owner, Strings.Get("SettingsPage_AutomationPortalWillRestart"), Strings.Get("SettingsPage_YourShortcutsAreReady"));
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

    private void OnSoundToggle(object sender, RoutedEventArgs e) { if (!_loading) _host.UiSettings.Update(d => d.PlayRunSounds = SoundToggle.IsChecked == true); }
    private void OnCrashReportsToggle(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var on = CrashReportsToggle.IsChecked == true;
        _host.Settings.Update(s => s.SendCrashReports = on);
        Startup.CrashReporting.Current?.Update(on);
    }

    private void OnNotifyToggle(object sender, RoutedEventArgs e) { if (!_loading) _host.Settings.Update(s => s.NotifyOnHotkeyRuns = NotifyToggle.IsChecked == true); }
    private void OnLinksModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LinksModeBox.SelectedIndex < 0) return;
        var mode = (LinkMode)LinksModeBox.SelectedIndex;
        _host.Settings.Update(s => s.LinksMode = mode);
    }
    private void OnOpenData(object sender, RoutedEventArgs e) => OpenFolder(_host.Paths.RoamingRoot);
    private void OnOpenLogs(object sender, RoutedEventArgs e) => OpenFolder(_host.Paths.LogsFolder);
    private void OnShowWelcome(object sender, RoutedEventArgs e) => _host.ShowWelcome();
    private void OnShowWhatsNew(object sender, RoutedEventArgs e) => _host.ShowWhatsNew();
    private void OnShowTour(object sender, RoutedEventArgs e) => _host.ShowTour();
}

