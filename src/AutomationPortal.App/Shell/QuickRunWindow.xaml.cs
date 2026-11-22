using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AutomationPortal.Actions.Documents;
using AutomationPortal.App.Dialogs;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Logic;
using AutomationPortal.App.Integration;
using AutomationPortal.App.Pages;
using AutomationPortal.Core;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Shell;

public sealed class QuickRunRow : System.ComponentModel.INotifyPropertyChanged
{
    private string? _subtitle;
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public QuickRunRow(QuickRunItem item)
    {
        Item = item;
        Title = item.Title;
        _subtitle = item.Subtitle;
        ActionLabel = item.ActionLabel;
        Symbol = Icons.Parse(item.Icon);
        Brush = Palette.Gradient(item.Color);
    }

    public QuickRunItem Item { get; }
    public string Title { get; }
    public string? Subtitle
    {
        get => _subtitle;
        set { if (_subtitle == value) return; _subtitle = value; PropertyChanged?.Invoke(this, new(nameof(Subtitle))); }
    }
    public string ActionLabel { get; }
    public SymbolRegular Symbol { get; }
    public Brush Brush { get; }

    /// <summary>What a screen reader announces for the result ("Say Hello, Short code: hi, Run").</summary>
    public override string ToString() => string.Join(", ", new[] { Title, Subtitle, ActionLabel }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>The Quick Run palette: borderless search popup in the middle of the active monitor.</summary>
public partial class QuickRunWindow : FluentWindow
{
    private readonly AppHost _host;
    private readonly ObservableCollection<QuickRunRow> _rows = new();
    private CancellationTokenSource? _cts;
    private string? _preset;
    private QuickRunItem? _locked;
    private DateTime _openedAt;

    private const string NormalHints = "Enter run      Tab give it text      Ctrl+Enter edit      Esc close";
    private const string LockedHints = "Type the input, then press Enter      Backspace go back      Esc close";

    public QuickRunWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        ResultList.ItemsSource = _rows;
        Deactivated += (_, _) => { if ((DateTime.UtcNow - _openedAt).TotalMilliseconds > 400) Hide(); };
        // Esc closes the window also when the answer text has the keyboard focus.
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Hide(); e.Handled = true; } };
        IsVisibleChanged += (_, e) => { if (e.NewValue is false) { CancelAsk(); HideAnswer(); } };
        // A shortcut that is still running shows the same count the tile shows ("12 of 40"), also when this window is opened mid-run.
        _host.Runs.ProgressChanged += (_, e) => ShowProgress(e.ShortcutId, e.CountText ?? e.Text);
        _host.Runs.StateChanged += (_, e) => { if (e.State != Services.TileRunState.Running) ShowProgress(e.ShortcutId, null); };
    }

    private void ShowProgress(Guid shortcutId, string? text)
    {
        foreach (var row in _rows.Where(r => r.Item.ShortcutId == shortcutId))
            row.Subtitle = text is null ? row.Item.Subtitle : text.Replace("\n", " ");
    }

    public void Open(string? presetInput)
    {
        _preset = string.IsNullOrWhiteSpace(presetInput) ? null : presetInput;
        _locked = null;
        Box.Text = "";
        UpdateChrome();
        _openedAt = DateTime.UtcNow;
        _host.Theme.Refresh(this);
        _ = RefreshAsync();

        WindowPlacement.ShowCentered(this, w => WindowPlacement.PlaceTopCenter(w, 0.18));
        AutomationPortal.Editor.Motion.Animate(Slide, TranslateTransform.YProperty, -10, 0, AutomationPortal.Editor.MotionRole.Enter);
        Box.Focus();
        Keyboard.Focus(Box);
        _openedAt = DateTime.UtcNow;
    }

    // ---- searching -------------------------------------------------------------------------

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        Placeholder.Visibility = Box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_locked is null) _ = RefreshAsync();
        else ShowLockedRow();
    }

    private async Task RefreshAsync()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var ct = cts.Token;
        var query = Box.Text;
        var context = new QuickRunContext { PresetInput = _preset };
        try
        {
            var tasks = _host.QuickRunProviders.Select(async p =>
            {
                try { return await p.QueryAsync(query, context, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { AppLog.Write("Quick Run provider failed: " + ex.Message); return Array.Empty<QuickRunItem>(); }
            }).ToList();
            var all = (await Task.WhenAll(tasks)).SelectMany(x => x).OrderByDescending(i => i.Score).Take(8).ToList();
            if (ct.IsCancellationRequested) return;

            _rows.Clear();
            foreach (var item in all)
            {
                var row = new QuickRunRow(item);
                if (item.ShortcutId is { } sid && _host.Runs.IsRunning(sid) && _host.Runs.LatestProgress(sid) is { } p) row.Subtitle = (p.CountText ?? p.Text).Replace("\n", " ");
                _rows.Add(row);
            }
            var typed = query.Trim();
            HideAnswer();
            if (_locked is null && QuickRunFallbacks.Applies(typed, all))
            {
                // Nothing of the user's matched: ask the AI, or describe a new shortcut, instead of a dead end.
                var (ready, privacy, _) = QuickAskService.Status(_host.Services);
                foreach (var item in QuickRunFallbacks.Build(typed, ready, privacy, () => AskInlineAsync(typed), () => { GetAiReadySheet.Show(_host); return Task.CompletedTask; }, () => MakeShortcut(typed)))
                    _rows.Add(new QuickRunRow(item));
            }
            ResultList.Visibility = _rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            NoMatches.Visibility = Visibility.Collapsed;
            if (_rows.Count > 0) ResultList.SelectedIndex = 0;
        }
        catch (OperationCanceledException) { }
    }

    private void ShowLockedRow()
    {
        if (_locked is null) return;
        var item = _locked;
        var typed = Box.Text;
        _rows.Clear();
        _rows.Add(new QuickRunRow(new QuickRunItem
        {
            Id = item.Id, Title = "Run with input: " + (typed.Length == 0 ? "…" : typed), Subtitle = item.Title,
            Icon = item.Icon, Color = item.Color, Score = item.Score, ActionLabel = "Run", ShortcutId = item.ShortcutId,
            AcceptsInput = true, ExecuteAsync = item.ExecuteAsync,
        }));
        ResultList.SelectedIndex = 0;
        ResultList.Visibility = Visibility.Visible;
        NoMatches.Visibility = Visibility.Collapsed;
    }

    private string? _hint;

    private void UpdateChrome()
    {
        if (_locked is not null)
        {
            Chip.Visibility = Visibility.Visible;
            ChipText.Text = _locked.Title;
            Placeholder.Text = Strings.Get("QuickRunWindow_TypeTheInput");
            Hints.Text = LockedHints;
        }
        else if (_preset is not null)
        {
            Chip.Visibility = Visibility.Visible;
            var oneLine = _preset.Replace('\r', ' ').Replace('\n', ' ');
            ChipText.Text = "Selected text: " + (oneLine.Length > 28 ? oneLine[..28] + "…" : oneLine);
            Placeholder.Text = Strings.Get("QuickRunWindow_ChooseAShortcutTo");
            Hints.Text = NormalHints.Replace("Tab give it text", "Tab type other text");
        }
        else
        {
            Chip.Visibility = Visibility.Collapsed;
            Placeholder.Text = _hint ??= QuickRunHints.Next();
            Hints.Text = NormalHints;
        }
        Placeholder.Visibility = Box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- keys and clicks -------------------------------------------------------------------

    private void OnBoxKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Hide();
                e.Handled = true;
                break;
            case Key.Down:
                Move(1); e.Handled = true; break;
            case Key.Up:
                Move(-1); e.Handled = true; break;
            case Key.Enter when Keyboard.Modifiers == ModifierKeys.Control && ResultList.SelectedItem is QuickRunRow { Item.ShortcutId: { } editId }:
                Hide(); _host.EditorLauncher.Open(editId); e.Handled = true; break;
            case Key.Enter:
                _ = ExecuteSelectedAsync(); e.Handled = true; break;
            case Key.Tab:
                if (_locked is null && ResultList.SelectedItem is QuickRunRow row && row.Item.AcceptsInput)
                {
                    _locked = row.Item;
                    Box.Text = "";
                    UpdateChrome();
                    ShowLockedRow();
                }
                e.Handled = true;
                break;
            case Key.Back when _locked is not null && Box.Text.Length == 0:
                _locked = null;
                UpdateChrome();
                _ = RefreshAsync();
                e.Handled = true;
                break;
        }
    }

    private void Move(int delta)
    {
        if (_rows.Count == 0) return;
        var i = ResultList.SelectedIndex + delta;
        if (i < 0) i = _rows.Count - 1;
        if (i >= _rows.Count) i = 0;
        ResultList.SelectedIndex = i;
        ResultList.ScrollIntoView(ResultList.SelectedItem);
    }

    private void OnRowClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: QuickRunRow row })
        {
            ResultList.SelectedItem = row;
            _ = ExecuteSelectedAsync();
            e.Handled = true;
        }
    }

    private async Task ExecuteSelectedAsync()
    {
        if (ResultList.SelectedItem is not QuickRunRow row) return;
        var typed = Box.Text;
        var input = _locked is not null ? typed : _preset;
        var item = row.Item;
        if (!item.KeepsWindowOpen) Hide();
        try
        {
            await item.ExecuteAsync(new QuickRunInvocation { Query = typed, Input = input });
        }
        catch (UserCancelledException) { }
        catch (Exception ex)
        {
            AppLog.Write("Quick Run failed: " + ex);
            _host.Notifications.Show(Strings.Get("QuickRunWindow_QuickRunCouldntDo"), ex is ActionException ? ex.Message : Strings.Get("QuickRunWindow_SomethingWentWrongTry"));
        }
    }

    // ---- Ask AI and Make a shortcut fall-backs ----------------------------------------------

    private CancellationTokenSource? _askCts;
    private string _answer = "";

    private void CancelAsk()
    {
        _askCts?.Cancel();
        _askCts = null;
    }

    private void HideAnswer()
    {
        CancelAsk();
        if (AnswerPanel.Visibility != Visibility.Visible) return;
        AnswerPanel.Visibility = Visibility.Collapsed;
        UpdateChrome();
    }

    private void ShowAnswer(string text, string privacy, bool canCopy)
    {
        _answer = canCopy ? text : "";
        AnswerText.Text = text;
        AnswerPrivacy.Text = privacy;
        AnswerPrivacy.Visibility = privacy.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CopyAnswer.Content = Strings.Get("QuickRunAnswer_Copy");
        CopyAnswer.Visibility = canCopy ? Visibility.Visible : Visibility.Collapsed;
        AnswerPanel.Visibility = Visibility.Visible;
        Hints.Text = Strings.Get("QuickRunWindow_AnswerHints");
    }

    private async Task AskInlineAsync(string question)
    {
        CancelAsk();
        var cts = _askCts = new CancellationTokenSource();
        var (_, privacy, name) = QuickAskService.Status(_host.Services);
        ShowAnswer(string.Format(Strings.Get("QuickRunAnswer_Asking"), name.Length > 0 ? name : "AI"), privacy, canCopy: false);
        try
        {
            var selected = _preset;
            var result = await Task.Run(() => QuickAskService.AskAsync(_host.Services, question, selected, cts.Token));
            if (cts.IsCancellationRequested) return;
            if (result.NeedsSetup) { Hide(); GetAiReadySheet.Show(_host); return; }
            ShowAnswer(result.Answer ?? result.Problem ?? "", result.Ok ? result.Privacy : "", result.Ok);
        }
        catch (OperationCanceledException) { }
    }

    private Task MakeShortcut(string description)
    {
        if (ShortcutAiService.IsReady(_host.Services)) _host.EditorLauncher.OpenNew(null, new AutomationPortal.Editor.EditorOpenOptions(Describe: description));
        else GetAiReadySheet.Show(_host);
        return Task.CompletedTask;
    }

    private async void OnCopyAnswer(object sender, RoutedEventArgs e)
    {
        if (_answer.Length == 0) return;
        try
        {
            await _host.Services.Clipboard.SetAsync(AutomationPortal.Core.ContentValue.Text(_answer));
            CopyAnswer.Content = Strings.Get("QuickRunAnswer_Copied");
        }
        catch (Exception ex) { AppLog.Write("Quick Run copy failed: " + ex.Message); }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_host.IsExiting) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
}
