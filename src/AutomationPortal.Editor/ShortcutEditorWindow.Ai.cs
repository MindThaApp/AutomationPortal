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
            extras.Add(($"Can change or delete things: {string.Join(", ", n.DestructiveTitles)}. Those steps are marked below, so look at them before you run this.", Ui.Caution));
        if (n.Warnings.Count > 0)
            extras.Add(("Automation Portal noticed: " + string.Join(" ", n.Warnings.Take(3)), Ui.Secondary));
        ShowNote(new NoteSpec(SymbolRegular.Sparkle24, n.Headline, n.Detail, extras, n.Automations,
            () => _vm.UndoAiResult(), n.IsDraft ? () => _vm.KeepDraft() : null, n.IsDraft ? "Keep" : "Dismiss", () => _vm.DismissAiNote()));
    }

    private void ShowNote(NoteSpec spec)
    {
        var box = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 12, 12) };
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
        buttons.Children.Add(Ui.TextButton("Undo", spec.Undo, SymbolRegular.ArrowUndo24));
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
        System.Windows.Automation.AutomationProperties.SetName(describe, "Describe the shortcut you want");
        var status = Ui.Text("", Ui.Critical, 12.5, wrap: true);
        status.Margin = new Thickness(0, 0, 0, 8);
        var busy = new ProgressRing { IsIndeterminate = true, Width = 22, Height = 22, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
        Wpf.Ui.Controls.Button? create = null;
        create = Ui.TextButton(Strings.Get("ShortcutEditorWindow_CreateDraft"), async () =>
        {
            if (_describing || string.IsNullOrWhiteSpace(describe.Text)) return;
            _describing = true;
            busy.Visibility = Visibility.Visible;
            status.Text = "";
            create!.IsEnabled = false;
            _describeCts = new CancellationTokenSource();
            try
            {
                var result = await _vm.Context.Host.DescribeWithAiAsync(describe.Text.Trim(), null, _describeCts.Token);
                if (!result.Success) status.Text = result.Error ?? "Couldn't build a draft from that. Try describing it a different way, or start blank.";
                else { _startedBlank = true; _vm.ApplyAiResult(result, isChange: false); }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { status.Text = "Couldn't build a draft: " + ex.Message; }
            finally { _describing = false; busy.Visibility = Visibility.Collapsed; create!.IsEnabled = true; }
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
        row.Children.Add(Ui.Text("Want to describe a shortcut in a sentence? ", Ui.Secondary, 13));
        var link = new TextBlock { Text = Strings.Get("ShortcutEditorWindow_SetUpAIIn"), FontSize = 13, Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, TextDecorations = TextDecorations.Underline, Focusable = true };
        link.SetResourceReference(TextBlock.ForegroundProperty, Ui.Accent);
        link.MouseLeftButtonUp += (_, _) => _vm.Context.Host.OpenAiSettings();
        link.KeyDown += (_, e) => { if (e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space) _vm.Context.Host.OpenAiSettings(); };
        System.Windows.Automation.AutomationProperties.SetName(link, "Set up AI in Settings, AI page");
        row.Children.Add(link);
        return row;
    }

    // ---- Describe a change ---------------------------------------------------------------------------

    private async void DescribeChange()
    {
        if (!_vm.Context.Host.CanDescribeWithAi) return;
        CommitName();
        var isEmpty = _vm.IsEmpty;
        var dialog = new Window
        {
            Title = isEmpty ? Strings.Get("ShortcutEditorWindow_DescribeIt") : Strings.Get("ShortcutEditorWindow_DescribeAChange"), Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 520, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, FontFamily = Ui.Font,
        };
        dialog.SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
        var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        stack.Children.Add(Ui.Text(isEmpty ? Strings.Get("ShortcutEditorWindow_DescribeTheShortcut") : Strings.Get("ShortcutEditorWindow_WhatShouldChange"), Ui.Primary, 20, FontWeights.SemiBold));
        var hint = Ui.Text(isEmpty ? Strings.Get("ShortcutEditorWindow_SayWhatItShould")
                                   : "Say what to change in plain words. It rewrites the steps for you; you can undo it with one click.", Ui.Secondary, 13, wrap: true);
        hint.Margin = new Thickness(0, 6, 0, 12);
        stack.Children.Add(hint);
        var box = new Wpf.Ui.Controls.TextBox
        {
            PlaceholderText = isEmpty ? "For example: every Friday, zip my Downloads folder and email it to me" : "For example: also copy the result to the clipboard",
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90,
        };
        System.Windows.Automation.AutomationProperties.SetName(box, isEmpty ? "Describe the shortcut you want" : "Describe the change you want");
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
        var cancel = Ui.TextButton("Cancel", () => { }, null);
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
                status.Text = result.Error ?? "Couldn't make a draft from that. Try other words, or start blank.";
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { status.Text = "Couldn't reach the AI: " + ex.Message; }
            running = false; go.IsEnabled = true; box.IsEnabled = true; busy.Visibility = Visibility.Collapsed;
        };
        dialog.Loaded += (_, _) => box.Focus();
        dialog.ShowDialog();
        if (outcome is { Success: true } result) _vm.ApplyAiResult(result, isChange: !isEmpty);
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
                    Strings.Get("ShortcutEditorWindow_RunOneStepButton"), Strings.Get("ShortcutEditorWindow_NotNow"), "Cancel", enter: DialogChoice.Primary) == DialogChoice.Primary)
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
            Strings.Get("ShortcutEditorWindow_SaveDraft"), Strings.Get("ShortcutEditorWindow_DiscardDraft"), "Cancel", enter: DialogChoice.Primary);
        if (answer == DialogChoice.Cancel) return false;
        if (answer == DialogChoice.Secondary) _vm.DiscardDraft();
        return true;
    }
}
