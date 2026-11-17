using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutomationPortal.App.Dialogs;
using AutomationPortal.App.Services;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Pages;

public sealed class HistoryRowVm
{
    public HistoryRowVm(RunRecord r)
    {
        Record = r;
        Name = r.ShortcutName;
        When = FriendlyTime(r.Started);
        Duration = FriendlyDuration(r.DurationMs);
        StatusSymbol = r.Status switch { RunStatus.Succeeded => SymbolRegular.CheckmarkCircle24, RunStatus.Failed => SymbolRegular.ErrorCircle24, RunStatus.Cancelled => SymbolRegular.DismissCircle24, _ => SymbolRegular.RecordStop24 };
        StatusBrush = new SolidColorBrush(AutomationPortal.Editor.HighContrast.Active ? SystemColors.WindowTextColor : r.Status switch { RunStatus.Succeeded => Color.FromRgb(0x2F, 0xB3, 0x44), RunStatus.Failed => Color.FromRgb(0xE0, 0x31, 0x31), RunStatus.Cancelled => Color.FromRgb(0x86, 0x8E, 0x96), _ => Color.FromRgb(0xF0, 0x8C, 0x00) });
        StatusBrush.Freeze();
        SourceSymbol = r.Source switch
        {
            RunSource.Editor => SymbolRegular.Edit24, RunSource.Library => SymbolRegular.Apps24, RunSource.Hotkey => SymbolRegular.Keyboard24,
            RunSource.Automation => SymbolRegular.FlashAuto24, RunSource.Cli => SymbolRegular.Code24, RunSource.SendTo => SymbolRegular.Send24,
            RunSource.QuickRun => SymbolRegular.Search24, RunSource.Link => SymbolRegular.Link24, RunSource.Nested => SymbolRegular.ArrowRepeatAll24,
            _ => SymbolRegular.Alert24,
        };
        SourceText = SourceName(r.Source);
        Day = RunHistoryGrouping.DayLabel(r.Started.ToLocalTime().Date, DateTime.Now.Date);
    }

    public string Day { get; }

    public RunRecord Record { get; }
    public string Name { get; }
    public string When { get; }
    public string Duration { get; }
    public SymbolRegular StatusSymbol { get; }
    public SolidColorBrush StatusBrush { get; }
    public SymbolRegular SourceSymbol { get; }
    public string SourceText { get; }

    public static string SourceName(RunSource s) => s switch
    {
        RunSource.Editor => "Run from the editor", RunSource.Library => "Run from your library", RunSource.Hotkey => "Run with a hotkey",
        RunSource.Automation => "Run by an automation", RunSource.Cli => "Run from the command line", RunSource.SendTo => "Run from Explorer",
        RunSource.QuickRun => "Run from Quick Run", RunSource.Link => "Run from a link", RunSource.Nested => "Run by another shortcut", _ => "Run by a reminder",
    };

    public static string StatusName(RunStatus s) => s switch
    {
        RunStatus.Succeeded => "Finished", RunStatus.Failed => "Didn't finish", RunStatus.Cancelled => "You stopped it", _ => "Stopped itself",
    };

    public static string FriendlyTime(DateTimeOffset t)
    {
        var local = t.ToLocalTime();
        var today = DateTime.Now.Date;
        if (local.Date == today) return "Today at " + local.ToString("t");
        if (local.Date == today.AddDays(-1)) return "Yesterday at " + local.ToString("t");
        return local.ToString("d") + " at " + local.ToString("t");
    }

    public static string FriendlyDuration(double ms) =>
        ms < 1000 ? $"{Math.Max(1, (int)ms)} ms" : ms < 60_000 ? $"{ms / 1000:0.#} s" : $"{(int)(ms / 60_000)} min {(int)(ms / 1000 % 60)} s";
}

public partial class RunHistoryPage : UserControl, IAppPage
{
    private readonly AppHost _host;
    private string? _wantedRun;
    private bool _loading;

    public RunHistoryPage(AppHost host)
    {
        _host = host;
        InitializeComponent();
        _loading = true;
        StatusBox.ItemsSource = new[] { Strings.Get("RunHistoryPage_AllRuns"), "Finished", "Didn't finish", Strings.Get("RunHistoryPage_YouStoppedIt"), Strings.Get("RunHistoryPage_StoppedItself") };
        StatusBox.SelectedIndex = 0;
        SourceBox.ItemsSource = new[] { Strings.Get("RunHistoryPage_AllSources"), "Manual", "Automations", Strings.Get("RunHistoryPage_LinksAndOtherApps") };
        SourceBox.SelectedIndex = 0;
        _loading = false;
        _host.Runs.StateChanged += (_, e) => { if (e.State is TileRunState.Succeeded or TileRunState.Failed && IsVisible) Dispatcher.BeginInvoke(Reload); };
    }

    public void Navigated(NavTarget target)
    {
        _wantedRun = target.Item;
        Reload();
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e) { if (!_loading) Reload(); }
    private void OnStatusChanged(object sender, SelectionChangedEventArgs e) { if (!_loading) Reload(); }

    private void Reload()
    {
        var filter = new RunHistoryFilter { Limit = 300, Text = string.IsNullOrWhiteSpace(FilterBox.Text) ? null : FilterBox.Text.Trim() };
        filter.Status = StatusBox.SelectedIndex switch { 1 => RunStatus.Succeeded, 2 => RunStatus.Failed, 3 => RunStatus.Cancelled, 4 => RunStatus.Stopped, _ => null };
        var group = (SourceGroup)Math.Max(0, SourceBox.SelectedIndex);
        var records = _host.History.Query(filter).Where(r => RunHistoryActions.InGroup(r.Source, group)).ToList();
        var previous = (RunList.SelectedItem as HistoryRowVm)?.Record.RunId;
        var rows = records.Select(r => new HistoryRowVm(r)).ToList();
        var view = new System.Windows.Data.ListCollectionView(rows);
        view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(HistoryRowVm.Day)));
        RunList.ItemsSource = view;

        var filtered = filter.Text is not null || filter.Status is not null || group != SourceGroup.All;
        ContentGrid.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        ClearButton.IsEnabled = _host.History.Query(new RunHistoryFilter { Limit = 1 }).Count > 0;
        if (rows.Count == 0)
        {
            EmptyTitle.Text = filtered ? Strings.Get("RunHistoryPage_NoRunsMatch") : Strings.Get("RunHistoryPage_NothingHasRunYet");
            EmptyText.Text = filtered ? Strings.Get("RunHistoryPage_TryADifferentSearch") : Strings.Get("RunHistoryPage_WhenYouRunA");
        }
        SubtitleText.Text = rows.Count == 1 ? "1 run" : $"{rows.Count} runs";
        // Only the records that exist are counted, so the line names how far back they go.
        if (!filtered && RunCounts.Text(_host.History.Query(new RunHistoryFilter { Limit = JsonlRunHistory.MaxRecords }), DateTimeOffset.Now) is { } counts)
            SubtitleText.Text = counts;

        var wanted = _wantedRun ?? previous;
        _wantedRun = null;
        RunList.SelectedItem = rows.FirstOrDefault(r => r.Record.RunId == wanted) ?? rows.FirstOrDefault();
        if (RunList.SelectedItem is not null) RunList.ScrollIntoView(RunList.SelectedItem);
    }

    private void OnRunSelected(object sender, SelectionChangedEventArgs e)
    {
        DetailsPanel.Children.Clear();
        if (RunList.SelectedItem is not HistoryRowVm row) return;
        var r = row.Record;

        // Header
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new SymbolIcon(row.StatusSymbol) { FontSize = 30, Foreground = row.StatusBrush, Margin = new Thickness(0, 0, 12, 0) });
        var titles = new StackPanel();
        titles.Children.Add(Ui.Text(r.ShortcutName, size: 20, weight: FontWeights.SemiBold));
        titles.Children.Add(Ui.Text($"{HistoryRowVm.StatusName(r.Status)}  ·  {row.When}  ·  {row.Duration}", secondary: true, size: 13));
        head.Children.Add(titles);
        DetailsPanel.Children.Add(head);
        var src = Ui.Text(row.SourceText, secondary: true, size: 12);
        src.Margin = new Thickness(0, 6, 0, 0);
        DetailsPanel.Children.Add(src);
        DetailsPanel.Children.Add(BuildActions(r));
        if (BuildFilesChanged(r) is { } changed) DetailsPanel.Children.Add(changed);

        // Problem
        if (r.Status == RunStatus.Failed && !string.IsNullOrWhiteSpace(r.ErrorMessage))
        {
            var err = new Border { Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(14, 10, 14, 10), CornerRadius = new CornerRadius(8), Child = Ui.Text(r.ErrorMessage!, size: 14) };
            err.SetResourceReference(Border.BackgroundProperty, "SystemFillColorCriticalBackgroundBrush");
            DetailsPanel.Children.Add(err);
        }

        // Output
        if (!string.IsNullOrWhiteSpace(r.OutputPreview))
        {
            DetailsPanel.Children.Add(Heading("Result"));
            var box = new Border { Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(8), Child = Ui.Text(r.OutputPreview, size: 13) };
            box.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
            DetailsPanel.Children.Add(box);
        }

        // Steps
        DetailsPanel.Children.Add(Heading(r.Steps.Count == 0 ? "Steps" : $"Steps ({r.Steps.Count})"));
        if (r.Steps.Count == 0) DetailsPanel.Children.Add(Ui.Text("No steps were recorded for this run.", secondary: true, size: 13));
        var n = 0;
        foreach (var s in r.Steps)
        {
            n++;
            DetailsPanel.Children.Add(StepRow(n, s, s.Uid == r.FailedStepUid));
        }
    }

    private UIElement BuildActions(RunRecord r)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var shortcut = _host.Library.GetById(r.ShortcutId);
        var deleted = shortcut is null && _host.LibraryStore.RecentlyDeleted.List().Any(d => d.ShortcutId == r.ShortcutId);
        var again = RunHistoryActions.RunAgain(shortcut, deleted);

        row.Children.Add(ActionButton(again.AsksForFiles ? Strings.Get("RunHistoryPage_RunAgain") : Strings.Get("RunHistoryPage_RunAgain2"), SymbolRegular.ArrowRepeatAll20, again.Enabled, again.Tooltip, () => RunAgainAsync(r.ShortcutId, again.AsksForFiles)));
        if (r.Status == RunStatus.Failed)
        {
            var step = RunHistoryActions.FailedStep(r);
            var canOpen = shortcut is not null;
            var tip = !canOpen ? Strings.Get("RunHistoryPage_ThisShortcutNoLonger") : Strings.Get("RunHistoryPage_OpenTheEditorWith");
            row.Children.Add(ActionButton(Strings.Get("RunHistoryPage_OpenAtFailedStep"), SymbolRegular.Edit20, canOpen, tip, () => OpenAtFailedStep(r, step)));
        }
        if (RunUndo.CanUndo(_host.Paths, r.RunId))
        {
            UIElement? undo = null;
            undo = ActionButton(Strings.Get("RunHistoryPage_UndoFileChanges"), SymbolRegular.ArrowUndo20, true,
                Strings.Get("RunHistoryPage_PutBackTheFiles"), () => UndoFileChangesAsync(r, undo!));
            row.Children.Add(undo);
        }
        row.Children.Add(ActionButton(Strings.Get("RunHistoryPage_CopyDetails"), SymbolRegular.Copy20, true, Strings.Get("RunHistoryPage_CopyAShortSummary"), () => CopyDetails(r)));
        return row;
    }

    private static UIElement ActionButton(string text, SymbolRegular icon, bool enabled, string tooltip, Action click)
    {
        var b = new Wpf.Ui.Controls.Button { Content = text, Icon = new SymbolIcon(icon), Margin = new Thickness(0, 0, 8, 6), IsEnabled = enabled, ToolTip = tooltip, Focusable = true };
        AutomationProperties.SetName(b, text);
        AutomationProperties.SetHelpText(b, tooltip);
        b.Click += (_, _) => click();
        if (enabled) return b;
        // A disabled button can't show its tooltip, so the wrapper carries it.
        var holder = new Border { Child = b, ToolTip = tooltip, Background = Brushes.Transparent };
        ToolTipService.SetShowOnDisabled(b, true);
        return holder;
    }

    /// <summary>"Files changed": what the run did on disk, from its own record, with a link to each item. Null when it changed no files.</summary>
    private UIElement? BuildFilesChanged(RunRecord r)
    {
        IReadOnlyList<FileChange> changes;
        try { changes = new FileJournal(_host.Paths).Read(r.RunId)?.Changes ?? Array.Empty<FileChange>(); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { return null; }
        if (changes.Count == 0) return null;

        var panel = new StackPanel();
        panel.Children.Add(Heading(Strings.Get("RunHistoryPage_FilesChanged")));
        var summary = Ui.Text(char.ToUpperInvariant(RunResultText.Summary(changes)[0]) + RunResultText.Summary(changes)[1..] + ".", secondary: true, size: 12);
        summary.Margin = new Thickness(0, -4, 0, 6);
        panel.Children.Add(summary);
        var (shown, more) = RunHistoryGrouping.FileRows(changes, 4);
        foreach (var row in shown)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = Ui.Text(row.Name, size: 13);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            g.Children.Add(name);
            var what = Ui.Text(row.What, secondary: true, size: 13);
            what.TextTrimming = TextTrimming.CharacterEllipsis;
            what.Margin = new Thickness(8, 0, 8, 0);
            Grid.SetColumn(what, 1);
            g.Children.Add(what);
            var path = row.Path;
            var show = new Wpf.Ui.Controls.HyperlinkButton { Content = Strings.Get("ResultView_ShowInFolder"), Padding = new Thickness(0), IsEnabled = File.Exists(path) || Directory.Exists(path) };
            AutomationProperties.SetName(show, "Show " + row.Name + " in its folder");
            show.Click += (_, _) => ShowInFolder(path);
            Grid.SetColumn(show, 2);
            g.Children.Add(show);
            panel.Children.Add(g);
        }
        if (more > 0) panel.Children.Add(Ui.Text($"and {more} more", secondary: true, size: 12));
        return panel;
    }

    private static void ShowInFolder(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { AppLog.Write("Show in folder failed: " + ex.Message); }
    }

    private async void RunAgainAsync(Guid shortcutId, bool askForFiles = false)
    {
        if (_host.Library.GetById(shortcutId) is not { } s) return;
        ContentValue? input = null;
        if (askForFiles)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Choose the files to run \"" + s.Name + "\" on", Multiselect = true, CheckFileExists = true };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true || dlg.FileNames.Length == 0) return;
            input = ContentValue.Files(dlg.FileNames);
        }
        try { await _host.Runs.RunAsync(s, input, RunSource.Library); }
        catch (Exception ex) { AppLog.Write("Run again failed: " + ex.Message); }
    }

    private async void UndoFileChangesAsync(RunRecord r, UIElement button)
    {
        if (!button.IsEnabled) return;
        button.IsEnabled = false;
        var reload = false;
        try { reload = await RunUndo.RunAsync(Window.GetWindow(this), _host.Paths, r.RunId); }
        catch (Exception ex) { AppLog.Write("Undo file changes failed: " + ex.Message); }
        finally { button.IsEnabled = true; }
        if (reload) Reload();
    }

    private async void OpenAtFailedStep(RunRecord r, StepLog? step)
    {
        var uid = step?.Uid;
        var found = _host.EditorLauncher.OpenAtStep(r.ShortcutId, uid);
        if (found) return;
        var why = uid is null
            ? Strings.Get("RunHistoryPage_ThisRunDidntRecord")
            : Strings.Get("RunHistoryPage_TheShortcutHasChanged");
        await Prompts.MessageAsync(Window.GetWindow(this), "Couldn't select the step", why);
    }

    private void CopyDetails(RunRecord r)
    {
        var text = RunHistoryActions.Summary(r, HistoryRowVm.SourceName(r.Source), HistoryRowVm.StatusName(r.Status));
        try { Clipboard.SetText(text); }
        catch (Exception ex) { AppLog.Write("Copy details failed: " + ex.Message); }
    }

    private static TextBlock Heading(string text)
    {
        var t = Ui.Text(text, size: 14, weight: FontWeights.SemiBold);
        t.Margin = new Thickness(0, 20, 0, 8);
        return t;
    }

    private UIElement StepRow(int number, StepLog s, bool failed)
    {
        var failedStep = failed || !string.IsNullOrEmpty(s.Error);
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = _host.Actions.Describe(s.ActionId);
        var chip = Ui.Chip(info?.Icon ?? "Flash24", info?.Color ?? "gray", 30, 16);
        chip.Margin = new Thickness(0, 0, 12, 0);
        chip.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(chip);

        var text = new StackPanel();
        var title = Ui.Text($"{number}. {(string.IsNullOrWhiteSpace(s.Title) ? info?.Title ?? s.ActionId : s.Title)}", weight: FontWeights.SemiBold);
        text.Children.Add(title);
        if (failedStep && !string.IsNullOrWhiteSpace(s.Error))
        {
            var e = Ui.Text(s.Error!, size: 13);
            e.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
            text.Children.Add(e);
        }
        else if (!string.IsNullOrWhiteSpace(s.OutputPreview))
            text.Children.Add(Ui.Text(s.OutputPreview, secondary: true, size: 12));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        right.Children.Add(Ui.Text(HistoryRowVm.FriendlyDuration(s.DurationMs), secondary: true, size: 12));
        var mark = new SymbolIcon(failedStep ? SymbolRegular.DismissCircle20 : SymbolRegular.CheckmarkCircle20) { FontSize = 18, Margin = new Thickness(10, 0, 0, 0) };
        mark.Foreground = AutomationPortal.Editor.HighContrast.Active ? SystemColors.WindowTextBrush : failedStep ? new SolidColorBrush(Color.FromRgb(0xE0, 0x31, 0x31)) : new SolidColorBrush(Color.FromRgb(0x2F, 0xB3, 0x44));
        right.Children.Add(mark);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return grid;
    }

    private void OnMore(object sender, RoutedEventArgs e)
    {
        var clear = new System.Windows.Controls.MenuItem { Header = Strings.Get("RunHistoryPage_ClearHistory"), Icon = new SymbolIcon(SymbolRegular.Delete20) };
        clear.Click += (_, _) => OnClear(sender, e);
        ClearButton.ContextMenu = new ContextMenu { PlacementTarget = ClearButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, Items = { clear } };
        ClearButton.ContextMenu.IsOpen = true;
    }

    private async void OnClear(object sender, RoutedEventArgs e)
    {
        if (!await Prompts.ConfirmAsync(Window.GetWindow(this), Strings.Get("RunHistoryPage_ClearRunHistory"), Strings.Get("RunHistoryPage_DeleteTheWholeRun"), Strings.Get("RunHistoryPage_ClearHistory2"), danger: true)) return;
        _host.History.Clear();
        Reload();
    }
}
