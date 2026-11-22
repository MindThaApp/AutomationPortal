using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using AutomationPortal.Core;
using AutomationPortal.Editor.UI;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace AutomationPortal.Editor;

/// <summary>
/// "Describe it" for a new shortcut: the sentence, up to three clarifying questions, then a plain preview of the draft.
/// Nothing is saved or run here; <see cref="Outcome"/> is only set when the person chooses "Open in editor".
/// </summary>
internal sealed class DescribeItWindow : Window
{
    private readonly EditorContext _context;
    private readonly CancellationTokenSource _cts = new();
    private readonly ContentControl _page = new();
    private string _description;
    private bool _busy;

    public AiDraftResult? Outcome { get; private set; }

    private DescribeItWindow(Window? owner, EditorContext context, string description)
    {
        _context = context;
        _description = description;
        Title = Strings.Get("ShortcutEditorWindow_DescribeIt");
        Owner = owner;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        Width = 520; SizeToContent = SizeToContent.Height; MaxHeight = 700; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; FontFamily = Ui.Font;
        this.SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
        Content = _page;
        Closing += (_, _) => _cts.Cancel();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }

    /// <summary>Shows the dialog. <paramref name="autoStart"/> goes straight to checking the request instead of waiting for "Create draft".</summary>
    public static AiDraftResult? Show(Window? owner, EditorContext context, string description, bool autoStart)
    {
        var w = new DescribeItWindow(owner, context, description);
        w.Loaded += (_, _) => { if (autoStart && description.Trim().Length > 0) w.Start(); else w.ShowDescribe(null); };
        w.ShowDialog();
        return w.Outcome;
    }

    // ---- pages ---------------------------------------------------------------------------------

    private StackPanel NewPage(string title, string? hint)
    {
        var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        stack.Children.Add(Ui.Text(title, Ui.Primary, 20, FontWeights.SemiBold));
        if (hint is not null)
        {
            var h = Ui.Text(hint, Ui.Secondary, 13, wrap: true);
            h.Margin = new Thickness(0, 6, 0, 12);
            stack.Children.Add(h);
        }
        return stack;
    }

    private void AddPrivacy(StackPanel stack)
    {
        if (_context.Host.AiDescribePrivacyLine is not { Length: > 0 } line) return;
        var p = Ui.Text(line, Ui.Tertiary, 12, wrap: true);
        p.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(p);
    }

    private static StackPanel ButtonRow(params Wpf.Ui.Controls.Button[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        for (var i = 0; i < buttons.Length; i++)
        {
            if (i > 0) buttons[i].Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(buttons[i]);
        }
        return row;
    }

    private void ShowDescribe(string? error)
    {
        var stack = NewPage(Strings.Get("ShortcutEditorWindow_DescribeTheShortcut"), Strings.Get("ShortcutEditorWindow_SayWhatItShould"));
        var box = new Wpf.Ui.Controls.TextBox
        {
            PlaceholderText = Strings.Get("ShortcutEditorWindow_ForExampleEveryMorning"), Text = _description,
            AcceptsReturn = false, TextWrapping = TextWrapping.Wrap, MinHeight = 90,
        };
        AutomationProperties.SetName(box, Strings.Get("DescribeIt_BoxName"));
        stack.Children.Add(box);
        AddPrivacy(stack);
        if (error is not null)
        {
            var status = Ui.Text(error, Ui.Critical, 12.5, wrap: true);
            status.Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(status);
        }
        var cancel = Ui.TextButton(Strings.Get("DescribeIt_Cancel"), () => Close(), null, ControlAppearance.Transparent);
        cancel.IsCancel = true;
        var go = Ui.TextButton(Strings.Get("ShortcutEditorWindow_CreateDraft"), () =>
        {
            if (string.IsNullOrWhiteSpace(box.Text)) return;
            _description = box.Text.Trim();
            Start();
        }, SymbolRegular.Sparkle24, ControlAppearance.Primary);
        go.IsDefault = true;
        stack.Children.Add(ButtonRow(cancel, go));
        _page.Content = stack;
        box.Focus();
        box.CaretIndex = box.Text.Length;
    }

    private void ShowBusy(string text)
    {
        var stack = NewPage(Strings.Get("ShortcutEditorWindow_DescribeTheShortcut"), null);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 4) };
        row.Children.Add(new ProgressRing { IsIndeterminate = true, Width = 20, Height = 20 });
        var t = Ui.Text(text, Ui.Secondary, 13);
        t.Margin = new Thickness(10, 0, 0, 0);
        row.Children.Add(t);
        AutomationProperties.SetName(row, text);
        stack.Children.Add(row);
        AddPrivacy(stack);
        _page.Content = stack;
    }

    // ---- flow ----------------------------------------------------------------------------------

    private async void Start()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            ShowBusy(Strings.Get("DescribeIt_Checking"));
            IReadOnlyList<AiQuestion> questions;
            try { questions = await _context.Host.ClarifyWithAiAsync(_description, _cts.Token); }
            catch (OperationCanceledException) { return; }
            catch (Exception) { questions = Array.Empty<AiQuestion>(); }
            if (questions.Count == 0) await BuildAsync(_description);
            else ShowQuestions(questions);
        }
        finally { _busy = false; }
    }

    private async Task BuildAsync(string request)
    {
        ShowBusy(Strings.Get("ShortcutEditorWindow_WritingYourDraft"));
        try
        {
            var result = await _context.Host.DescribeWithAiAsync(request, null, _cts.Token);
            if (result.Success) ShowPreview(result);
            else ShowDescribe(result.Error ?? Strings.Get("DescribeIt_NoDraft"));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowDescribe(Strings.Get("DescribeIt_CouldntBuild") + " " + ex.Message); }
    }

    private void ShowQuestions(IReadOnlyList<AiQuestion> questions)
    {
        var stack = NewPage(Strings.Get("DescribeIt_QuestionsTitle"), Strings.Get("DescribeIt_QuestionsHint"));
        var readers = new List<Func<string>>();
        foreach (var q in questions) stack.Children.Add(QuestionCard(q, readers));
        AddPrivacy(stack);

        async void Build(bool withAnswers)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var request = withAnswers
                    ? AiQuestions.AppendAnswers(_description, questions, readers.Select(r => r()).ToList())
                    : _description;
                await BuildAsync(request);
            }
            finally { _busy = false; }
        }

        var cancel = Ui.TextButton(Strings.Get("DescribeIt_Cancel"), () => Close(), null, ControlAppearance.Transparent);
        cancel.IsCancel = true;
        var skip = Ui.TextButton(Strings.Get("DescribeIt_Skip"), () => Build(false));
        var go = Ui.TextButton(Strings.Get("DescribeIt_Build"), () => Build(true), SymbolRegular.Sparkle24, ControlAppearance.Primary);
        go.IsDefault = true;
        stack.Children.Add(ButtonRow(cancel, skip, go));
        _page.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack };
        go.Focus();
    }

    private FrameworkElement QuestionCard(AiQuestion q, List<Func<string>> readers)
    {
        var card = new Border { CornerRadius = Radius.Card, BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(0, 0, 0, 10) };
        card.SetResourceReference(Border.BackgroundProperty, Ui.CardFill);
        card.SetResourceReference(Border.BorderBrushProperty, Ui.CardStroke);
        var inner = new StackPanel();
        var label = Ui.Text(q.Text, Ui.Primary, 14, FontWeights.SemiBold, wrap: true);
        label.Margin = new Thickness(0, 0, 0, 8);
        inner.Children.Add(label);
        AutomationProperties.SetName(card, q.Text);
        var group = "q" + Guid.NewGuid().ToString("N");

        RadioButton Radio(string text, bool on)
        {
            var r = new RadioButton { Content = text, GroupName = group, IsChecked = on, Margin = new Thickness(0, 0, 0, 4) };
            r.SetResourceReference(Control.ForegroundProperty, Ui.Primary);
            AutomationProperties.SetName(r, q.Text + " " + text);
            inner.Children.Add(r);
            return r;
        }

        switch (q.Kind)
        {
            case AiQuestionKind.Choice:
                var radios = q.Options.Select(o => (Text: o, Button: Radio(o, o == q.Default))).ToList();
                readers.Add(() => radios.FirstOrDefault(r => r.Button.IsChecked == true).Text ?? q.Default);
                break;
            case AiQuestionKind.YesNo:
                var yes = Radio(Strings.Get("DescribeIt_Yes"), q.Default == "yes");
                Radio(Strings.Get("DescribeIt_No"), q.Default != "yes");
                readers.Add(() => yes.IsChecked == true ? Strings.Get("DescribeIt_Yes") : Strings.Get("DescribeIt_No"));
                break;
            case AiQuestionKind.Text:
                var text = new Wpf.Ui.Controls.TextBox { Text = q.Default, TextWrapping = TextWrapping.Wrap };
                AutomationProperties.SetName(text, q.Text);
                inner.Children.Add(text);
                readers.Add(() => text.Text.Trim());
                break;
            default:
                var isFolder = q.Kind == AiQuestionKind.Folder;
                var path = "";
                var shown = Ui.Text(Strings.Get("DescribeIt_NotChosen"), Ui.Secondary, 13, wrap: true);
                shown.Margin = new Thickness(10, 0, 0, 0);
                shown.VerticalAlignment = VerticalAlignment.Center;
                var pick = Ui.TextButton(Strings.Get("DescribeIt_Choose"), () =>
                {
                    string? chosen = null;
                    if (isFolder)
                    {
                        var d = new OpenFolderDialog { Title = Strings.Get("DescribeIt_ChooseFolder") };
                        if (d.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(d.FolderName)) chosen = d.FolderName;
                    }
                    else
                    {
                        var d = new OpenFileDialog { Title = Strings.Get("DescribeIt_ChooseFile"), CheckFileExists = true, Multiselect = false };
                        if (d.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(d.FileName)) chosen = d.FileName;
                    }
                    if (chosen is null) return;
                    path = chosen;
                    shown.Text = chosen;
                }, isFolder ? SymbolRegular.Folder24 : SymbolRegular.Document24);
                AutomationProperties.SetName(pick, q.Text + " " + Strings.Get("DescribeIt_Choose"));
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(pick);
                row.Children.Add(shown);
                inner.Children.Add(row);
                readers.Add(() => path);
                break;
        }
        card.Child = inner;
        return card;
    }

    private void ShowPreview(AiDraftResult result)
    {
        var stack = NewPage(Strings.Get("DescribeIt_PreviewTitle"), Strings.Get("DescribeIt_PreviewHint"));
        if (!string.IsNullOrWhiteSpace(result.Summary))
        {
            var s = Ui.Text(result.Summary, Ui.Primary, 13.5, wrap: true);
            s.Margin = new Thickness(0, 0, 0, 10);
            stack.Children.Add(s);
        }
        var list = new StackPanel();
        AutomationProperties.SetName(list, Strings.Get("DescribeIt_StepsName"));
        var n = 1;
        foreach (var line in AiDraftSummary.Lines(result.Shortcut!, _context.Actions))
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = Ui.Text($"{n++}. {line.Title}", Ui.Primary, 13.5, wrap: true);
            row.Children.Add(title);
            var (label, loud) = line.Effect switch
            {
                AiStepEffect.ChangesFiles => (Strings.Get("DescribeIt_ChangesFiles"), true),
                AiStepEffect.MakesCopies => (Strings.Get("DescribeIt_MakesCopies"), false),
                AiStepEffect.ControlsApps => (Strings.Get("DescribeIt_ControlsApps"), true),
                _ => (Strings.Get("DescribeIt_NoFileChanges"), false),
            };
            var badge = new Border { CornerRadius = Radius.Card, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top, Child = Ui.Text(label, Ui.Primary, 12) };
            badge.SetResourceReference(Border.BackgroundProperty, loud ? "DsCautionBackgroundBrush" : "DsHoverBrush");
            Grid.SetColumn(badge, 1);
            row.Children.Add(badge);
            AutomationProperties.SetName(row, $"{line.Title}, {label}");
            list.Children.Add(row);
        }
        stack.Children.Add(list);
        AddPrivacy(stack);

        var startOver = Ui.TextButton(Strings.Get("DescribeIt_StartOver"), () => ShowDescribe(null));
        var open = Ui.TextButton(Strings.Get("DescribeIt_OpenInEditor"), () => { Outcome = result; Close(); }, SymbolRegular.Open24, ControlAppearance.Primary);
        open.IsDefault = true;
        stack.Children.Add(ButtonRow(startOver, open));
        _page.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack };
        open.Focus();
    }
}
