using System.Windows;
using System.Windows.Controls;
using AutomationPortal.Editor.UI;
using Wpf.Ui.Controls;

namespace AutomationPortal.Editor;

/// <summary>The "When it runs" card at the top of the editor: the hotkey and any automations, with add and edit in place.</summary>
internal sealed partial class ShortcutEditorWindow
{
    private bool _addingHotkey;

    private void InitWhenCard()
    {
        RefreshWhen();
        Activated += (_, _) => RefreshWhen();
    }

    private void RefreshWhen()
    {
        var s = _vm.Shortcut;
        IReadOnlyList<AutomationSummary> automations;
        try { automations = _vm.Context.Host.GetAutomationsFor(s.Id); } catch { automations = Array.Empty<AutomationSummary>(); }

        var card = new Border { CornerRadius = Radius.Card, Padding = new Thickness(14, 10, 10, 10), BorderThickness = new Thickness(1) };
        card.Dyn(Border.BorderBrushProperty, Ui.CardStroke).Dyn(Border.BackgroundProperty, Ui.CardFill);
        System.Windows.Automation.AutomationProperties.SetName(card, Strings.Get("ShortcutEditorWindowWhen_WhenItRuns"));
        var stack = new StackPanel();
        card.Child = stack;

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(Ui.Text(Strings.Get("ShortcutEditorWindow_WhenItRuns"), Ui.Primary, 14, FontWeights.SemiBold));
        var plain = string.IsNullOrWhiteSpace(s.Hotkey) && automations.Count == 0;
        if (plain) title.Children.Add(Ui.Text(Strings.Get("ShortcutEditorWindow_WhenYouClickIt"), Ui.Secondary, 12.5));
        head.Children.Add(title);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        if (string.IsNullOrWhiteSpace(s.Hotkey) && !_addingHotkey)
            buttons.Children.Add(Ui.TextButton(Strings.Get("ShortcutEditorWindow_AddHotkey"), () => { _addingHotkey = true; RefreshWhen(); }, SymbolRegular.Keyboard24));
        var add = Ui.TextButton(Strings.Get("ShortcutEditorWindow_AddAutomation"), () =>
        {
            _vm.SaveNow();
            _vm.Context.Host.AddTrigger(s.Id);
        }, SymbolRegular.Add16);
        add.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(add);
        Grid.SetColumn(buttons, 1);
        head.Children.Add(buttons);
        stack.Children.Add(head);

        if (!string.IsNullOrWhiteSpace(s.Hotkey) || _addingHotkey)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            row.Children.Add(Ui.Text(Strings.Get("ShortcutEditorWindowWhen_Hotkey"), Ui.Secondary, 13));
            ((TextBlock)row.Children[0]).VerticalAlignment = VerticalAlignment.Center;
            ((TextBlock)row.Children[0]).Margin = new Thickness(0, 0, 12, 0);
            var recorder = new HotkeyRecorder { Value = s.Hotkey };
            var problem = Ui.Text("", Ui.Critical, 12, wrap: true);
            problem.Margin = new Thickness(0, 4, 0, 0);
            problem.Visibility = Visibility.Collapsed;
            void Check()
            {
                string? p = null;
                try { if (!string.IsNullOrWhiteSpace(s.Hotkey)) p = _vm.Context.Host.HotkeyProblem(s.Hotkey!, s.Id); } catch { }
                problem.Text = p ?? "";
                problem.Visibility = p is null ? Visibility.Collapsed : Visibility.Visible;
            }
            recorder.ValueChanged += v =>
            {
                s.Hotkey = v;
                _vm.MetadataEdited();
                Check();
                // The app registers the keys a moment after the shortcut is saved; look again then.
                var again = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                again.Tick += (_, _) => { again.Stop(); Check(); };
                again.Start();
            };
            row.Children.Add(recorder);
            stack.Children.Add(row);
            stack.Children.Add(problem);
            Check();
            if (_addingHotkey) { _addingHotkey = false; recorder.Dispatcher.BeginInvoke(new Action(() => recorder.Focus()), System.Windows.Threading.DispatcherPriority.Input); }
        }

        foreach (var a in automations)
        {
            var g = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = Ui.Text(a.Sentence, Ui.Primary, 13.5, wrap: true);
            text.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(text);
            var state = Ui.Text(a.Enabled ? Strings.Get("ShortcutEditorWindowWhen_On") : Strings.Get("ShortcutEditorWindowWhen_Off"), a.Enabled ? Ui.Success : Ui.Tertiary, 12);
            state.Margin = new Thickness(10, 0, 10, 0);
            state.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(state, 1);
            g.Children.Add(state);
            var edit = Ui.TextButton(Strings.Get("ShortcutEditorWindowWhen_Edit"), () => _vm.Context.Host.EditAutomation(a.Id));
            Grid.SetColumn(edit, 2);
            g.Children.Add(edit);
            stack.Children.Add(g);
            if (a.NextRun is { } nextRun)
            {
                var line = Ui.Text(nextRun, Ui.Tertiary, 12);
                line.Margin = new Thickness(0, 2, 0, 0);
                stack.Children.Add(line);
            }
        }
        if (automations.Count > 0 || !string.IsNullOrWhiteSpace(s.Hotkey))
        {
            var test = Ui.TextButton(Strings.Get("ShortcutEditorWindow_TestItNow"), () => { if (!_vm.IsRunning) _ = _vm.RunAsync(); }, SymbolRegular.Play24);
            test.HorizontalAlignment = HorizontalAlignment.Left;
            test.Margin = new Thickness(0, 8, 0, 0);
            System.Windows.Automation.AutomationProperties.SetName(test, Strings.Get("ShortcutEditorWindowWhen_TestItNowRun"));
            stack.Children.Add(test);
        }
        WhenHost.Content = card;
    }
}
