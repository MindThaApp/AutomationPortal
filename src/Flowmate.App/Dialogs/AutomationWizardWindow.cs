using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Flowmate.App.Infrastructure;
using Flowmate.App.Integration;
using Flowmate.App.Logic;
using Flowmate.Core;
using Wpf.Ui.Controls;

namespace Flowmate.App.Dialogs;

/// <summary>
/// The New Automation wizard: (1) pick a trigger, (2) set its options and "Only if…" conditions,
/// (3) choose or create the shortcut to run, (4) how it runs. Also edits an existing automation.
/// </summary>
public sealed class AutomationWizardWindow : FluentWindow
{
    private static readonly string[] CategoryOrder =
        { "Time", "Calls & Screen", "Web & Weather", "System", "Apps", "Files & Drives", "Network", "Power", "Devices", "Keyboard", "Clipboard" };

    private static readonly Dictionary<string, string> CategoryColors = new()
    {
        ["Time"] = "red", ["Calls & Screen"] = "pink", ["Web & Weather"] = "cyan", ["System"] = "graphite", ["Apps"] = "indigo",
        ["Files & Drives"] = "teal", ["Network"] = "blue", ["Power"] = "green", ["Devices"] = "slate", ["Keyboard"] = "purple", ["Clipboard"] = "orange",
    };

    private static readonly string[] StepTitles = { "What should start it?", "Set it up", "What should it run?", "How should it run?" };
    private static readonly string[] StepNames = { "Trigger", "Options", "Shortcut", "Settings" };

    private readonly AppHost _host;
    private readonly AutomationWizardState _state;
    private readonly Automation? _editing;
    private readonly TaskCompletionSource<bool> _closed = new();
    private bool _saved;

    private readonly Grid _stepBar = new();
    private readonly TextBlock _title;
    private readonly ContentControl _body = new();
    private readonly TextBlock _message;
    private readonly Wpf.Ui.Controls.Button _back, _next;
    private string _category = "";
    private string _search = "";
    private readonly List<IParameterForm> _forms = new();

    private AutomationWizardWindow(AppHost host, Automation? editing, Guid? presetShortcutId, string? presetTriggerId = null, IReadOnlyDictionary<string, ParamValue>? presetValues = null)
    {
        _host = host;
        _editing = editing;
        _state = editing is null ? new AutomationWizardState() : AutomationWizardState.FromAutomation(editing);
        if (presetShortcutId is { } id) _state.ExistingShortcutId = id;
        if (editing is null && presetTriggerId is not null && host.Triggers.Get(presetTriggerId) is not null)
        {
            _state.SelectTrigger(presetTriggerId);
            foreach (var (key, value) in presetValues ?? new Dictionary<string, ParamValue>()) _state.TriggerValues[key] = value;
            _state.TryNext(host.Triggers, host.Conditions, host.Library);
        }
        if (_state.TriggerId is { } tid) _category = host.Triggers.Get(tid)?.Descriptor.Category ?? "";
        if (_category.Length == 0) _category = Categories().FirstOrDefault() ?? "";
        if (_host.Library.All.Count == 0) _state.CreateNewShortcut = true;

        Title = editing is null ? "New automation" : "Edit automation";
        Width = 820; Height = 700; MinWidth = 640; MinHeight = 520;
        ShowInTaskbar = false;
        WindowBackdropType = WindowBackdropType.Mica;
        ExtendsContentIntoTitleBar = true;
        UseLayoutRounding = true;
        SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        FontFamily = (FontFamily)Application.Current.FindResource("FlowFont");

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TitleBar { Title = Title, ShowMinimize = false, ShowMaximize = false, CanMaximize = false });

        _stepBar.Margin = new Thickness(28, 6, 28, 0);
        Grid.SetRow(_stepBar, 1);
        root.Children.Add(_stepBar);

        _title = new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        _title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        _title.FontFamily = (FontFamily)Application.Current.FindResource("FlowDisplayFont");
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 14, 0) };
        var content = new StackPanel { Margin = new Thickness(28, 18, 14, 12) };
        content.Children.Add(_title);
        content.Children.Add(_body);
        scroll.Content = content;
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);

        var footer = new Grid { Margin = new Thickness(28, 8, 28, 20) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _message = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 0, 16, 0) };
        _message.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        footer.Children.Add(_message);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = new Wpf.Ui.Controls.Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        cancel.Click += (_, _) => Close();
        _back = new Wpf.Ui.Controls.Button { Content = "Back", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), Icon = new SymbolIcon(SymbolRegular.ArrowLeft20) };
        _back.Click += (_, _) => { if (_state.TryBack()) ShowStep(); };
        _next = new Wpf.Ui.Controls.Button { MinWidth = 110, Margin = new Thickness(8, 0, 0, 0), Appearance = ControlAppearance.Primary, IsDefault = true };
        _next.Click += (_, _) => OnNext();
        buttons.Children.Add(cancel);
        buttons.Children.Add(_back);
        buttons.Children.Add(_next);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        Content = root;
        Closed += (_, _) => _closed.TrySetResult(_saved);
        ShowStep();
    }

    /// <summary>Opens the wizard. Completes with true when an automation was saved.</summary>
    /// <param name="presetTriggerId">Start with this trigger chosen (and its options filled in) and go straight to the options step.</param>
    public static Task<bool> ShowAsync(AppHost host, Window? owner, Guid? presetShortcutId = null, Automation? editing = null,
        string? presetTriggerId = null, IReadOnlyDictionary<string, ParamValue>? presetValues = null)
    {
        var w = new AutomationWizardWindow(host, editing, presetShortcutId, presetTriggerId, presetValues);
        host.Theme.Refresh(w);
        if (owner is { IsVisible: true })
        {
            w.Owner = owner;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            w.Show();
        }
        else WindowPlacement.ShowCentered(w);
        return w._closed.Task;
    }

    // ---- chrome ----------------------------------------------------------------------------

    private IReadOnlyList<string> Categories()
    {
        var cats = _host.Triggers.Categories.ToList();
        return CategoryOrder.Where(cats.Contains).Concat(cats.Where(c => !CategoryOrder.Contains(c))).ToList();
    }

    private void ShowStep()
    {
        var step = (int)_state.Step;
        _title.Text = StepTitles[step];
        BuildStepBar(step);
        _forms.Clear();
        _body.Content = _state.Step switch
        {
            WizardStep.Trigger => BuildTriggerStep(),
            WizardStep.Options => BuildOptionsStep(),
            WizardStep.Shortcut => BuildShortcutStep(),
            _ => BuildSettingsStep(),
        };
        _back.Visibility = _state.Step == AutomationWizardState.First ? Visibility.Collapsed : Visibility.Visible;
        _next.Content = _state.Step == AutomationWizardState.Last ? (_editing is null ? "Create" : "Save") : "Next";
        _next.Icon = new SymbolIcon(_state.Step == AutomationWizardState.Last ? SymbolRegular.Checkmark20 : SymbolRegular.ArrowRight20);
        RefreshFooter();
    }

    private void BuildStepBar(int current)
    {
        _stepBar.Children.Clear();
        _stepBar.ColumnDefinitions.Clear();
        for (var i = 0; i < StepNames.Length; i++)
        {
            _stepBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var done = i < current;
            var active = i == current;
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
            var dot = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13) };
            if (active || done) dot.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
            else { dot.BorderThickness = new Thickness(1.5); dot.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush"); }
            dot.Child = done
                ? new SymbolIcon(SymbolRegular.Checkmark16) { FontSize = 14, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                : new TextBlock { Text = (i + 1).ToString(), FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = active ? Brushes.White : (Brush)FindResource("TextFillColorSecondaryBrush") };
            var label = Ui.Text(StepNames[i], secondary: !active, size: 13, weight: active ? FontWeights.SemiBold : FontWeights.Normal, wrap: false);
            label.Margin = new Thickness(8, 0, 0, 0);
            label.VerticalAlignment = VerticalAlignment.Center;
            panel.Children.Add(dot);
            panel.Children.Add(label);
            Grid.SetColumn(panel, i);
            _stepBar.Children.Add(panel);
        }
    }

    private void RefreshFooter()
    {
        var block = _state.BlockingMessage(_host.Triggers, _host.Conditions, _host.Library);
        _message.Text = block ?? "";
        _next.IsEnabled = block is null;
    }

    private async void OnNext()
    {
        if (_state.Step == AutomationWizardState.Last) { await SaveAsync(); return; }
        if (_state.TryNext(_host.Triggers, _host.Conditions, _host.Library)) ShowStep();
    }

    // ---- step 1: trigger -------------------------------------------------------------------

    private FrameworkElement BuildTriggerStep()
    {
        var root = new StackPanel();
        var search = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Search triggers", Icon = new SymbolIcon(SymbolRegular.Search20), Text = _search, Margin = new Thickness(0, 0, 0, 12) };
        root.Children.Add(search);

        var pills = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var cards = new WrapPanel();

        void Fill()
        {
            pills.Children.Clear();
            cards.Children.Clear();
            var searching = _search.Trim().Length > 0;
            pills.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
            foreach (var c in Categories())
            {
                var cat = c;
                var pill = new Wpf.Ui.Controls.Button
                {
                    Content = cat, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 6, 14, 6),
                    Appearance = cat == _category ? ControlAppearance.Primary : ControlAppearance.Secondary,
                };
                pill.Click += (_, _) => { _category = cat; Fill(); };
                pills.Children.Add(pill);
            }
            var list = searching ? _host.Triggers.Search(_search) : _host.Triggers.ByCategory(_category);
            foreach (var d in list) cards.Children.Add(TriggerCard(d, Fill));
            if (list.Count == 0)
                cards.Children.Add(Ui.Text(searching ? "No triggers match your search." : "There are no triggers in this group yet.", secondary: true));
        }

        search.TextChanged += (_, _) => { _search = search.Text; Fill(); };
        root.Children.Add(pills);
        root.Children.Add(cards);
        Fill();
        return root;
    }

    private FrameworkElement TriggerCard(TriggerDescriptor d, Action refresh)
    {
        var selected = string.Equals(_state.TriggerId, d.Id, StringComparison.OrdinalIgnoreCase);
        var button = new System.Windows.Controls.Button { Style = (Style)FindResource("BigCardButton"), Width = 244, Margin = new Thickness(0, 0, 12, 12), MinHeight = 92, VerticalContentAlignment = VerticalAlignment.Top };
        AutomationProperties.SetName(button, d.Title);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var chip = Ui.Chip(d.Icon, CategoryColors.GetValueOrDefault(d.Category, "gray"), 40, 20);
        chip.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(chip);
        var text = new StackPanel { Margin = new Thickness(12, 0, 4, 0) };
        text.Children.Add(Ui.Text(d.Title, weight: FontWeights.SemiBold, size: 14));
        text.Children.Add(Ui.Text(d.Description, secondary: true, size: 12));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (selected)
        {
            var tick = new SymbolIcon(SymbolRegular.CheckmarkCircle24) { FontSize = 20, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            tick.SetResourceReference(Control.ForegroundProperty, "AccentFillColorDefaultBrush");
            Grid.SetColumn(tick, 1);
            grid.Children.Add(tick);
            button.BorderBrush = (Brush)FindResource("AccentFillColorDefaultBrush");
        }
        button.Content = grid;
        button.Click += (_, _) =>
        {
            _state.SelectTrigger(d.Id);
            RefreshFooter();
            refresh();
        };
        button.MouseDoubleClick += (_, _) => OnNext();
        return button;
    }

    // ---- step 2: options + conditions ------------------------------------------------------

    private FrameworkElement BuildOptionsStep()
    {
        var root = new StackPanel();
        var trigger = _host.Triggers.Get(_state.TriggerId ?? "")?.Descriptor;
        if (trigger is null) { root.Children.Add(Ui.Text("Go back and pick what should start the automation.", secondary: true)); return root; }

        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        head.Children.Add(Ui.Chip(trigger.Icon, CategoryColors.GetValueOrDefault(trigger.Category, "gray"), 40, 20));
        var titles = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Ui.Text(trigger.Title, weight: FontWeights.SemiBold, size: 16));
        titles.Children.Add(Ui.Text(trigger.Description, secondary: true, size: 12));
        head.Children.Add(titles);
        root.Children.Add(head);

        var sentence = Ui.Text("", size: 15, weight: FontWeights.SemiBold);
        sentence.Margin = new Thickness(0, 10, 0, 16);
        root.Children.Add(sentence);
        void UpdateSentence() => sentence.Text = AutomationSentence.RenderSummary(trigger.SummarySegments, trigger.Parameters, _state.TriggerValues);
        UpdateSentence();

        if (trigger.Parameters.Count == 0)
            root.Children.Add(Ui.Text("This trigger has nothing to set up.", secondary: true));
        else
        {
            var form = _host.ParameterForms.Create(trigger.Parameters, _state.TriggerValues, _host.Services);
            form.Changed += (_, _) => { UpdateSentence(); RefreshFooter(); };
            _forms.Add(form);
            root.Children.Add(form.Element);
        }
        if (!string.IsNullOrWhiteSpace(trigger.OutputDescription))
            root.Children.Add(Ui.Text("Your shortcut receives: " + trigger.OutputDescription, secondary: true, size: 12));

        // Only if…
        var onlyIf = Ui.Text("Only if… (optional)", size: 15, weight: FontWeights.SemiBold);
        onlyIf.Margin = new Thickness(0, 22, 0, 4);
        root.Children.Add(onlyIf);
        root.Children.Add(Ui.Text("Add conditions to run only at the right moments. All of them must be true.", secondary: true, size: 12));
        var conditionHost = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        root.Children.Add(conditionHost);

        void FillConditions()
        {
            conditionHost.Children.Clear();
            foreach (var draft in _state.Conditions.ToList())
            {
                var d = _host.Conditions.Get(draft.ConditionId)?.Descriptor;
                if (d is null) continue;
                var card = new Border { Style = (Style)FindResource("SurfaceCard"), Margin = new Thickness(0, 0, 0, 8) };
                var inner = new StackPanel();
                var top = new Grid();
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                top.ColumnDefinitions.Add(new ColumnDefinition());
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                top.Children.Add(Ui.Icon(d.Icon, 20));
                var name = Ui.Text(d.Title, weight: FontWeights.SemiBold);
                name.Margin = new Thickness(10, 0, 0, 0);
                name.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(name, 1);
                top.Children.Add(name);
                var remove = new Wpf.Ui.Controls.Button { Icon = new SymbolIcon(SymbolRegular.Dismiss16), Appearance = ControlAppearance.Transparent, ToolTip = "Remove this condition" };
                remove.Click += (_, _) => { _state.Conditions.Remove(draft); FillConditions(); RefreshFooter(); };
                AutomationProperties.SetName(remove, "Remove condition");
                Grid.SetColumn(remove, 2);
                top.Children.Add(remove);
                inner.Children.Add(top);
                if (d.Parameters.Count > 0)
                {
                    var form = _host.ParameterForms.Create(d.Parameters, draft.Values, _host.Services);
                    form.Element.Margin = new Thickness(0, 10, 0, -10);
                    form.Changed += (_, _) => RefreshFooter();
                    _forms.Add(form);
                    inner.Children.Add(form.Element);
                }
                card.Child = inner;
                conditionHost.Children.Add(card);
            }
        }
        FillConditions();

        var add = new Wpf.Ui.Controls.Button { Content = "Add a condition", Icon = new SymbolIcon(SymbolRegular.Add20), Margin = new Thickness(0, 4, 0, 0) };
        add.Click += (_, _) =>
        {
            var menu = new ContextMenu();
            foreach (var d in _host.Conditions.Search(""))
            {
                var desc = d;
                var item = new Wpf.Ui.Controls.MenuItem { Header = d.Title, Icon = new SymbolIcon(Icons.Parse(d.Icon)), ToolTip = d.Description };
                item.Click += (_, _) => { _state.AddCondition(desc.Id); FillConditions(); RefreshFooter(); };
                menu.Items.Add(item);
            }
            menu.PlacementTarget = add;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        };
        root.Children.Add(add);
        return root;
    }

    // ---- step 3: shortcut ------------------------------------------------------------------

    private FrameworkElement BuildShortcutStep()
    {
        var root = new StackPanel();
        var existing = new RadioButton { Content = "Run a shortcut I already have", GroupName = "which", IsChecked = !_state.CreateNewShortcut, Margin = new Thickness(0, 0, 0, 8), FontSize = 14 };
        var create = new RadioButton { Content = "Create a new shortcut for this", GroupName = "which", IsChecked = _state.CreateNewShortcut, FontSize = 14 };
        var picker = new System.Windows.Controls.ComboBox { Margin = new Thickness(28, 0, 0, 20), HorizontalAlignment = HorizontalAlignment.Stretch, MaxWidth = 460, DisplayMemberPath = nameof(Shortcut.Name) };
        picker.ItemsSource = _host.Library.All;
        picker.SelectedItem = _host.Library.All.FirstOrDefault(s => s.Id == _state.ExistingShortcutId);
        AutomationProperties.SetName(picker, "Shortcut to run");
        var nameBox = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Name the new shortcut", Text = _state.NewShortcutName, Margin = new Thickness(28, 8, 0, 8), MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Stretch };
        var note = Ui.Text("You'll add the steps right after the automation is created.", secondary: true, size: 12);
        note.Margin = new Thickness(28, 0, 0, 0);

        void Sync()
        {
            _state.CreateNewShortcut = create.IsChecked == true;
            picker.IsEnabled = !_state.CreateNewShortcut;
            nameBox.IsEnabled = _state.CreateNewShortcut;
            nameBox.Visibility = note.Visibility = _state.CreateNewShortcut ? Visibility.Visible : Visibility.Collapsed;
            picker.Visibility = _state.CreateNewShortcut ? Visibility.Collapsed : Visibility.Visible;
            RefreshFooter();
        }

        existing.Checked += (_, _) => Sync();
        create.Checked += (_, _) => Sync();
        picker.SelectionChanged += (_, _) => { _state.ExistingShortcutId = (picker.SelectedItem as Shortcut)?.Id; RefreshFooter(); };
        nameBox.TextChanged += (_, _) => { _state.NewShortcutName = nameBox.Text; RefreshFooter(); };
        if (_host.Library.All.Count == 0) { existing.IsEnabled = false; existing.ToolTip = "You don't have any shortcuts yet."; }

        root.Children.Add(existing);
        root.Children.Add(picker);
        root.Children.Add(create);
        root.Children.Add(nameBox);
        root.Children.Add(note);
        Sync();
        return root;
    }

    // ---- step 4: settings ------------------------------------------------------------------

    private FrameworkElement BuildSettingsStep()
    {
        var root = new StackPanel();
        var summary = new Border { Style = (Style)FindResource("SurfaceCard"), Margin = new Thickness(0, 0, 0, 18) };
        var preview = BuildPreviewAutomation();
        summary.Child = SentenceBlock(preview);
        root.Children.Add(summary);

        root.Children.Add(Ui.Text("When it happens", size: 14, weight: FontWeights.SemiBold));
        var now = new RadioButton { Content = "Run it right away", GroupName = "mode", IsChecked = _state.RunMode == RunMode.Immediately, Margin = new Thickness(0, 8, 0, 4) };
        var ask = new RadioButton { Content = "Ask me first (a notification with Run and Skip)", GroupName = "mode", IsChecked = _state.RunMode == RunMode.AskFirst };
        now.Checked += (_, _) => _state.RunMode = RunMode.Immediately;
        ask.Checked += (_, _) => _state.RunMode = RunMode.AskFirst;
        root.Children.Add(now);
        root.Children.Add(ask);

        var notify = new ToggleSwitch { Content = "Show a notification when it runs", IsChecked = _state.NotifyWhenRun, Margin = new Thickness(0, 18, 0, 0) };
        notify.Click += (_, _) => _state.NotifyWhenRun = notify.IsChecked == true;
        root.Children.Add(notify);

        var cool = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        cool.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cool.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cool.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var l1 = Ui.Text("Don't run more than once every", wrap: false);
        l1.VerticalAlignment = VerticalAlignment.Center;
        var minutes = new NumberBox { Minimum = 0, Maximum = 1440, SmallChange = 1, Value = _state.CooldownMinutes, Width = 110, Margin = new Thickness(10, 0, 10, 0), MaxDecimalPlaces = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var l2 = Ui.Text("minutes", wrap: false);
        l2.VerticalAlignment = VerticalAlignment.Center;
        minutes.ValueChanged += (_, _) => _state.CooldownMinutes = (int)(minutes.Value ?? 0);
        AutomationProperties.SetName(minutes, "Minutes between runs");
        cool.Children.Add(l1);
        Grid.SetColumn(minutes, 1); cool.Children.Add(minutes);
        Grid.SetColumn(l2, 2); cool.Children.Add(l2);
        root.Children.Add(cool);
        root.Children.Add(Ui.Text("Leave it at 0 to run every time. A pause is handy for triggers that can fire many times in a row.", secondary: true, size: 12));
        return root;
    }

    private Automation BuildPreviewAutomation()
    {
        var shortcutId = _state.CreateNewShortcut ? Guid.Empty : _state.ExistingShortcutId ?? Guid.Empty;
        return _state.Build(shortcutId);
    }

    private FrameworkElement SentenceBlock(Automation a)
    {
        var model = AutomationSentence.Build(a, _host.Triggers, _host.Conditions, _host.Library);
        var panel = new StackPanel();
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 15 };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        foreach (var part in model.Parts)
        {
            var text = part.Kind == SentencePartKind.Shortcut && _state.CreateNewShortcut
                ? (string.IsNullOrWhiteSpace(_state.NewShortcutName) ? "your new shortcut" : _state.NewShortcutName.Trim())
                : part.Text;
            var run = new System.Windows.Documents.Run(text);
            if (part.Kind == SentencePartKind.Token) { run.FontWeight = FontWeights.SemiBold; run.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "AccentTextFillColorPrimaryBrush"); }
            else if (part.Kind == SentencePartKind.Shortcut) run.FontWeight = FontWeights.SemiBold;
            tb.Inlines.Add(run);
        }
        panel.Children.Add(tb);
        if (model.ConditionsText.Length > 0) panel.Children.Add(Ui.Text(model.ConditionsText, secondary: true, size: 13));
        return panel;
    }

    // ---- save ------------------------------------------------------------------------------

    private async Task SaveAsync()
    {
        try
        {
            Guid shortcutId;
            Guid? createdId = null;
            if (_state.CreateNewShortcut)
            {
                var s = new Shortcut
                {
                    Name = _state.NewShortcutName.Trim(), Icon = "Flash24", Color = "blue",
                    SortOrder = _host.Library.All.Count == 0 ? 0 : _host.Library.All.Max(x => x.SortOrder) + 1,
                };
                _host.Library.Save(s);
                shortcutId = s.Id;
                createdId = s.Id;
            }
            else shortcutId = _state.ExistingShortcutId ?? throw new LibraryException("Choose a shortcut to run.");

            var automation = _state.Build(shortcutId);
            if (_editing is not null)
            {
                automation.Id = _editing.Id;
                automation.Enabled = _editing.Enabled;
                automation.LastRun = _editing.LastRun;
                automation.CreatedAt = _editing.CreatedAt;
            }
            _host.Library.SaveAutomation(automation);
            _saved = true;
            Close();
            if (createdId is { } id) _host.EditorLauncher.Open(id);
        }
        catch (LibraryException ex)
        {
            await Prompts.MessageAsync(this, "Couldn't save the automation", ex.Message);
        }
    }
}
