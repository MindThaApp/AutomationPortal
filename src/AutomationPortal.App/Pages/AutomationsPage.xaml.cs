using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutomationPortal.App.Dialogs;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Logic;
using AutomationPortal.App.Services;
using AutomationPortal.Core;
using AutomationPortal.Triggers;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Pages;

public partial class AutomationsPage : UserControl, IAppPage
{
    private readonly AppHost _host;
    private bool _loading;
    private bool _reloadQueued;
    private RunResult? _testResult;

    public AutomationsPage(AppHost host)
    {
        _host = host;
        InitializeComponent();
        _host.Library.Changed += (_, e) =>
        {
            if (!(e.What.HasFlag(LibraryChange.Automations) || e.What.HasFlag(LibraryChange.Shortcuts)) || _reloadQueued) return;
            _reloadQueued = true;
            Dispatcher.BeginInvoke(() => { _reloadQueued = false; Reload(); });
        };
        _host.Settings.Changed += (_, _) => Dispatcher.BeginInvoke(RefreshPaused);
        _host.TriggerHost.Automations.AutomationRan += (_, _) => Dispatcher.BeginInvoke(Reload);
        _host.TriggerHost.Automations.StatusChanged += (_, _) => Dispatcher.BeginInvoke(Reload);
        _host.Runs.StateChanged += (_, e) => { if (e.Source == RunSource.Automation && e.State is TileRunState.Succeeded or TileRunState.Failed) Dispatcher.BeginInvoke(Reload); };
    }

    private Window? Owner => Window.GetWindow(this);

    public void Navigated(NavTarget target)
    {
        TestBar.Visibility = Visibility.Collapsed;
        Reload();
    }

    private void RefreshPaused()
    {
        _loading = true;
        var paused = _host.Settings.Current.AutomationsPaused;
        PauseToggle.IsChecked = paused;
        PausedBar.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;
        _loading = false;
    }

    private void OnPauseToggle(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var paused = PauseToggle.IsChecked == true;
        _host.Settings.Update(s => { s.AutomationsPaused = paused; s.AutomationsPausedUntil = null; });
    }

    // ---- list ------------------------------------------------------------------------------

    private void Reload()
    {
        RefreshPaused();
        var automations = _host.Library.Automations.OrderBy(a => a.CreatedAt).ToList();
        RowsHost.Children.Clear();
        EmptyState.Visibility = automations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SubtitleText.Text = automations.Count switch { 0 => "Run shortcuts automatically.", 1 => "1 automation", _ => $"{automations.Count} automations" };
        if (automations.Count == 0) return;

        var all = _host.History.Query(new RunHistoryFilter { Limit = JsonlRunHistory.MaxRecords }).Where(r => r.Source == RunSource.Automation).ToList();
        var recent = all.Take(400).ToList();
        foreach (var a in automations) RowsHost.Children.Add(BuildRow(a, recent, all));
    }

    private UIElement BuildRow(Automation a, IReadOnlyList<RunRecord> recent, IReadOnlyList<RunRecord> all)
    {
        var model = AutomationSentence.Build(a, _host.Triggers, _host.Conditions, _host.Library);
        var trigger = _host.Triggers.Get(a.Trigger.TriggerId)?.Descriptor;

        var card = new Border { Style = (Style)FindResource("SurfaceCard"), Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(16, 14, 16, 14) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var chip = Ui.Chip(trigger?.Icon ?? "Question24", "blue", 42, 22);
        chip.Margin = new Thickness(0, 0, 14, 0);
        chip.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(chip);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var sentence = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 15 };
        sentence.SetResourceReference(TextBlock.ForegroundProperty, "DsTextPrimaryBrush");
        foreach (var part in model.Parts)
        {
            var run = new System.Windows.Documents.Run(part.Text);
            if (part.Kind == SentencePartKind.Token) { run.FontWeight = FontWeights.SemiBold; run.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "DsAccentTextBrush"); }
            else if (part.Kind == SentencePartKind.Shortcut) run.FontWeight = FontWeights.SemiBold;
            sentence.Inlines.Add(run);
        }
        text.Children.Add(sentence);
        if (model.ConditionsText.Length > 0) text.Children.Add(Ui.Text(model.ConditionsText, secondary: true, size: 12));

        var last = recent.FirstOrDefault(r => r.ShortcutId == a.ShortcutId);
        var (ranCount, ranLast) = RanTimes.For(a.ShortcutId, all);
        var lastLine = AutomationSentence.LastRunText(a.LastRun ?? (last is { Status: RunStatus.Succeeded or RunStatus.Failed } ? last.Started : null), DateTimeOffset.Now);
        text.Children.Add(Ui.Text(lastLine, secondary: true, size: 12));
        if (RanTimes.Text(ranCount, ranLast, DateTimeOffset.Now) is { } ranLine) text.Children.Add(Ui.Text(ranLine, secondary: true, size: 12));
        if (last is { Status: RunStatus.Failed, ErrorMessage: { Length: > 0 } err } && (a.LastRun is null || last.Started >= a.LastRun.Value.AddMinutes(-1)))
        {
            var e = Ui.Text("The last run didn't finish: " + err, size: 12);
            e.SetResourceReference(TextBlock.ForegroundProperty, "DsCriticalBrush");
            text.Children.Add(e);
            text.Children.Add(FixButton(Strings.Get("AutomationsPage_SeeWhatHappened"), SymbolRegular.History24, () => _host.OpenRunHistory(last.RunId)));
        }
        if (!a.Enabled && a.PausedReason is { Length: > 0 } paused)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var p = Ui.Text("Paused: " + paused, size: 12);
            p.VerticalAlignment = VerticalAlignment.Center;
            p.SetResourceReference(TextBlock.ForegroundProperty, "DsCautionBrush");
            var resume = new Wpf.Ui.Controls.Button { Content = "Resume", Icon = new SymbolIcon(SymbolRegular.Play20), Margin = new Thickness(10, 0, 0, 0),
                ToolTip = Strings.Get("AutomationsPage_TurnThisAutomationBack") };
            resume.Click += (_, _) => SetEnabled(a, true);
            row.Children.Add(p);
            row.Children.Add(resume);
            text.Children.Add(row);
        }
        var status = _host.TriggerHost.Automations.GetStatus(a.Id);
        if (status.Error is { Length: > 0 })
        {
            var err2 = Ui.Text("Problem: " + status.Error, size: 12);
            err2.SetResourceReference(TextBlock.ForegroundProperty, "DsCriticalBrush");
            text.Children.Add(err2);
            text.Children.Add(FixButton(Strings.Get("AutomationsPage_EditAutomation"), SymbolRegular.Edit24, () => _ = EditAsync(a, AutomationProblem.TriggerError)));
        }
        else if (status.LastSkipReason is { Length: > 0 }) text.Children.Add(Ui.Text("Skipped last time: " + status.LastSkipReason, secondary: true, size: 12));
        if (model.TriggerMissing)
        {
            text.Children.Add(Ui.Text(Strings.Get("AutomationsPage_ThisAutomationCantRun"), secondary: true, size: 12));
            text.Children.Add(FixButton(Strings.Get("AutomationsPage_PickAnotherTrigger"), SymbolRegular.Edit24, () => _ = EditAsync(a, AutomationProblem.TriggerMissing)));
        }
        if (model.ShortcutMissing)
        {
            text.Children.Add(Ui.Text(Strings.Get("AutomationsPage_TheShortcutItRuns"), secondary: true, size: 12));
            text.Children.Add(FixButton(Strings.Get("AutomationsPage_ChooseAnotherShortcut"), SymbolRegular.Edit24, () => _ = EditAsync(a, AutomationProblem.ShortcutMissing)));
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var test = new Wpf.Ui.Controls.Button { Content = Strings.Get("AutomationsPage_TestNow"), Icon = new SymbolIcon(SymbolRegular.Play20), ToolTip = Strings.Get("AutomationsPage_RunTheShortcutOnce"), IsEnabled = !model.ShortcutMissing };
        test.Click += (_, _) => _ = TestAsync(a);
        var more = new Wpf.Ui.Controls.Button { Icon = new SymbolIcon(SymbolRegular.MoreHorizontal24), Appearance = ControlAppearance.Transparent, Margin = new Thickness(6, 0, 0, 0), ToolTip = Strings.Get("AutomationsPage_MoreOptions") };
        System.Windows.Automation.AutomationProperties.SetName(more, Strings.Get("AutomationsPage_MoreOptions"));
        AutomationProperties.SetName(more, "More options");
        more.Click += (_, _) => ShowMenu(a, more);
        var toggle = new ToggleSwitch { IsChecked = a.Enabled, Margin = new Thickness(10, 0, 0, 0), ToolTip = Strings.Get("AutomationsPage_TurnThisAutomationOn") };
        AutomationProperties.SetName(toggle, "Enabled");
        toggle.Click += (_, _) => SetEnabled(a, toggle.IsChecked == true);
        var act = new Wpf.Ui.Controls.Button { Content = Strings.Get("AutomationsPage_Activity"), Appearance = ControlAppearance.Transparent, Margin = new Thickness(6, 0, 0, 0), ToolTip = Strings.Get("AutomationsPage_SeeWhenThisAutomation") };
        AutomationProperties.SetName(act, "Activity");
        act.Click += (_, _) => _ = AutomationActivityDialog.ShowAsync(Owner, _host.TriggerHost.Automations.Activity, a.Id, ActivityTitle(a));
        right.Children.Add(test);
        right.Children.Add(act);
        right.Children.Add(more);
        right.Children.Add(toggle);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        card.Child = grid;
        card.Opacity = a.Enabled ? 1 : 0.6;
        card.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2 && e.OriginalSource is not (System.Windows.Controls.Primitives.ButtonBase or ToggleSwitch)) _ = EditAsync(a); };
        return card;
    }

    /// <summary>A button under a problem line. It only opens a window; nothing changes until the person confirms there.</summary>
    private static Wpf.Ui.Controls.Button FixButton(string label, SymbolRegular icon, Action open)
    {
        var b = new Wpf.Ui.Controls.Button { Content = label, Icon = new SymbolIcon(icon), Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        b.Click += (_, _) => open();
        return b;
    }

    private string ActivityTitle(Automation a) => _host.Library.GetById(a.ShortcutId)?.Name ?? "automation";

    private void SetEnabled(Automation a, bool enabled)
    {
        var copy = (_host.Library.Automations.FirstOrDefault(x => x.Id == a.Id) ?? a).Clone();
        copy.Enabled = enabled;
        copy.PausedReason = null;
        _host.Library.SaveAutomation(copy);
    }

    private void ShowMenu(Automation a, FrameworkElement anchor)
    {
        var menu = new ContextMenu();
        var edit = new Wpf.Ui.Controls.MenuItem { Header = "Edit", Icon = new SymbolIcon(SymbolRegular.Edit24) };
        edit.Click += (_, _) => _ = EditAsync(a);
        var activity = new Wpf.Ui.Controls.MenuItem { Header = Strings.Get("AutomationsPage_Activity"), Icon = new SymbolIcon(SymbolRegular.History24) };
        activity.Click += (_, _) => _ = AutomationActivityDialog.ShowAsync(Owner, _host.TriggerHost.Automations.Activity, a.Id, ActivityTitle(a));
        AutomationProperties.SetName(activity, "Activity");
        var open = new Wpf.Ui.Controls.MenuItem { Header = Strings.Get("AutomationsPage_OpenTheShortcut"), Icon = new SymbolIcon(SymbolRegular.Open24) };
        open.Click += (_, _) => _host.EditorLauncher.Open(a.ShortcutId);
        open.IsEnabled = _host.Library.GetById(a.ShortcutId) is not null;
        var delete = new Wpf.Ui.Controls.MenuItem { Header = "Delete", Icon = new SymbolIcon(SymbolRegular.Delete24) };
        delete.Click += async (_, _) =>
        {
            if (await Prompts.ConfirmAsync(Owner, Strings.Get("AutomationsPage_DeleteAutomation"), Strings.Get("AutomationsPage_DeleteThisAutomationThe"), "Delete", danger: true))
                _host.Library.DeleteAutomation(a.Id);
        };
        menu.Items.Add(edit);
        menu.Items.Add(open);
        menu.Items.Add(activity);
        menu.Items.Add(new Separator());
        menu.Items.Add(delete);
        menu.PlacementTarget = anchor;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    // ---- actions ---------------------------------------------------------------------------

    private Task EditAsync(Automation a, AutomationProblem problem) => AutomationWizardWindow.ShowAsync(_host, Owner, null, a, focus: AutomationFixTarget.For(problem));

    private Task EditAsync(Automation a) => AutomationWizardWindow.ShowAsync(_host, Owner, null, a);

    private void OnNewAutomation(object sender, RoutedEventArgs e) => _ = AutomationWizardWindow.ShowAsync(_host, Owner);

    private void OnBrowseGallery(object sender, RoutedEventArgs e) => (Owner as MainWindow)?.NavigateTo(new NavTarget(AppPage.Gallery));

    /// <summary>
    /// Only the trigger's input is a sample: every step runs for real, including steps with their own fixed paths.
    /// The prompt says so and names the steps that can change or delete things.
    /// </summary>
    private string TestPrompt(Shortcut shortcut) => TestPromptText(shortcut, _host.Actions);

    internal static string TestPromptText(Shortcut shortcut, ActionRegistry actions)
    {
        var risky = shortcut.Actions
            .Where(x => x.Flow is not { Role: not FlowRole.Start })
            .Select(x => actions.Describe(x.ActionId) is { } d && d.IsDestructiveFor(x) ? d.Title : null)
            .OfType<string>().Distinct().ToList();
        var text = $"This runs \"{shortcut.Name}\" once now. Only what the trigger hands over is a sample (files in a temporary folder); every step runs for real, so steps that use their own folders or files act on them.";
        return risky.Count == 0 ? text : text + $"\n\nSteps that can change or delete things: {string.Join(", ", risky)}.";
    }

    private async Task TestAsync(Automation a)
    {
        if (_host.Library.GetById(a.ShortcutId) is not { } shortcut) return;
        if (!await Prompts.ConfirmAsync(Owner, Strings.Get("AutomationsPage_TestNow"), TestPrompt(shortcut), Strings.Get("AutomationsPage_TestNow"))) return;

        // Sample files, pictures and folders live in a fresh temp sandbox, never in Documents or on a drive.
        SampleSandbox sandbox;
        try { sandbox = SampleSandbox.Create(_host.Paths); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write("Test now could not make the sample files: " + ex.Message);
            ShowTest(Strings.Get("AutomationsPage_TheTestDidntStart"), Strings.Get("AutomationsPage_CouldntMakeSampleFiles"), null, SymbolRegular.ErrorCircle24, failed: true);
            return;
        }
        RunResult? result;
        ShowTest("Testing…", $"Running \"{shortcut.Name}\" once on sample files.", null, SymbolRegular.Play24, running: true);
        try { result = await _host.Runs.RunAsync(shortcut, SampleInputs.For(a, _host.Triggers, sandbox), RunSource.Automation); }
        finally { sandbox.Dispose(); }
        var kept = sandbox.HasLeftovers ? $" Files it left in the test folder were kept: {sandbox.Folder}" : "";

        _testResult = result;
        if (result is null) { ShowTest(Strings.Get("AutomationsPage_AlreadyRunning"), $"\"{shortcut.Name}\" is already running. Try again when it finishes.", null, SymbolRegular.Info24); return; }
        switch (result.Status)
        {
            case RunStatus.Succeeded or RunStatus.Stopped:
                ShowTest(Strings.Get("AutomationsPage_TheTestWorked"), $"\"{shortcut.Name}\" ran without problems." + kept, result, SymbolRegular.CheckmarkCircle24, success: true);
                break;
            case RunStatus.Cancelled:
                ShowTest(Strings.Get("AutomationsPage_TheTestWasCancelled"), Strings.Get("AutomationsPage_YouStoppedTheShortcut") + kept, null, SymbolRegular.DismissCircle24);
                break;
            default:
                ShowTest(Strings.Get("AutomationsPage_TheTestDidntFinish"), (result.ErrorMessage ?? Strings.Get("UnattendedFailure_SomethingWentWrong")) + kept, result, SymbolRegular.ErrorCircle24, failed: true);
                break;
        }
    }

    private void ShowTest(string title, string message, RunResult? result, SymbolRegular icon, bool running = false, bool success = false, bool failed = false)
    {
        TestTitle.Text = title;
        TestMessage.Text = message;
        TestIcon.Symbol = icon;
        TestIcon.SetResourceReference(Control.ForegroundProperty, failed ? "DsCriticalBrush" : success ? "DsSuccessBrush" : "DsTextSecondaryBrush");
        TestBar.SetResourceReference(Border.BackgroundProperty, failed ? "DsCriticalBackgroundBrush" : success ? "DsSuccessBackgroundBrush" : "DsCardBrush");
        TestDetails.Visibility = result is not null && !running ? Visibility.Visible : Visibility.Collapsed;
        TestBar.Visibility = Visibility.Visible;
    }

    private void OnTestDetails(object sender, RoutedEventArgs e) => _host.OpenRunHistory(_testResult?.RunId);
    private void OnDismissTest(object sender, RoutedEventArgs e) => TestBar.Visibility = Visibility.Collapsed;
}
