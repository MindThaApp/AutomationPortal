using System.Windows;
using System.Windows.Controls;
using AutomationPortal.Core;
using AutomationPortal.Editor.UI;
using AutomationPortal.Editor.ViewModels;
using Wpf.Ui.Controls;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfCheckBox = System.Windows.Controls.CheckBox;

namespace AutomationPortal.Editor;

/// <summary>The "Details" sheet: basics (name, Quick Run name, description, where it shows up) and a collapsed Advanced part (input, import questions, export).</summary>
internal sealed class DetailsSheet : FluentWindow
{
    private readonly EditorViewModel _vm;
    private readonly StackPanel _questions = new();

    public DetailsSheet(EditorViewModel vm)
    {
        _vm = vm;
        Title = Strings.Get("DetailsSheet_ShortcutDetails");
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
        root.Children.Add(new TitleBar { Title = Strings.Get("DetailsSheet_ShortcutDetails"), ShowMaximize = false, ShowMinimize = false });

        var stack = new StackPanel { Margin = new Thickness(28, 6, 28, 12) };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var s = vm.Shortcut;

        // ---- general ----------------------------------------------------------------------
        stack.Children.Add(Heading("Basics"));
        var nameError = Ui.Text("", Ui.Critical, 12, wrap: true);
        nameError.Visibility = Visibility.Collapsed;
        var name = new Wpf.Ui.Controls.TextBox { Text = s.Name, PlaceholderText = Strings.Get("DetailsSheet_ShortcutName") };
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

        var alias = new Wpf.Ui.Controls.TextBox { Text = s.Alias ?? "", PlaceholderText = Strings.Get("DetailsSheet_ForExampleGm") };
        alias.TextChanged += (_, _) =>
        {
            var v = alias.Text.Trim();
            s.Alias = v.Length == 0 ? null : v;
            vm.MetadataEdited();
        };
        stack.Children.Add(Field(Strings.Get("DetailsSheet_QuickRunName"), alias, Strings.Get("DetailsSheet_TypeThisInQuick")));

        var desc = new Wpf.Ui.Controls.TextBox { Text = s.Description ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70, PlaceholderText = Strings.Get("DetailsSheet_WhatDoesThisShortcut") };
        desc.TextChanged += (_, _) => { s.Description = string.IsNullOrWhiteSpace(desc.Text) ? null : desc.Text; vm.MetadataEdited(); };
        stack.Children.Add(Field("Description", desc));

        // ---- where it shows up -------------------------------------------------------------
        stack.Children.Add(Heading(Strings.Get("DetailsSheet_WhereItShowsUp")));
        stack.Children.Add(Toggle(Strings.Get("DetailsSheet_PinIt"), Strings.Get("DetailsSheet_ShowItInThe"), s.Pinned, v => { s.Pinned = v; vm.MetadataEdited(); }));
        stack.Children.Add(Toggle(Strings.Get("DetailsSheet_ShowInQuickRun"), Strings.Get("DetailsSheet_FindItByName"), s.ShowInQuickRun, v => { s.ShowInQuickRun = v; vm.MetadataEdited(); }));
        stack.Children.Add(Toggle("Show in \"Send to\" and the right-click menu", Strings.Get("DetailsSheet_RunItOnFiles"), s.ShowInSendTo, v => { s.ShowInSendTo = v; vm.MetadataEdited(); }));
        // ---- advanced (collapsed) ----------------------------------------------------------
        var advanced = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        stack.Children.Add(new Expander { Header = "Advanced", Content = advanced, IsExpanded = false, Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch });
        stack.Children.Add(Ui.Text("The hotkey and automations are in the \"When it runs\" card at the top of the editor.", Ui.Tertiary, 12, wrap: true));

        // ---- input -------------------------------------------------------------------------
        advanced.Children.Add(Heading(Strings.Get("DetailsSheet_WhatItCanTake")));
        advanced.Children.Add(Ui.Text(Strings.Get("DetailsSheet_TickWhatThisShortcut"), Ui.Secondary, 12.5, wrap: true));
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
        advanced.Children.Add(kindPanel);
        var options = new (string Label, WhenNoInput Value)[]
        {
            (Strings.Get("DetailsSheet_CarryOnWithoutIt"), WhenNoInput.ContinueWithNothing), (Strings.Get("DetailsSheet_AskMeToChoose"), WhenNoInput.AskForFiles),
            (Strings.Get("DetailsSheet_UseWhatsOnThe"), WhenNoInput.UseClipboard), (Strings.Get("DetailsSheet_StopAndTellMe"), WhenNoInput.StopAndSay),
        };
        noInput = new WpfComboBox { ItemsSource = options.Select(o => o.Label).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(options, o => o.Value == s.WhenNoInput)), IsEnabled = s.AcceptsInput.Count > 0 };
        noInput.SelectionChanged += (_, _) => { if (noInput.SelectedIndex >= 0) { s.WhenNoInput = options[noInput.SelectedIndex].Value; vm.MetadataEdited(); } };
        advanced.Children.Add(Field(Strings.Get("DetailsSheet_IfTheresNoInput"), noInput));

        // ---- import questions --------------------------------------------------------------
        advanced.Children.Add(Heading(Strings.Get("DetailsSheet_QuestionsWhenSomeoneAdds")));
        advanced.Children.Add(Ui.Text(Strings.Get("DetailsSheet_WhenThisShortcutIs"), Ui.Secondary, 12.5, wrap: true));
        advanced.Children.Add(_questions);
        var addQ = Ui.TextButton(Strings.Get("DetailsSheet_AddQuestion"), () =>
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
        advanced.Children.Add(addQ);
        RefreshQuestions();

        // ---- share ---------------------------------------------------------------------------
        advanced.Children.Add(Heading(Strings.Get("DetailsSheet_RunItFromElsewhere")));
        advanced.Children.Add(Ui.Text(Strings.Get("DetailsSheet_StartThisShortcutFrom"), Ui.Secondary, 12.5, wrap: true));
        advanced.Children.Add(CopyRow("Command", RunFromElsewhere.Command(s.Name), Strings.Get("DetailsSheet_CopyCommand"), Strings.Get("DetailsSheet_TypeThisInA")));
        advanced.Children.Add(CopyRow("Link", RunFromElsewhere.Link(s.Name), Strings.Get("DetailsSheet_CopyLink"), Strings.Get("DetailsSheet_PasteItInA")));
        // Kept in the app's settings, not in the shortcut, so a shared or imported shortcut never brings it along.
        var host = vm.Context.Host;
        var id = s.Id;
        if (host.LinksMode(id) is { } linkMode)
        {
            var choices = new[] { "Never run it", "Ask me every time", "Run it without asking" };
            var current = linkMode == LinkMode.Never ? 0 : linkMode == LinkMode.PerShortcut && host.LinksRunWithoutAsking(id) ? 2 : 1;
            var linkBox = new WpfComboBox { ItemsSource = choices, SelectedIndex = current };
            System.Windows.Automation.AutomationProperties.SetName(linkBox, "When a link wants to run this shortcut");
            linkBox.SelectionChanged += (_, _) =>
            {
                switch (linkBox.SelectedIndex)
                {
                    case 0: host.SetLinksMode(id, LinkMode.Never); host.SetLinksRunWithoutAsking(id, false); break;
                    case 1: host.SetLinksMode(id, LinkMode.Ask); host.SetLinksRunWithoutAsking(id, false); break;
                    case 2: host.SetLinksMode(id, LinkMode.PerShortcut); host.SetLinksRunWithoutAsking(id, true); break;
                }
            };
            advanced.Children.Add(Field("When an automationportal:// link wants to run this shortcut", linkBox));
            advanced.Children.Add(Ui.Text("Links that hand over files or a file path always ask first. New shortcuts start with the choice in Settings › Links.", Ui.Tertiary, 12, wrap: true));
        }
        else
            advanced.Children.Add(Ui.Text(Strings.Get("DetailsSheet_LinksFollowTheSetting"), Ui.Tertiary, 12, wrap: true));
        var share = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        share.Children.Add(HostButton(Strings.Get("DetailsSheet_AddToDesktop"), SymbolRegular.Desktop24, () => vm.Context.Host.AddToDesktop(s)));
        share.Children.Add(HostButton(Strings.Get("DetailsSheet_AddToStartMenu"), SymbolRegular.Apps24, () => vm.Context.Host.AddToStartMenu(s)));
        share.Children.Add(HostButton("Export…", SymbolRegular.ArrowExportLtr24, () => vm.Context.Host.Export(s)));
        advanced.Children.Add(share);

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

    /// <summary>A read-only line of text with a button that copies it, for the command and the link.</summary>
    private static FrameworkElement CopyRow(string label, string text, string buttonText, string hint)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new Wpf.Ui.Controls.TextBox { Text = text, IsReadOnly = true };
        System.Windows.Automation.AutomationProperties.SetName(box, label);
        var copy = new Wpf.Ui.Controls.Button { Content = buttonText, Margin = new Thickness(8, 0, 0, 0) };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(text); copy.Content = "Copied"; }
            catch (System.Runtime.InteropServices.COMException) { copy.Content = Strings.Get("DetailsSheet_CouldntCopy"); }
        };
        Grid.SetColumn(copy, 1);
        grid.Children.Add(box);
        grid.Children.Add(copy);
        var holder = new StackPanel();
        holder.Children.Add(grid);
        return Field(label, holder, hint);
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

    private void RefreshQuestions()
    {
        _questions.Children.Clear();
        var s = _vm.Shortcut;
        foreach (var q in s.ImportQuestions.ToList())
        {
            var card = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 8, 0, 0), BorderThickness = new Thickness(1) };
            card.Dyn(Border.BorderBrushProperty, Ui.CardStroke).Dyn(Border.BackgroundProperty, Ui.CardFill);
            var stack = new StackPanel();
            var text = new Wpf.Ui.Controls.TextBox { Text = q.Question, PlaceholderText = Strings.Get("DetailsSheet_TheQuestionForExample") };
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
            var remove = Ui.IconButton(SymbolRegular.Delete16, Strings.Get("DetailsSheet_RemoveThisQuestion"), () =>
            {
                s.ImportQuestions.Remove(q);
                _vm.MetadataEdited();
                RefreshQuestions();
            }, 30, 14);
            remove.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);
            stack.Children.Add(row);

            var def = new Wpf.Ui.Controls.TextBox { Text = q.DefaultValue?.ToLiteralText() ?? "", PlaceholderText = Strings.Get("DetailsSheet_SuggestedAnswerOptional"), Margin = new Thickness(0, 8, 0, 0) };
            def.TextChanged += (_, _) => { q.DefaultValue = string.IsNullOrEmpty(def.Text) ? null : ParamValue.Text(def.Text); _vm.MetadataEdited(); };
            stack.Children.Add(def);
            card.Child = stack;
            _questions.Children.Add(card);
        }
    }
}
