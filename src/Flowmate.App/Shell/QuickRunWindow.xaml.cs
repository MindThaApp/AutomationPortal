using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Flowmate.App.Infrastructure;
using Flowmate.App.Integration;
using Flowmate.Core;
using Wpf.Ui.Controls;

namespace Flowmate.App.Shell;

public sealed class QuickRunRow
{
    public QuickRunRow(QuickRunItem item)
    {
        Item = item;
        Title = item.Title;
        Subtitle = item.Subtitle;
        ActionLabel = item.ActionLabel;
        Symbol = Icons.Parse(item.Icon);
        Brush = Palette.Gradient(item.Color);
    }

    public QuickRunItem Item { get; }
    public string Title { get; }
    public string? Subtitle { get; }
    public string ActionLabel { get; }
    public SymbolRegular Symbol { get; }
    public Brush Brush { get; }
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

    private const string NormalHints = "↑ ↓ choose      Enter run      Tab add input      Esc close";
    private const string LockedHints = "Type the input, then press Enter      Backspace go back      Esc close";

    public QuickRunWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        ResultList.ItemsSource = _rows;
        Deactivated += (_, _) => { if ((DateTime.UtcNow - _openedAt).TotalMilliseconds > 400) Hide(); };
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
        Slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(170)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
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
            foreach (var item in all) _rows.Add(new QuickRunRow(item));
            ResultList.Visibility = _rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            NoMatches.Visibility = _rows.Count == 0 && query.Trim().Length > 0 ? Visibility.Visible : Visibility.Collapsed;
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

    private void UpdateChrome()
    {
        if (_locked is not null)
        {
            Chip.Visibility = Visibility.Visible;
            ChipText.Text = _locked.Title;
            Placeholder.Text = "Type the input";
            Hints.Text = LockedHints;
        }
        else if (_preset is not null)
        {
            Chip.Visibility = Visibility.Visible;
            var oneLine = _preset.Replace('\r', ' ').Replace('\n', ' ');
            ChipText.Text = "Selected text: " + (oneLine.Length > 28 ? oneLine[..28] + "…" : oneLine);
            Placeholder.Text = "Choose a shortcut to run on it";
            Hints.Text = NormalHints.Replace("Tab add input", "Tab type other input");
        }
        else
        {
            Chip.Visibility = Visibility.Collapsed;
            Placeholder.Text = "Type a shortcut name";
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
        Hide();
        try
        {
            await item.ExecuteAsync(new QuickRunInvocation { Query = typed, Input = input });
        }
        catch (UserCancelledException) { }
        catch (Exception ex)
        {
            AppLog.Write("Quick Run failed: " + ex);
            _host.Notifications.Show("Quick Run couldn't do that", ex is ActionException ? ex.Message : "Something went wrong. Try again.");
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_host.IsExiting) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
}
