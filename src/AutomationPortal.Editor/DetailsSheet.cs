using System.Windows;
using System.Windows.Controls;
using AutomationPortal.Core;
using AutomationPortal.Editor.UI;
using AutomationPortal.Editor.ViewModels;
using Wpf.Ui.Controls;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfCheckBox = System.Windows.Controls.CheckBox;

namespace AutomationPortal.Editor;

/// <summary>The "Details" sheet: name, alias, hotkey, where it shows up, input, triggers, import questions, export.</summary>
internal sealed class DetailsSheet : FluentWindow
{
    private readonly EditorViewModel _vm;
    private readonly StackPanel _triggers = new();
    private readonly StackPanel _questions = new();

    public DetailsSheet(EditorViewModel vm)
    {
        _vm = vm;
        Title = "Shortcut details";
        Width = 560;
        Height = 760;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowBackdropType = WindowBackdropType.Mica;
        ExtendsContentIntoTitleBar = true;
        ShowInTaskbar = false;
        FontFamily = Ui.Font;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TitleBar { Title = "Shortcut details", ShowMaximize = false, ShowMinimize = false });

        var stack = new StackPanel { Margin = new Thickness(28, 6, 28, 12) };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var s = vm.Shortcut;

        // ---- general ----------------------------------------------------------------------
        stack.Children.Add(Heading("General"));
        var nameError = Ui.Text("", Ui.Critical, 12, wrap: true);
        nameError.Visibility = Visibility.Collapsed;
        var name = new Wpf.Ui.Controls.TextBox { Text = s.Name, PlaceholderText = "Shortcut name" };
        void CommitName()
        {
            var err = vm.TryRename(name.Text);
            nameError.Text = err ?? "";
            nameError.Visibility = err is null ? Visibility.Collapsed : Visibility.Visible;
            if (err is null) name.Text = vm.Shortcut.Name;
        }
        name.LostFocus += (_, _) => CommitName();
        name.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) CommitName(); };
        stack.Children.Add(Field("Name", name));
        stack.Children.Add(nameError);

        var alias = new Wpf.Ui.Controls.TextBox { Text = s.Alias ?? "", PlaceholderText = "For example gm" };
        alias.TextChanged += (_, _) =>
        {
            var v = alias.Text.Trim();
            s.Alias = v.Length == 0 ? null : v;
            vm.MetadataEdited();
        };
        stack.Children.Add(Field("Quick Run name", alias, "Type this in Quick Run to start the shortcut quickly."));

        var desc = new Wpf.Ui.Controls.TextBox { Text = s.Description ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70, PlaceholderText = "What does this shortcut do?" };
        desc.TextChanged += (_, _) => { s.Description = string.IsNullOrWhiteSpace(desc.Text) ? null : desc.Text; vm.MetadataEdited(); };
        stack.Children.Add(Field("Description", desc));

        var hotkey = new HotkeyRecorder { Value = s.Hotkey };
        var hotkeyProblem = Ui.Text("", Ui.Critical, 12, wrap: true);
        hotkeyProblem.Margin = new Thickness(0, 4, 0, 0);
        hotkeyProblem.Visibility = Visibility.Collapsed;
        void CheckHotkey()
        {
            string? problem = null;
            try { if (!string.IsNullOrWhiteSpace(s.Hotkey)) problem = vm.Context.Host.HotkeyProblem(s.Hotkey!, s.Id); } catch { }
            hotkeyProblem.Text = problem ?? "";
            hotkeyProblem.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
        }
        hotkey.ValueChanged += v =>
        {
            s.Hotkey = v;
            vm.MetadataEdited();
            CheckHotkey();
            // The app registers the keys a moment after the shortcut is saved; look again then.
            var again = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            again.Tick += (_, _) => { again.Stop(); CheckHotkey(); };
            again.Start();
        };
        var hotkeyField = Field("Hotkey", hotkey, "Press this anywhere in Windows to run the shortcut.");
        ((StackPanel)hotkeyField).Children.Add(hotkeyProblem);
        stack.Children.Add(hotkeyField);
        CheckHotkey();

        // ---- where it shows up -------------------------------------------------------------
        stack.Children.Add(Heading("Where it shows up"));
        stack.Children.Add(Toggle("Pin it", "Show it in the tray menu and the Quick Panel.", s.Pinned, v => { s.Pinned = v; vm.MetadataEdited(); }));
        stack.Children.Add(Toggle("Show in Quick Run", "Find it by name when you open Quick Run.", s.ShowInQuickRun, v => { s.ShowInQuickRun = v; vm.MetadataEdited(); }));
        stack.Children.Add(Toggle("Show in \"Send to\" and the right-click menu", "Run it on files you pick in File Explorer.", s.ShowInSendTo, v => { s.ShowInSendTo = v; vm.MetadataEdited(); }));

        // ---- input -------------------------------------------------------------------------
        stack.Children.Add(Heading("What it can take as input"));
        stack.Children.Add(Ui.Text("Tick what this shortcut can work with. Leave all unticked if it doesn't need anything.", Ui.Secondary, 12.5, wrap: true));
        var kinds = new[] { ("Files", ContentKind.File), ("Images", ContentKind.Image), ("Text", ContentKind.Text), ("Links", ContentKind.Url), ("Folders", ContentKind.Folder) };
        var kindPanel = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        WpfComboBox? noInput = null;
        foreach (var (label, kind) in kinds)
        {
            var cb = new WpfCheckBox { Content = label, IsChecked = s.AcceptsInput.Contains(kind), Margin = new Thickness(0, 0, 18, 0) };
            void Toggle(bool on)
            {
                if (on && !s.AcceptsInput.Contains(kind)) s.AcceptsInput.Add(kind);
                if (!on) s.AcceptsInput.Remove(kind);
                if (noInput is not null) noInput.IsEnabled = s.AcceptsInput.Count > 0;
                vm.MetadataEdited();
            }
            cb.Checked += (_, _) => Toggle(true);
            cb.Unchecked += (_, _) => Toggle(false);
            kindPanel.Children.Add(cb);
        }
        stack.Children.Add(kindPanel);
        var options = new (string Label, WhenNoInput Value)[]
        {
            ("Carry on without it", WhenNoInput.ContinueWithNothing), ("Ask me to choose files", WhenNoInput.AskForFiles),
            ("Use what's on the clipboard", WhenNoInput.UseClipboard), ("Stop and tell me", WhenNoInput.StopAndSay),
        };
        noInput = new WpfComboBox { ItemsSource = options.Select(o => o.Label).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(options, o => o.Value == s.WhenNoInput)), IsEnabled = s.AcceptsInput.Count > 0 };
        noInput.SelectionChanged += (_, _) => { if (noInput.SelectedIndex >= 0) { s.WhenNoInput = options[noInput.SelectedIndex].Value; vm.MetadataEdited(); } };
        stack.Children.Add(Field("If there's no input", noInput));

        // ---- triggers ----------------------------------------------------------------------
        stack.Children.Add(Heading("Automations"));
        stack.Children.Add(Ui.Text("Run this shortcut automatically when something happens.", Ui.Secondary, 12.5, wrap: true));
        stack.Children.Add(_triggers);
        var addTrigger = Ui.TextButton("Add automation", () =>
        {
            vm.SaveNow();
            vm.Context.Host.AddTrigger(s.Id);
            RefreshTriggers();
        }, SymbolRegular.Add16);
        addTrigger.Margin = new Thickness(0, 8, 0, 0);
        addTrigger.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(addTrigger);
        RefreshTriggers();
        Activated += (_, _) => RefreshTriggers();

        // ---- import questions --------------------------------------------------------------
        stack.Children.Add(Heading("Questions when someone adds it"));
        stack.Children.Add(Ui.Text("When this shortcut is imported or taken from the gallery, these questions are asked and the answers fill in the actions.", Ui.Secondary, 12.5, wrap: true));
        stack.Children.Add(_questions);
        var addQ = Ui.TextButton("Add question", () =>
        {
            var firstWithParams = s.Actions.FirstOrDefault(a => vm.Context.Actions.Describe(a.ActionId) is { Parameters.Count: > 0 });
            if (firstWithParams is null) return;
            var d = vm.Context.Actions.Describe(firstWithParams.ActionId)!;
            s.ImportQuestions.Add(new ImportQuestion { ActionUid = firstWithParams.Uid, ParameterKey = d.Parameters[0].Key, Question = "" });
            vm.MetadataEdited();
            RefreshQuestions();
        }, SymbolRegular.Add16);
        addQ.Margin = new Thickness(0, 8, 0, 0);
        addQ.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(addQ);
        RefreshQuestions();

        // ---- share ---------------------------------------------------------------------------
        stack.Children.Add(Heading("Share and add"));
        var share = new WrapPanel();
        share.Children.Add(HostButton("Add to desktop", SymbolRegular.Desktop24, () => vm.Context.Host.AddToDesktop(s)));
        share.Children.Add(HostButton("Add to Start menu", SymbolRegular.Apps24, () => vm.Context.Host.AddToStartMenu(s)));
        share.Children.Add(HostButton("Export…", SymbolRegular.ArrowExportLtr24, () => vm.Context.Host.Export(s)));
        stack.Children.Add(share);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(28, 8, 28, 18) };
        var done = Ui.TextButton("Done", () => Close(), null, ControlAppearance.Primary);
        done.IsDefault = true;
        done.IsCancel = true;
        footer.Children.Add(done);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;
        Closing += (_, _) => { CommitName(); _vm.SaveNow(); };
    }

    private static TextBlock Heading(string text)
    {
        var t = Ui.Text(text, Ui.Primary, 16, FontWeights.SemiBold);
        t.Margin = new Thickness(0, 22, 0, 8);
        return t;
    }

    private static FrameworkElement Field(string label, FrameworkElement editor, string? hint = null)
    {
        var s = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        var l = Ui.Text(label, Ui.Secondary, 12, FontWeights.SemiBold);
        l.Margin = new Thickness(0, 0, 0, 4);
        s.Children.Add(l);
        if (editor is HotkeyRecorder or WpfComboBox) editor.HorizontalAlignment = HorizontalAlignment.Left;
        s.Children.Add(editor);
        if (hint is not null)
        {
            var h = Ui.Text(hint, Ui.Tertiary, 12, wrap: true);
            h.Margin = new Thickness(0, 4, 0, 0);
            s.Children.Add(h);
        }
        return s;
    }

    private static FrameworkElement Toggle(string title, string hint, bool value, Action<bool> set)
    {
        var g = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Ui.Text(title, Ui.Primary, 14, wrap: true));
        texts.Children.Add(Ui.Text(hint, Ui.Secondary, 12, wrap: true));
        g.Children.Add(texts);
        var sw = new ToggleSwitch { IsChecked = value, Margin = new Thickness(12, 0, 0, 0) };
        sw.Checked += (_, _) => set(true);
        sw.Unchecked += (_, _) => set(false);
        System.Windows.Automation.AutomationProperties.SetName(sw, title);
        Grid.SetColumn(sw, 1);
        g.Children.Add(sw);
        return g;
    }

    private Wpf.Ui.Controls.Button HostButton(string text, SymbolRegular icon, Action act)
    {
        var b = Ui.TextButton(text, () => { _vm.SaveNow(); act(); }, icon);
        b.Margin = new Thickness(0, 0, 8, 8);
        return b;
    }

    private void RefreshTriggers()
    {
        _triggers.Children.Clear();
        IReadOnlyList<AutomationSummary> list;
        try { list = _vm.Context.Host.GetAutomationsFor(_vm.Shortcut.Id); } catch { list = Array.Empty<AutomationSummary>(); }
        if (list.Count == 0)
        {
            var t = Ui.Text("No triggers yet.", Ui.Tertiary, 13);
            t.Margin = new Thickness(0, 8, 0, 0);
            _triggers.Children.Add(t);
            return;
        }
        foreach (var a in list)
        {
            var card = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 8, 8), Margin = new Thickness(0, 8, 0, 0), BorderThickness = new Thickness(1) };
            card.Dyn(Border.BorderBrushProperty, Ui.CardStroke).Dyn(Border.BackgroundProperty, Ui.CardFill);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(Ui.Text(a.Sentence, Ui.Primary, 14, wrap: true));
            var state = Ui.Text(a.Enabled ? "On" : "Off", a.Enabled ? Ui.Success : Ui.Tertiary, 12);
            state.Margin = new Thickness(10, 0, 10, 0);
            Grid.SetColumn(state, 1);
            g.Children.Add(state);
            var edit = Ui.TextButton("Edit", () => _vm.Context.Host.EditAutomation(a.Id));
            Grid.SetColumn(edit, 2);
            g.Children.Add(edit);
            card.Child = g;
            _triggers.Children.Add(card);
        }
    }

    private void RefreshQuestions()
    {
        _questions.Children.Clear();
        var s = _vm.Shortcut;
        foreach (var q in s.ImportQuestions.ToList())
        {
            var card = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 8, 0, 0), BorderThickness = new Thickness(1) };
            card.Dyn(Border.BorderBrushProperty, Ui.CardStroke).Dyn(Border.BackgroundProperty, Ui.CardFill);
            var stack = new StackPanel();
            var text = new Wpf.Ui.Controls.TextBox { Text = q.Question, PlaceholderText = "The question, for example: Which folder should it tidy?" };
            text.TextChanged += (_, _) => { q.Question = text.Text; _vm.MetadataEdited(); };
            stack.Children.Add(text);

            var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var actions = s.Actions.Select((a, i) => (a, i, d: _vm.Context.Actions.Describe(a.ActionId))).Where(x => x.d is { Parameters.Count: > 0 }).ToList();
            var actionBox = new WpfComboBox { ItemsSource = actions.Select(x => $"{x.i + 1}. {x.d!.Title}").ToList(), SelectedIndex = actions.FindIndex(x => x.a.Uid == q.ActionUid) };
            var paramBox = new WpfComboBox { Margin = new Thickness(8, 0, 0, 0) };
            void FillParams()
            {
                var cur = actions.FirstOrDefault(x => x.a.Uid == q.ActionUid);
                var ps = cur.d?.Parameters ?? Array.Empty<ParameterDescriptor>();
                paramBox.ItemsSource = ps.Select(p => p.Label).ToList();
                paramBox.SelectedIndex = Math.Max(0, ps.ToList().FindIndex(p => p.Key == q.ParameterKey));
            }
            FillParams();
            actionBox.SelectionChanged += (_, _) =>
            {
                if (actionBox.SelectedIndex < 0) return;
                var pick = actions[actionBox.SelectedIndex];
                if (pick.a.Uid == q.ActionUid) return;
                q.ActionUid = pick.a.Uid;
                q.ParameterKey = pick.d!.Parameters[0].Key;
                FillParams();
                _vm.MetadataEdited();
            };
            paramBox.SelectionChanged += (_, _) =>
            {
                var cur = actions.FirstOrDefault(x => x.a.Uid == q.ActionUid);
                if (paramBox.SelectedIndex < 0 || cur.d is null) return;
                q.ParameterKey = cur.d.Parameters[paramBox.SelectedIndex].Key;
                _vm.MetadataEdited();
            };
            row.Children.Add(actionBox);
            Grid.SetColumn(paramBox, 1);
            row.Children.Add(paramBox);
            var remove = Ui.IconButton(SymbolRegular.Delete16, "Remove this question", () =>
            {
                s.ImportQuestions.Remove(q);
                _vm.MetadataEdited();
                RefreshQuestions();
            }, 30, 14);
            remove.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);
            stack.Children.Add(row);

            var def = new Wpf.Ui.Controls.TextBox { Text = q.DefaultValue?.ToLiteralText() ?? "", PlaceholderText = "Suggested answer (optional)", Margin = new Thickness(0, 8, 0, 0) };
            def.TextChanged += (_, _) => { q.DefaultValue = string.IsNullOrEmpty(def.Text) ? null : ParamValue.Text(def.Text); _vm.MetadataEdited(); };
            stack.Children.Add(def);
            card.Child = stack;
            _questions.Children.Add(card);
        }
    }
}
