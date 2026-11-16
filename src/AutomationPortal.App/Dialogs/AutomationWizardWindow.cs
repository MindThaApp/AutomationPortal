using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Integration;
using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Dialogs;

/// <summary>
/// The New Automation sheet: one sentence, "When … run …". The first blank opens on a Popular row of the most common
/// triggers, with "More…" for the full grouped list. Choosing a card fills the sentence; there is no Next button.
/// Options (conditions, ask first, notification, pauses) sit in a collapsed row. Also edits an existing automation.
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

    private readonly AppHost _host;
    private readonly AutomationWizardState _state;
    private readonly Automation? _editing;
    private readonly TaskCompletionSource<bool> _closed = new();
    private bool _saved;

    private readonly ContentControl _body = new();
    private readonly ContentControl _sentenceHost = new();
    private readonly TextBlock _message;
    private readonly Wpf.Ui.Controls.Button _turnOn;
    private string _category = "";
    private string _search = "";
    private bool _showAll;
    private bool _optionsOpen;
    private readonly List<IParameterForm> _forms = new();

    private AutomationWizardWindow(AppHost host, Automation? editing, Guid? presetShortcutId, string? presetTriggerId = null, IReadOnlyDictionary<string, ParamValue>? presetValues = null)
    {
        _host = host;
        _editing = editing;
        _state = editing is null ? new AutomationWizardState() : AutomationWizardState.FromAutomation(editing);
        if (presetShortcutId is { } id) _state.ExistingShortcutId = id;
        if (editing is null && presetTriggerId is not null && host.Triggers.Get(presetTriggerId) is not null)
        {
            _state.ChooseTrigger(presetTriggerId);
            foreach (var (key, value) in presetValues ?? new Dictionary<string, ParamValue>()) _state.TriggerValues[key] = value;
        }
        if (_state.TriggerId is { } tid) _category = host.Triggers.Get(tid)?.Descriptor.Category ?? "";
        if (_category.Length == 0) _category = Categories().FirstOrDefault() ?? "";
        if (_host.Library.All.Count == 0) _state.CreateNewShortcut = true;
        else if (editing is null && _state.ExistingShortcutId is null && _host.Library.All.Count == 1) _state.ExistingShortcutId = _host.Library.All[0].Id;

        Title = editing is null ? "New automation" : "Edit automation";
        Width = 640; Height = 700; MinWidth = 560; MinHeight = 520;
        ShowInTaskbar = false;
        WindowBackdropType = WindowBackdropType.Mica;
        ExtendsContentIntoTitleBar = true;
        UseLayoutRounding = true;
        SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        FontFamily = (FontFamily)Application.Current.FindResource("FlowFont");

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TitleBar { Title = Title, ShowMinimize = false, ShowMaximize = false, CanMaximize = false });

        var heading = new TextBlock { Text = Title, FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        heading.FontFamily = (FontFamily)Application.Current.FindResource("FlowDisplayFont");
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 14, 0) };
        var content = new StackPanel { Margin = new Thickness(28, 36, 14, 12) };
        content.Children.Add(heading);
        content.Children.Add(_body);
        scroll.Content = content;
        Grid.SetRow(scroll, 1);
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
        _turnOn = new Wpf.Ui.Controls.Button
        {
            Content = editing is null ? "Turn on" : "Save", MinWidth = 110, Margin = new Thickness(8, 0, 0, 0),
            Appearance = ControlAppearance.Primary, IsDefault = true, Icon = new SymbolIcon(SymbolRegular.Checkmark20),
        };
        _turnOn.Click += async (_, _) => await SaveAsync();
        buttons.Children.Add(cancel);
        buttons.Children.Add(_turnOn);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
        Closed += (_, _) => _closed.TrySetResult(_saved);
        Rebuild();
    }

    /// <summary>Opens the sheet. Completes with true when an automation was saved.</summary>
    /// <param name="presetTriggerId">Start with this trigger chosen and its options filled in.</param>
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

    private void Rebuild()
    {
        _forms.Clear();
        var root = new StackPanel();
        _sentenceHost.Content = BuildSentence();
        root.Children.Add(_sentenceHost);
        root.Children.Add(BuildWhenSection());
        root.Children.Add(BuildRunSection());
        if (_state.TriggerId is not null) root.Children.Add(BuildOptionsRow());
        _body.Content = root;
        RefreshFooter();
    }

    private void RefreshFooter()
    {
        var block = _state.ReadyMessage(_host.Triggers, _host.Conditions, _host.Library);
        _message.Text = block ?? "";
        _turnOn.IsEnabled = block is null;
    }

    private static TextBlock SectionLabel(string text)
    {
        var t = Ui.Text(text, size: 14, weight: FontWeights.SemiBold);
        t.Margin = new Thickness(0, 18, 0, 8);
        return t;
    }

    // ---- the sentence ----------------------------------------------------------------------

    private FrameworkElement BuildSentence()
    {
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 17, Margin = new Thickness(0, 0, 0, 4) };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        string whenText = "";
        if (_state.TriggerId is { } id) whenText = WhenSummary.Text(id, _state.TriggerValues, _host.Triggers);
        var runText = _state.CreateNewShortcut
            ? (string.IsNullOrWhiteSpace(_state.NewShortcutName) ? "a new shortcut" : _state.NewShortcutName.Trim())
            : _host.Library.All.FirstOrDefault(s => s.Id == _state.ExistingShortcutId)?.Name ?? "";
        void Token(string text, string placeholder)
        {
            var empty = text.Length == 0;
            var run = new System.Windows.Documents.Run(empty ? placeholder : text) { FontWeight = FontWeights.SemiBold };
            if (empty) run.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextFillColorTertiaryBrush");
            else run.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
            tb.Inlines.Add(run);
        }
        tb.Inlines.Add(new System.Windows.Documents.Run("When: "));
        Token(whenText, "choose what starts it");
        tb.Inlines.Add(new System.Windows.Documents.Run(",  run: "));
        Token(runText, "choose a shortcut");
        return tb;
    }

    // ---- when ------------------------------------------------------------------------------

    private FrameworkElement BuildWhenSection()
    {
        var root = new StackPanel();
        root.Children.Add(SectionLabel("When"));
        var trigger = _state.TriggerId is null ? null : _host.Triggers.Get(_state.TriggerId)?.Descriptor;
        if (trigger is null || _showAll) { root.Children.Add(BuildPicker()); return root; }

        var head = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var chip = Ui.Chip(trigger.Icon, CategoryColors.GetValueOrDefault(trigger.Category, "gray"), 40, 20);
        chip.VerticalAlignment = VerticalAlignment.Top;
        head.Children.Add(chip);
        var titles = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Ui.Text(trigger.Title, weight: FontWeights.SemiBold, size: 15));
        titles.Children.Add(Ui.Text(trigger.Description, secondary: true, size: 12));
        Grid.SetColumn(titles, 1);
        head.Children.Add(titles);
        var change = new Wpf.Ui.Controls.Button { Content = "Change", VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 0, 0, 0) };
        change.Click += (_, _) => { _showAll = false; _state.ClearTrigger(); Rebuild(); };
        AutomationProperties.SetName(change, "Change what starts it");
        Grid.SetColumn(change, 2);
        head.Children.Add(change);
        root.Children.Add(head);

        if (trigger.Parameters.Count > 0)
        {
            var form = _host.ParameterForms.Create(trigger.Parameters, _state.TriggerValues, _host.Services);
            form.Changed += (_, _) => { _sentenceHost.Content = BuildSentence(); RefreshFooter(); };
            _forms.Add(form);
            root.Children.Add(form.Element);
        }
        if (!string.IsNullOrWhiteSpace(trigger.OutputDescription))
            root.Children.Add(Ui.Text("Your shortcut receives: " + trigger.OutputDescription, secondary: true, size: 12));
        return root;
    }

    /// <summary>The Popular row, with "More…" for the full grouped list and search.</summary>
    private FrameworkElement BuildPicker()
    {
        var root = new StackPanel();
        var popular = WhenSummary.AvailablePopular(_host.Triggers);
        var cards = new WrapPanel();
        var more = new StackPanel { Visibility = _showAll ? Visibility.Visible : Visibility.Collapsed };

        if (popular.Count > 0)
        {
            var pop = Ui.Text("Popular", secondary: true, size: 12);
            pop.Margin = new Thickness(0, 0, 0, 6);
            root.Children.Add(pop);
            var row = new WrapPanel();
            foreach (var p in popular)
            {
                var d = _host.Triggers.Get(p.TriggerId)!.Descriptor;
                row.Children.Add(TriggerCard(d, p.Title, p.Hint));
            }
            root.Children.Add(row);
        }

        var moreButton = new Wpf.Ui.Controls.Button { Content = _showAll || popular.Count == 0 ? "All ways to start" : "More…", Icon = new SymbolIcon(SymbolRegular.ChevronDown20), Margin = new Thickness(0, 4, 0, 0) };
        moreButton.Click += (_, _) => { _showAll = true; Rebuild(); };
        if (!_showAll && popular.Count > 0) root.Children.Add(moreButton);
        else if (popular.Count == 0) _showAll = true;
        more.Visibility = _showAll ? Visibility.Visible : Visibility.Collapsed;

        var search = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Search all ways to start", Icon = new SymbolIcon(SymbolRegular.Search20), Text = _search, Margin = new Thickness(0, 14, 0, 12) };
        var pills = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
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
            foreach (var d in list) cards.Children.Add(TriggerCard(d, d.Title, d.Description));
            if (list.Count == 0)
                cards.Children.Add(Ui.Text(searching ? "Nothing matches your search." : "There is nothing in this group yet.", secondary: true));
        }
        search.TextChanged += (_, _) => { _search = search.Text; Fill(); };
        more.Children.Add(search);
        more.Children.Add(pills);
        more.Children.Add(cards);
        root.Children.Add(more);
        if (_showAll) Fill();
        return root;
    }

    private FrameworkElement TriggerCard(TriggerDescriptor d, string title, string hint)
    {
        var button = new System.Windows.Controls.Button { Style = (Style)FindResource("BigCardButton"), Width = 266, Margin = new Thickness(0, 0, 10, 10), MinHeight = 76, VerticalContentAlignment = VerticalAlignment.Top };
        AutomationProperties.SetName(button, title);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var chip = Ui.Chip(d.Icon, CategoryColors.GetValueOrDefault(d.Category, "gray"), 36, 18);
        chip.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(chip);
        var text = new StackPanel { Margin = new Thickness(12, 0, 4, 0) };
        text.Children.Add(Ui.Text(title, weight: FontWeights.SemiBold, size: 14));
        text.Children.Add(Ui.Text(hint, secondary: true, size: 12));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        button.Content = grid;
        button.Click += (_, _) =>
        {
            _state.ChooseTrigger(d.Id);
            _showAll = false;
            Rebuild();
        };
        return button;
    }

    // ---- run -------------------------------------------------------------------------------

    private FrameworkElement BuildRunSection()
    {
        var root = new StackPanel();
        root.Children.Add(SectionLabel("Run"));
        var existing = new RadioButton { Content = "A shortcut I already have", GroupName = "which", IsChecked = !_state.CreateNewShortcut, Margin = new Thickness(0, 0, 0, 6), FontSize = 14 };
        var create = new RadioButton { Content = "A new shortcut (I'll add its steps next)", GroupName = "which", IsChecked = _state.CreateNewShortcut, FontSize = 14 };
        var picker = new System.Windows.Controls.ComboBox { Margin = new Thickness(28, 0, 0, 12), HorizontalAlignment = HorizontalAlignment.Left, Width = 380, DisplayMemberPath = nameof(Shortcut.Name) };
        picker.ItemsSource = _host.Library.All;
        picker.SelectedItem = _host.Library.All.FirstOrDefault(s => s.Id == _state.ExistingShortcutId);
        AutomationProperties.SetName(picker, "Shortcut to run");
        var nameBox = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Name the new shortcut", Text = _state.NewShortcutName, Margin = new Thickness(28, 8, 0, 8), Width = 380, HorizontalAlignment = HorizontalAlignment.Left };

        void Sync()
        {
            _state.CreateNewShortcut = create.IsChecked == true;
            picker.IsEnabled = !_state.CreateNewShortcut;
            nameBox.IsEnabled = _state.CreateNewShortcut;
            nameBox.Visibility = _state.CreateNewShortcut ? Visibility.Visible : Visibility.Collapsed;
            picker.Visibility = _state.CreateNewShortcut ? Visibility.Collapsed : Visibility.Visible;
            RefreshFooter();
        }
        existing.Checked += (_, _) => { Sync(); Rebuild(); };
        create.Checked += (_, _) => { Sync(); Rebuild(); };
        picker.SelectionChanged += (_, _) => { _state.ExistingShortcutId = (picker.SelectedItem as Shortcut)?.Id; Rebuild(); };
        nameBox.TextChanged += (_, _) => { _state.NewShortcutName = nameBox.Text; _sentenceHost.Content = BuildSentence(); RefreshFooter(); };
        if (_host.Library.All.Count == 0) { existing.IsEnabled = false; existing.ToolTip = "You don't have any shortcuts yet."; }

        root.Children.Add(existing);
        root.Children.Add(picker);
        root.Children.Add(create);
        root.Children.Add(nameBox);
        Sync();
        return root;
    }

    // ---- options (collapsed) ---------------------------------------------------------------

    private FrameworkElement BuildOptionsRow()
    {
        var expander = new Expander
        {
            Header = "Options", IsExpanded = _optionsOpen, Margin = new Thickness(0, 22, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(expander, "Options");
        expander.Expanded += (_, _) => _optionsOpen = true;
        expander.Collapsed += (_, _) => _optionsOpen = false;
        var root = new StackPanel { Margin = new Thickness(4, 4, 4, 4) };

        // Only if…
        var onlyIf = Ui.Text("Only if… (optional)", size: 14, weight: FontWeights.SemiBold);
        onlyIf.Margin = new Thickness(0, 4, 0, 4);
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

        // How it runs
        root.Children.Add(Ui.Text("When it happens", size: 14, weight: FontWeights.SemiBold, wrap: false));
        ((TextBlock)root.Children[^1]).Margin = new Thickness(0, 20, 0, 0);
        var now = new RadioButton { Content = "Run it right away", GroupName = "mode", IsChecked = _state.RunMode == RunMode.Immediately, Margin = new Thickness(0, 8, 0, 4) };
        var ask = new RadioButton { Content = "Ask me first (a notification with Run and Skip)", GroupName = "mode", IsChecked = _state.RunMode == RunMode.AskFirst };
        now.Checked += (_, _) => _state.RunMode = RunMode.Immediately;
        ask.Checked += (_, _) => _state.RunMode = RunMode.AskFirst;
        root.Children.Add(now);
        root.Children.Add(ask);

        var notify = new ToggleSwitch { Content = "Tell me when it runs", IsChecked = _state.NotifyWhenRun, Margin = new Thickness(0, 14, 0, 0) };
        notify.Click += (_, _) => _state.NotifyWhenRun = notify.IsChecked == true;
        root.Children.Add(notify);

        var cool = new Grid { Margin = new Thickness(0, 14, 0, 0) };
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

        var frequent = new ToggleSwitch { Content = "Don't pause this automation when it runs often", IsChecked = _state.AllowFrequentRuns, Margin = new Thickness(0, 14, 0, 0) };
        frequent.Click += (_, _) => _state.AllowFrequentRuns = frequent.IsChecked == true;
        root.Children.Add(frequent);
        root.Children.Add(Ui.Text("Normally Automation Portal pauses an automation that runs more than 60 times in 10 minutes, because that usually means a loop.", secondary: true, size: 12));

        if (AutomationWizardState.IsScheduleTrigger(_state.TriggerId))
        {
            var missed = new ToggleSwitch { Content = "If missed, run when Automation Portal next starts or the PC wakes", IsChecked = _state.RunIfMissed, Margin = new Thickness(0, 14, 0, 0) };
            missed.Click += (_, _) => _state.RunIfMissed = missed.IsChecked == true;
            AutomationProperties.SetName(missed, "If missed, run when Automation Portal next starts or the PC wakes");
            root.Children.Add(missed);
            root.Children.Add(Ui.Text("If the PC was off or asleep, or Automation Portal wasn't running, at the scheduled time, it runs once when Automation Portal starts or the PC wakes. Only times from the last 24 hours count, and nothing runs for a time that passed while automations were paused.", secondary: true, size: 12));
        }
        expander.Content = root;
        return expander;
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
