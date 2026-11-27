using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutomationPortal.Core;
using AutomationPortal.Editor.UI;
using Wpf.Ui.Controls;

namespace AutomationPortal.Editor;

/// <summary>"Describe it", "Describe a change", the note above the steps, and recording a macro into the shortcut.</summary>
internal sealed partial class ShortcutEditorWindow
{
    private CancellationTokenSource? _describeCts;
    private bool _recording;

    private sealed record NoteSpec(SymbolRegular Icon, string Headline, string? Detail, IReadOnlyList<(string Text, string BrushKey)> Extras,
        IReadOnlyList<SuggestedAutomation> Automations, Action Undo, Action? Keep, string DismissText, Action Dismiss);

    // ---- the note ------------------------------------------------------------------------------

    private void OnAiNoteChanged()
    {
        // The name box may still hold the old name while it has focus (a new shortcut opens with the name selected).
        NameBox.Text = _vm.Name;
        if (NameBox.IsKeyboardFocused) ActionList.Focus();
        if (_vm.AiNote is not { } n) { NoteHost.Content = null; return; }
        var extras = new List<(string, string)>();
        if (n.DestructiveTitles.Count > 0)
            extras.Add((Strings.Format("ShortcutEditorWindowAi_CanChangeOrDelete", string.Join(", ", n.DestructiveTitles)), Ui.Caution));
        if (n.Warnings.Count > 0)
            extras.Add((Strings.Get("ShortcutEditorWindowAi_AutomationPortalNoticed") + string.Join(" ", n.Warnings.Take(3)), Ui.Secondary));
        ShowNote(new NoteSpec(SymbolRegular.Sparkle24, n.Headline, n.Detail, extras, n.Automations,
            () => _vm.UndoAiResult(), n.IsDraft ? () => _vm.KeepDraft() : null, n.IsDraft ? "Keep" : "Dismiss", () => _vm.DismissAiNote()));
    }

    private void ShowNote(NoteSpec spec)
    {
        var box = new Border { CornerRadius = Radius.Card, BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 12, 12) };
        box.SetResourceReference(Border.BackgroundProperty, Ui.CardFill);
        box.SetResourceReference(Border.BorderBrushProperty, Ui.CardStroke);
        System.Windows.Automation.AutomationProperties.SetName(box, spec.Headline);

        var stack = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Ui.Icon(spec.Icon, 18, Ui.Accent);
        icon.Margin = new Thickness(0, 0, 10, 0);
        head.Children.Add(icon);
        var title = Ui.Text(spec.Headline, Ui.Primary, 14, FontWeights.SemiBold, wrap: true);
        Grid.SetColumn(title, 1);
        head.Children.Add(title);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
        buttons.Children.Add(Ui.TextButton(Strings.Get("ShortcutEditorWindowAi_Undo"), spec.Undo, SymbolRegular.ArrowUndo24));
        var dismiss = Ui.TextButton(spec.DismissText, () => { if (spec.Keep is { } keep) keep(); else spec.Dismiss(); }, null,
            spec.Keep is null ? ControlAppearance.Transparent : ControlAppearance.Primary);
        dismiss.Margin = new Thickness(6, 0, 0, 0);
        buttons.Children.Add(dismiss);
        Grid.SetColumn(buttons, 2);
        head.Children.Add(buttons);
        stack.Children.Add(head);

        if (!string.IsNullOrWhiteSpace(spec.Detail))
        {
            var d = Ui.Text(spec.Detail!, Ui.Secondary, 13, wrap: true);
            d.Margin = new Thickness(28, 4, 0, 0);
            stack.Children.Add(d);
        }
        foreach (var (text, key) in spec.Extras)
        {
            var t = Ui.Text(text, key, 12.5, wrap: true);
            t.Margin = new Thickness(28, 6, 0, 0);
            stack.Children.Add(t);
        }
        if (spec.Automations.Count > 0)
        {
            var row = new WrapPanel { Margin = new Thickness(28, 10, 0, 0) };
            var ask = Ui.Text(Strings.Get("ShortcutEditorWindow_AddThisAutomation"), Ui.Primary, 13, FontWeights.SemiBold);
            ask.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(ask);
            foreach (var s in spec.Automations)
            {
                var b = Ui.TextButton(s.Label, () => AddSuggested(s), SymbolRegular.Flash24);
                b.Margin = new Thickness(0, 0, 8, 4);
                row.Children.Add(b);
            }
            stack.Children.Add(row);
        }
        box.Child = stack;
        NoteHost.Content = box;
    }

    private void AddSuggested(SuggestedAutomation suggestion)
    {
        CommitName();
        _vm.SaveNow();
        _vm.Context.Host.AddSuggestedAutomation(_vm.Shortcut.Id, suggestion);
    }

    // ---- Describe it (empty new shortcut) -------------------------------------------------------------

    private void BuildStartChoices()
    {
        var t = Ui.Text(Strings.Get("ShortcutEditorWindow_HowDoYouWant"), Ui.Primary, 20, FontWeights.SemiBold);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        EmptyState.Children.Add(t);
        var d = Ui.Text(Strings.Get("ShortcutEditorWindow_BuildItYourselfStep"), Ui.Secondary, 14, wrap: true);
        d.TextAlignment = TextAlignment.Center;
        d.Margin = new Thickness(0, 8, 0, 16);
        EmptyState.Children.Add(d);

        var describe = new Wpf.Ui.Controls.TextBox
        {
            PlaceholderText = Strings.Get("ShortcutEditorWindow_ForExampleEveryMorning"),
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, Margin = new Thickness(0, 0, 0, 10),
        };
        System.Windows.Automation.AutomationProperties.SetName(describe, Strings.Get("ShortcutEditorWindowAi_DescribeTheShortcutYou"));
        var status = Ui.Text("", Ui.Critical, 12.5, wrap: true);
        status.Margin = new Thickness(0, 0, 0, 8);
        var busy = new ProgressRing { IsIndeterminate = true, Width = 22, Height = 22, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
        Wpf.Ui.Controls.Button? create = null;
        create = Ui.TextButton(Strings.Get("ShortcutEditorWindow_CreateDraft"), () =>
        {
            if (_describing || string.IsNullOrWhiteSpace(describe.Text)) return;
            _describing = true;
            try
            {
                var result = DescribeItWindow.Show(this, _vm.Context, describe.Text.Trim(), autoStart: true);
                if (result is { Success: true }) { _startedBlank = true; _vm.ApplyAiResult(result, isChange: false); }
            }
            finally { _describing = false; }
        }, SymbolRegular.Sparkle24, ControlAppearance.Primary);
        create.HorizontalAlignment = HorizontalAlignment.Right;

        var describeCard = new StackPanel { Visibility = Visibility.Collapsed };
        describeCard.Children.Add(describe);
        describeCard.Children.Add(busy);
        describeCard.Children.Add(status);
        describeCard.Children.Add(create);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var blank = Ui.TextButton(Strings.Get("ShortcutEditorWindow_StartBlank"), () => { _startedBlank = true; UpdateEmptyState(); ActionPanel.FocusSearch(); }, SymbolRegular.Add24, ControlAppearance.Primary);
        var desc = Ui.TextButton(Strings.Get("ShortcutEditorWindow_DescribeIt"), () => { describeCard.Visibility = Visibility.Visible; describe.Focus(); }, SymbolRegular.Sparkle24);
        desc.Margin = new Thickness(10, 0, 0, 0);
        buttons.Children.Add(blank);
        buttons.Children.Add(desc);
        buttons.Margin = new Thickness(0, 0, 0, 16);
        EmptyState.Children.Add(buttons);
        EmptyState.Children.Add(describeCard);

        if (_vm.AutoDescribe is { Length: > 0 } text)
        {
            _vm.AutoDescribe = null;
            describeCard.Visibility = Visibility.Visible;
            describe.Text = text;
            Dispatcher.BeginInvoke(new Action(() => create!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent))),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>Shown under the "Search actions" button when no AI is set up: Describe it is hidden, with a way to turn it on.</summary>
    private FrameworkElement BuildAiHint()
    {
        var row = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0) };
        row.Children.Add(Ui.Text(Strings.Get("ShortcutEditorWindowAi_WantToDescribeA"), Ui.Secondary, 13));
        var link = new TextBlock { Text = Strings.Get("ShortcutEditorWindow_SetUpAIIn"), FontSize = 13, Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, TextDecorations = TextDecorations.Underline, Focusable = true };
        link.SetResourceReference(TextBlock.ForegroundProperty, Ui.Accent);
        link.MouseLeftButtonUp += (_, _) => _vm.Context.Host.OpenAiSettings();
        link.KeyDown += (_, e) => { if (e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space) _vm.Context.Host.OpenAiSettings(); };
        System.Windows.Automation.AutomationProperties.SetName(link, Strings.Get("ShortcutEditorWindowAi_SetUpAIIn"));
        row.Children.Add(link);
        return row;
    }

    // ---- Describe a change ---------------------------------------------------------------------------

    private async void DescribeChange()
    {
        if (!_vm.Context.Host.CanDescribeWithAi) return;
        CommitName();
        var isEmpty = _vm.IsEmpty;
        if (isEmpty)
        {
            var draft = DescribeItWindow.Show(this, _vm.Context, "", autoStart: false);
            if (draft is { Success: true }) _vm.ApplyAiResult(draft, isChange: false);
            return;
        }
        var dialog = new Window
        {
            Title = isEmpty ? Strings.Get("ShortcutEditorWindow_DescribeIt") : Strings.Get("ShortcutEditorWindow_DescribeAChange"), Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 520, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, FontFamily = Ui.Font,
        };
        dialog.SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
        var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        stack.Children.Add(Ui.Text(isEmpty ? Strings.Get("ShortcutEditorWindow_DescribeTheShortcut") : Strings.Get("ShortcutEditorWindow_WhatShouldChange"), Ui.Primary, 20, FontWeights.SemiBold));
        var hint = Ui.Text(isEmpty ? Strings.Get("ShortcutEditorWindow_SayWhatItShould")
                                   : Strings.Get("ShortcutEditorWindowAi_SayWhatToChange"), Ui.Secondary, 13, wrap: true);
        hint.Margin = new Thickness(0, 6, 0, 12);
        stack.Children.Add(hint);
        var box = new Wpf.Ui.Controls.TextBox
        {
            PlaceholderText = isEmpty ? Strings.Get("ShortcutEditorWindowAi_ForExampleEveryFriday") : Strings.Get("ShortcutEditorWindowAi_ForExampleAlsoCopy"),
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90,
        };
        System.Windows.Automation.AutomationProperties.SetName(box, isEmpty ? Strings.Get("ShortcutEditorWindowAi_DescribeTheShortcutYou") : Strings.Get("ShortcutEditorWindowAi_DescribeTheChangeYou"));
        stack.Children.Add(box);
        var status = Ui.Text("", Ui.Critical, 12.5, wrap: true);
        status.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(status);
        var busy = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
        busy.Children.Add(new ProgressRing { IsIndeterminate = true, Width = 20, Height = 20 });
        var busyText = Ui.Text(Strings.Get("ShortcutEditorWindow_WritingYourDraft"), Ui.Secondary, 13);
        busyText.Margin = new Thickness(10, 0, 0, 0);
        busy.Children.Add(busyText);
        stack.Children.Add(busy);

        var go = Ui.TextButton(Strings.Get("ShortcutEditorWindow_MakeTheChange"), () => { }, SymbolRegular.Sparkle24, ControlAppearance.Primary);
        if (isEmpty) go.Content = Strings.Get("ShortcutEditorWindow_CreateDraft");
        var cancel = Ui.TextButton(Strings.Get("ShortcutEditorWindowAi_Cancel"), () => { }, null);
        cancel.IsCancel = true;
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        cancel.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(cancel);
        row.Children.Add(go);
        stack.Children.Add(row);
        dialog.Content = stack;

        AiDraftResult? outcome = null;
        var cts = new CancellationTokenSource();
        var running = false;
        cancel.Click += (_, _) => { cts.Cancel(); dialog.Close(); };
        dialog.Closing += (_, _) => cts.Cancel();
        go.Click += async (_, _) =>
        {
            if (running || string.IsNullOrWhiteSpace(box.Text)) return;
            running = true; go.IsEnabled = false; box.IsEnabled = false; status.Text = ""; busy.Visibility = Visibility.Visible;
            try
            {
                var result = await _vm.Context.Host.DescribeWithAiAsync(box.Text.Trim(), isEmpty ? null : _vm.Shortcut.Clone(), cts.Token);
                if (result.Success) { outcome = result; dialog.Close(); return; }
                status.Text = result.Error ?? Strings.Get("ShortcutEditorWindowAi_CouldnTMakeA");
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { status.Text = Strings.Get("ShortcutEditorWindowAi_CouldnTReachThe") + ex.Message; }
            running = false; go.IsEnabled = true; box.IsEnabled = true; busy.Visibility = Visibility.Collapsed;
        };
        dialog.Loaded += (_, _) => box.Focus();
        dialog.ShowDialog();
        if (outcome is { Success: true } result) _vm.ApplyAiResult(result, isChange: !isEmpty);
    }

    // ---- Explain this shortcut ----------------------------------------------------------------------

    private void ExplainShortcut()
    {
        CommitName();
        var host = _vm.Context.Host;
        var dialog = new Window
        {
            Title = Strings.Get("ExplainFix_ShortcutTitle"), Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 520, SizeToContent = SizeToContent.Height, MaxHeight = 640, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, FontFamily = Ui.Font,
        };
        dialog.SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
        var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        stack.Children.Add(Ui.Text(Strings.Get("ExplainFix_ShortcutTitle"), Ui.Primary, 20, FontWeights.SemiBold));
        if (host.AiPrivacyLine is { Length: > 0 } privacy)
        {
            var p = Ui.Text(privacy, Ui.Tertiary, 12, wrap: true);
            p.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(p);
        }
        var busy = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        busy.Children.Add(new ProgressRing { IsIndeterminate = true, Width = 20, Height = 20 });
        var busyText = Ui.Text(Strings.Get("ExplainFix_Reading"), Ui.Secondary, 13);
        busyText.Margin = new Thickness(10, 0, 0, 0);
        busy.Children.Add(busyText);
        stack.Children.Add(busy);
        var text = new System.Windows.Controls.TextBox
        {
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Padding = new Thickness(0), Margin = new Thickness(0, 12, 0, 0), FontSize = 13.5, Visibility = Visibility.Collapsed,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 380,
        };
        text.SetResourceReference(Control.ForegroundProperty, Ui.Primary);
        System.Windows.Automation.AutomationProperties.SetName(text, Strings.Get("ExplainFix_TextName"));
        stack.Children.Add(text);
        var problem = Ui.Text("", Ui.Critical, 12.5, wrap: true);
        problem.Margin = new Thickness(0, 12, 0, 0);
        problem.Visibility = Visibility.Collapsed;
        stack.Children.Add(problem);
        var setup = new TextBlock { Text = Strings.Get("ExplainFix_SetUp"), FontSize = 12.5, Cursor = System.Windows.Input.Cursors.Hand, TextDecorations = TextDecorations.Underline, Focusable = true, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
        setup.SetResourceReference(TextBlock.ForegroundProperty, Ui.Accent);
        System.Windows.Automation.AutomationProperties.SetName(setup, Strings.Get("ExplainFix_SetUpName"));
        setup.MouseLeftButtonUp += (_, _) => host.OpenAiSettings();
        setup.KeyDown += (_, e) => { if (e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space) host.OpenAiSettings(); };
        stack.Children.Add(setup);

        var copy = Ui.TextButton(Strings.Get("ExplainFix_Copy"), () =>
        {
            try { Clipboard.SetText(text.Text); } catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException) { }
        }, SymbolRegular.Copy20);
        copy.IsEnabled = false;
        var close = Ui.TextButton(Strings.Get("ExplainFix_Close"), () => { }, null, ControlAppearance.Primary);
        close.IsCancel = true;
        close.IsDefault = true;
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        copy.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(copy);
        row.Children.Add(close);
        stack.Children.Add(row);
        dialog.Content = stack;

        var cts = new CancellationTokenSource();
        dialog.Closing += (_, _) => cts.Cancel();
        close.Click += (_, _) => dialog.Close();
        dialog.ContentRendered += async (_, _) =>
        {
            try
            {
                var r = await host.ExplainShortcutAsync(_vm.Shortcut.Clone(), cts.Token);
                busy.Visibility = Visibility.Collapsed;
                if (r.Text is { Length: > 0 } t) { text.Text = t; text.Visibility = Visibility.Visible; copy.IsEnabled = true; }
                else
                {
                    problem.Text = r.Error ?? Strings.Get("ExplainFix_NoAnswer");
                    problem.Visibility = Visibility.Visible;
                    setup.Visibility = r.NeedsSetup ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                busy.Visibility = Visibility.Collapsed;
                problem.Text = Strings.Get("ExplainFix_CouldntReach") + " " + ex.Message;
                problem.Visibility = Visibility.Visible;
            }
        };
        dialog.ShowDialog();
    }

    // ---- recording ---------------------------------------------------------------------------------

    /// <summary>Records clicks and typing through the host and inserts the steps after the selection (one undo step).</summary>
    internal async Task RecordAsync()
    {
        if (_recording || !_vm.Context.Host.CanRecord) return;
        _recording = true;
        RecordButton.IsEnabled = false;
        try
        {
            var steps = await _vm.Context.Host.RecordMacroAsync(this);
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            if (steps is null) return; // cancelled before recording, or the recording was discarded in the review
            if (steps.Count == 0)
            {
                ShowNote(new NoteSpec(SymbolRegular.Record24, Strings.Get("ShortcutEditorWindow_NothingWasRecorded"), Strings.Get("ShortcutEditorWindow_StartAgainClickButtons"),
                    Array.Empty<(string, string)>(), Array.Empty<SuggestedAutomation>(), () => { }, null, "Dismiss", () => NoteHost.Content = null));
                return;
            }
            _startedBlank = true;
            _vm.InsertRecorded(steps);
            ShowNote(new NoteSpec(SymbolRegular.Record24, steps.Count == 1 ? "Recorded 1 step" : $"Recorded {steps.Count} steps",
                Strings.Get("ShortcutEditorWindow_EachStepFindsIts"),
                Array.Empty<(string, string)>(), Array.Empty<SuggestedAutomation>(), () => { _vm.Undo(); NoteHost.Content = null; }, null, "Dismiss", () => NoteHost.Content = null));
            // The first run of a recording is the one to watch: offer to go one step at a time (Next step / Stop).
            if (!_vm.IsRunning
                && EditorDialog.Ask(this, Strings.Get("ShortcutEditorWindow_RunOneStepTitle"), Strings.Get("ShortcutEditorWindow_RunOneStepBody"),
                    Strings.Get("ShortcutEditorWindow_RunOneStepButton"), Strings.Get("ShortcutEditorWindow_NotNow"), Strings.Get("ShortcutEditorWindowAi_Cancel"), enter: DialogChoice.Primary) == DialogChoice.Primary)
                _ = _vm.RunStepByStepAsync();
        }
        finally { _recording = false; RecordButton.IsEnabled = true; }
    }

    // ---- closing with an unkept draft ------------------------------------------------------------------

    /// <summary>False when the user cancelled the close.</summary>
    private bool ResolveUnkeptDraft()
    {
        if (!_vm.HasUnkeptDraft) return true;
        var answer = EditorDialog.Ask(this, Strings.Get("ShortcutEditorWindow_SaveThisDraft"), Strings.Get("ShortcutEditorWindow_YourDraftHasntBeen"),
            Strings.Get("ShortcutEditorWindow_SaveDraft"), Strings.Get("ShortcutEditorWindow_DiscardDraft"), Strings.Get("ShortcutEditorWindowAi_Cancel"), enter: DialogChoice.Primary);
        if (answer == DialogChoice.Cancel) return false;
        if (answer == DialogChoice.Secondary) _vm.DiscardDraft();
        return true;
    }
}
