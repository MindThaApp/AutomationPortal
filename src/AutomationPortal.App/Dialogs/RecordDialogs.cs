using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AutomationPortal.Actions.Windows.Apps;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.Core;
using AutomationPortal.Core.AppControl;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Dialogs;

/// <summary>"Which app will you show?": the open windows as tiles. Only the chosen app is recorded.</summary>
public static class RecordAppPicker
{
    public static async Task<RecordableWindow?> PickAsync(Window? owner)
    {
        IReadOnlyList<RecordableWindow> windows;
        try { windows = await Task.Run(RecordableWindows.List); }
        catch { windows = Array.Empty<RecordableWindow>(); }

        var body = new StackPanel();
        body.Children.Add(Ui.Text(Strings.Get("RecordDialogs_OpenTheAppFirst"), secondary: true, size: 13));
        RecordableWindow? chosen = null;
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 12, 0, 0) };
        var tiles = new List<ToggleButton>();
        FlowDialog? dlg = null;
        foreach (var w in windows)
        {
            var text = new StackPanel();
            text.Children.Add(Ui.Text(w.AppName, size: 14, weight: FontWeights.SemiBold, wrap: false));
            var title = Ui.Text(w.Title, secondary: true, size: 12, wrap: false);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(title);
            var tile = new ToggleButton
            {
                Content = text, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 10, 12, 10),
                HorizontalContentAlignment = HorizontalAlignment.Left, MinHeight = 56, ToolTip = w.Title,
            };
            AutomationProperties.SetName(tile, $"{w.AppName}: {w.Title}");
            tile.Click += (_, _) =>
            {
                foreach (var t in tiles) t.IsChecked = ReferenceEquals(t, tile);
                chosen = w;
                dlg?.SetPrimaryEnabled(true);
            };
            tiles.Add(tile);
            grid.Children.Add(tile);
        }
        if (windows.Count == 0)
            body.Children.Add(Ui.Text(Strings.Get("RecordDialogs_NoAppWindowsAre"), size: 13));
        else
            body.Children.Add(new ScrollViewer { Content = grid, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var note = Ui.Text(Strings.Get("RecordDialogs_StartsAfterA3"), secondary: true, size: 12);
        note.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(note);

        dlg = new FlowDialog(Strings.Get("RecordDialogs_WhichAppWillYou"), body, Strings.Get("RecordDialogs_StartRecording"), "Cancel", width: 620);
        dlg.SetPrimaryEnabled(false);
        return await dlg.ShowTopmostAsync(default, owner) ? chosen : null;
    }
}

/// <summary>
/// After Stop: every recorded step in plain words. Delete, move up or down, change what is typed, make it "ask each time",
/// and set how long each step waits for its control. Returns the steps to add, or null when the recording is discarded.
/// </summary>
public static class RecordingReviewDialog
{
    public static async Task<IReadOnlyList<ActionInstance>?> ShowAsync(Window? owner, RecordingReview review, string appName, TimeSpan took)
    {
        var body = new StackPanel();
        var head = Ui.Text("", size: 15, weight: FontWeights.SemiBold);
        body.Children.Add(head);
        var sub = Ui.Text(Strings.Get("RecordDialogs_ChangeAnythingBeforeAdding"), secondary: true, size: 13);
        sub.Margin = new Thickness(0, 2, 0, 10);
        body.Children.Add(sub);
        var rows = new StackPanel();
        body.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 440, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        if (review.LeftOut.Count > 0)
        {
            var left = Ui.Text(Strings.Format("RecordDialogs_NotRecordedList", string.Join("; ", review.LeftOut))
                               + Strings.Get("RecordDialogs_AddAClickOn"), secondary: true, size: 12);
            left.Margin = new Thickness(0, 10, 0, 0);
            body.Children.Add(left);
        }

        FlowDialog? dlg = null;
        void Rebuild()
        {
            var seconds = Math.Max(1, (int)Math.Round(took.TotalSeconds));
            head.Text = Strings.Format("RecordDialogs_RecordedIn01", appName, (review.Count == 1 ? "1 step" : $"{review.Count} steps"), seconds);
            rows.Children.Clear();
            for (var i = 0; i < review.Count; i++) rows.Children.Add(Row(review, i, Rebuild));
            if (review.Count == 0) rows.Children.Add(Ui.Text(Strings.Get("RecordDialogs_NoStepsLeftDiscard"), secondary: true, size: 13));
            dlg?.SetPrimaryEnabled(review.Result().Count > 0);
        }

        Rebuild();
        dlg = new FlowDialog(Strings.Get("RecordDialogs_ReviewTheRecording"), body, Strings.Get("RecordDialogs_AddSteps"), "Discard", width: 760);
        dlg.SetPrimaryEnabled(review.Result().Count > 0);
        return await dlg.ShowTopmostAsync(default, owner) ? review.Result() : null;
    }

    private static FrameworkElement Row(RecordingReview review, int i, Action rebuild)
    {
        var step = review.Steps[i];
        var card = new Border { CornerRadius = AutomationPortal.Editor.Radius.Card, BorderThickness = new Thickness(1), Padding = new Thickness(12, 8, 8, 8), Margin = new Thickness(0, 0, 0, 6) };
        card.SetResourceReference(Border.BackgroundProperty, "DsCardBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "DsStrokeBrush");
        var stack = new StackPanel();
        card.Child = stack;

        var top = new DockPanel();
        var number = Ui.Text((i + 1).ToString(CultureInfo.CurrentCulture), secondary: true, size: 13, wrap: false);
        number.Width = 24;
        DockPanel.SetDock(number, Dock.Left);
        top.Children.Add(number);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(buttons, Dock.Right);
        buttons.Children.Add(IconButton(SymbolRegular.ArrowUp24, Strings.Format("RecordDialogs_MoveStep0Up", i + 1), i > 0, () => { review.Move(i, i - 1); rebuild(); }));
        buttons.Children.Add(IconButton(SymbolRegular.ArrowDown24, Strings.Format("RecordDialogs_MoveStep0Down", i + 1), i < review.Count - 1, () => { review.Move(i, i + 1); rebuild(); }));
        buttons.Children.Add(IconButton(SymbolRegular.Dismiss24, Strings.Format("RecordDialogs_RemoveStep0", i + 1), true, () => { review.Delete(i); rebuild(); }));
        top.Children.Add(buttons);

        var sentence = Ui.Text(review.Describe(i), size: 14);
        sentence.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(sentence);
        stack.Children.Add(top);

        var details = new WrapPanel { Margin = new Thickness(24, 6, 0, 0) };
        var kind = UiSteps.KindOf(step);
        if (kind.Length > 0) details.Children.Add(Chip(kind, caution: false));
        if (UiSteps.IsWeak(step)) details.Children.Add(Chip(Strings.Get("RecordDialogs_FoundByItsPlace"), caution: true));

        if (step.ActionId == AppControlIds.Type && !UiSteps.IsSecret(step))
        {
            var box = new System.Windows.Controls.TextBox { Text = review.TextOf(i), MinWidth = 220, IsEnabled = !review.AsksEachTime(i), Margin = new Thickness(0, 0, 10, 0) };
            AutomationProperties.SetName(box, Strings.Format("RecordDialogs_TextTypedInStep", i + 1));
            box.LostKeyboardFocus += (_, _) =>
            {
                if (!review.AsksEachTime(i) && box.Text != review.TextOf(i)) { review.SetText(i, box.Text); sentence.Text = review.Describe(i); }
            };
            var ask = new System.Windows.Controls.CheckBox { Content = Strings.Get("RecordDialogs_AskMeEachTime"), IsChecked = review.AsksEachTime(i), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            ask.Click += (_, _) =>
            {
                if (!review.AsksEachTime(i) && box.Text != review.TextOf(i)) review.SetText(i, box.Text);
                review.SetAskEachTime(i, ask.IsChecked == true);
                rebuild();
            };
            details.Children.Add(box);
            details.Children.Add(ask);
        }
        else if (UiSteps.IsSecret(step))
            details.Children.Add(Chip(Strings.Get("RecordDialogs_PasswordBoxAsksEach"), caution: false));

        var wait = new System.Windows.Controls.TextBox { Text = review.TimeoutOf(i).ToString("0.#", CultureInfo.CurrentCulture), Width = 56, Margin = new Thickness(6, 0, 6, 0) };
        AutomationProperties.SetName(wait, Strings.Format("RecordDialogs_SecondsStep0Waits", i + 1));
        wait.LostKeyboardFocus += (_, _) =>
        {
            if (double.TryParse(wait.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var s)) review.SetTimeout(i, s);
            wait.Text = review.TimeoutOf(i).ToString("0.#", CultureInfo.CurrentCulture);
        };
        var waitRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        waitRow.Children.Add(Ui.Text(step.ActionId == AppControlIds.WaitWindow ? Strings.Get("RecordDialogs_WaitUpTo") : Strings.Get("RecordDialogs_WaitForItUp"), secondary: true, size: 12, wrap: false));
        waitRow.Children.Add(wait);
        waitRow.Children.Add(Ui.Text("seconds", secondary: true, size: 12, wrap: false));
        foreach (var t in waitRow.Children.OfType<System.Windows.Controls.TextBlock>()) t.VerticalAlignment = VerticalAlignment.Center;
        details.Children.Add(waitRow);
        stack.Children.Add(details);
        return card;
    }

    private static Wpf.Ui.Controls.Button IconButton(SymbolRegular icon, string name, bool enabled, Action onClick)
    {
        var b = new Wpf.Ui.Controls.Button { Icon = new SymbolIcon(icon), Appearance = ControlAppearance.Transparent, IsEnabled = enabled, Padding = new Thickness(6), Margin = new Thickness(2, 0, 0, 0), ToolTip = name };
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        AutomationProperties.SetName(b, name);
        b.Click += (_, _) => onClick();
        return b;
    }

    private static Border Chip(string text, bool caution)
    {
        var chip = new Border { CornerRadius = AutomationPortal.Editor.Radius.Control, Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 8, 4), VerticalAlignment = VerticalAlignment.Center };
        chip.SetResourceReference(Border.BackgroundProperty, caution ? "DsCautionBackgroundBrush" : "DsHoverBrush");
        chip.Child = Ui.Text(text, secondary: !caution, size: 12, wrap: false);
        return chip;
    }
}
