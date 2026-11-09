using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using AutomationPortal.Core;
using AutomationPortal.Editor.ViewModels;
using Wpf.Ui.Controls;

namespace AutomationPortal.Editor.UI;

/// <summary>Bottom drawer: run status, the final output and a live variables inspector.</summary>
internal sealed class ResultsDrawer : Border
{
    private const double OpenHeight = 250;
    private readonly EditorViewModel _vm;
    private readonly Border _body = new() { Height = 0, ClipToBounds = true };
    private readonly ContentControl _content = new() { Focusable = false };
    private readonly SymbolIcon _chevron;
    private readonly TextBlock _status;
    private readonly ContentControl _statusIcon = new() { Focusable = false };
    private readonly ToggleButton _resultTab;
    private readonly ToggleButton _varsTab;
    private readonly Wpf.Ui.Controls.Button _details;
    private readonly HashSet<string> _expandedVars = new();
    private bool _open;
    private bool _showVars;

    public ResultsDrawer(EditorViewModel vm)
    {
        _vm = vm;
        BorderThickness = new Thickness(0, 1, 0, 0);
        this.Dyn(BorderBrushProperty, Ui.CardStroke).Dyn(BackgroundProperty, "LayerFillColorDefaultBrush");

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Height = 42, Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _statusIcon.Margin = new Thickness(16, 0, 8, 0);
        header.Children.Add(_statusIcon);
        _status = Ui.Text("Ready", Ui.Primary, 13.5);
        Grid.SetColumn(_status, 1);
        header.Children.Add(_status);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
        _details = Ui.TextButton("See run details", () =>
        {
            if (_vm.LastResult is { } r) _vm.Context.Host.ShowRunDetails(r.RunId);
        }, null, ControlAppearance.Transparent);
        _details.Visibility = Visibility.Collapsed;
        right.Children.Add(_details);
        _chevron = Ui.Icon(SymbolRegular.ChevronUp20, 16, Ui.Secondary);
        _chevron.Margin = new Thickness(6, 0, 6, 0);
        right.Children.Add(_chevron);
        Grid.SetColumn(right, 2);
        header.Children.Add(right);
        header.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && Ui.FindAncestor<Wpf.Ui.Controls.Button>(d) is not null) return;
            SetOpen(!_open);
        };
        header.ToolTip = "Show or hide the result and variables";
        root.Children.Add(header);

        var bodyGrid = new Grid();
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 6) };
        _resultTab = Tab("Result", () => { _showVars = false; Render(); });
        _varsTab = Tab("Variables", () => { _showVars = true; Render(); });
        tabs.Children.Add(_resultTab);
        tabs.Children.Add(_varsTab);
        bodyGrid.Children.Add(tabs);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16, 4, 16, 12), Content = _content, Height = OpenHeight - 46 };
        Grid.SetRow(scroll, 1);
        bodyGrid.Children.Add(scroll);
        _body.Child = bodyGrid;
        Grid.SetRow(_body, 1);
        root.Children.Add(_body);
        Child = root;

        _vm.PropertyChanged += OnVmChanged;
        _vm.Inspector.CollectionChanged += (_, _) => { if (_open && _showVars) RenderVariables(); UpdateTabs(); };
        UpdateStatus();
        Render();
    }

    private ToggleButton Tab(string text, Action click)
    {
        var b = new ToggleButton { Content = text, Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 6, 0), FontSize = 13 };
        b.Click += (_, _) => click();
        return b;
    }

    public void SetOpen(bool open)
    {
        if (_open == open) return;
        _open = open;
        _body.BeginAnimation(HeightProperty, new DoubleAnimation(open ? OpenHeight : 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        _chevron.Symbol = open ? SymbolRegular.ChevronDown20 : SymbolRegular.ChevronUp20;
        if (open) Render();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(EditorViewModel.StatusText):
            case nameof(EditorViewModel.IsRunning):
                UpdateStatus();
                break;
            case nameof(EditorViewModel.LastResult):
                UpdateStatus();
                _showVars = false;
                Render();
                if (_vm.LastResult is { } r && (r.Status == RunStatus.Failed || (r.Status == RunStatus.Succeeded && r.Output.Count > 0))) SetOpen(true);
                break;
        }
    }

    private void UpdateStatus()
    {
        _status.Text = _vm.StatusText;
        FrameworkElement icon;
        if (_vm.IsRunning) icon = new ProgressRing { IsIndeterminate = true, Width = 18, Height = 18 };
        else if (_vm.LastResult is { } r)
            icon = r.Status switch
            {
                RunStatus.Succeeded => Ui.Icon(SymbolRegular.CheckmarkCircle20, 18, Ui.Success),
                RunStatus.Failed => Ui.Icon(SymbolRegular.DismissCircle20, 18, Ui.Critical),
                _ => Ui.Icon(SymbolRegular.Stop20, 18, Ui.Secondary),
            };
        else icon = Ui.Icon(SymbolRegular.Play20, 18, Ui.Tertiary);
        _statusIcon.Content = icon;
        _details.Visibility = _vm.LastResult is not null && !_vm.IsRunning ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTabs()
    {
        _resultTab.IsChecked = !_showVars;
        _varsTab.IsChecked = _showVars;
        _varsTab.Content = _vm.Inspector.Count > 0 ? $"Variables ({_vm.Inspector.Count})" : "Variables";
    }

    private void Render()
    {
        UpdateTabs();
        if (_showVars) RenderVariables(); else RenderResult();
    }

    private void RenderResult()
    {
        var r = _vm.LastResult;
        var stack = new StackPanel();
        if (r is null)
        {
            stack.Children.Add(Ui.Text(_vm.IsRunning ? "Running… the result appears here when it's done." : "Run the shortcut to see what it makes. The result of the last step shows up here.", Ui.Secondary, 13.5, wrap: true));
        }
        else
        {
            if (r.Status == RunStatus.Failed)
            {
                var b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 12) };
                b.SetResourceReference(Border.BackgroundProperty, "SystemFillColorCriticalBackgroundBrush");
                var s = new StackPanel();
                s.Children.Add(Ui.Text("It didn't finish", Ui.Primary, 14, FontWeights.SemiBold));
                s.Children.Add(Ui.Text(r.ErrorMessage ?? "Something went wrong.", Ui.Primary, 13.5, wrap: true));
                b.Child = s;
                stack.Children.Add(b);
            }
            if (r.Output.Count > 0)
            {
                var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var copy = Ui.TextButton("Copy", () =>
                {
                    try { Clipboard.SetText(r.Output.AsText()); } catch { }
                }, SymbolRegular.Copy20);
                DockPanel.SetDock(copy, Dock.Right);
                head.Children.Add(copy);
                head.Children.Add(Ui.Text("Result", Ui.Secondary, 12, FontWeights.SemiBold));
                stack.Children.Add(head);
                stack.Children.Add(ContentView.Build(r.Output));
            }
            else if (r.Status != RunStatus.Failed)
                stack.Children.Add(Ui.Text(r.Status == RunStatus.Cancelled ? "You stopped it before it finished." : "It finished without a result. Add \"Show Result\" at the end to see something here.", Ui.Secondary, 13.5, wrap: true));
        }
        _content.Content = stack;
    }

    private void RenderVariables()
    {
        var stack = new StackPanel();
        if (_vm.Inspector.Count == 0)
        {
            stack.Children.Add(Ui.Text("Run the shortcut and every step's result and every variable you set will appear here, live.", Ui.Secondary, 13.5, wrap: true));
            _content.Content = stack;
            return;
        }
        foreach (var item in _vm.Inspector)
        {
            var card = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 6), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
            card.SetResourceReference(Border.BorderBrushProperty, Ui.CardStroke);
            card.SetResourceReference(Border.BackgroundProperty, Ui.CardFill);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 140, MaxWidth = 260 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var chip = Ui.IconChip(item.Icon, item.ColorKey, 24, 14, 6);
            chip.Margin = new Thickness(0, 0, 10, 0);
            grid.Children.Add(chip);
            var name = Ui.Text(item.Name, Ui.Primary, 13.5, FontWeights.SemiBold);
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);
            var kind = Ui.Text(item.Kind, Ui.Tertiary, 12);
            kind.Margin = new Thickness(8, 0, 12, 0);
            Grid.SetColumn(kind, 2);
            grid.Children.Add(kind);
            var preview = Ui.Text(item.Preview, Ui.Secondary, 13);
            Grid.SetColumn(preview, 3);
            grid.Children.Add(preview);
            var expanded = _expandedVars.Contains(item.Name);
            if (expanded)
            {
                var detail = ContentView.Build(item.Value, 160);
                detail.Margin = new Thickness(34, 8, 0, 0);
                Grid.SetRow(detail, 1);
                Grid.SetColumnSpan(detail, 4);
                grid.Children.Add(detail);
            }
            card.Child = grid;
            card.MouseLeftButtonUp += (_, _) =>
            {
                if (!_expandedVars.Remove(item.Name)) _expandedVars.Add(item.Name);
                RenderVariables();
            };
            card.ToolTip = "Click to " + (expanded ? "collapse" : "see the full value");
            stack.Children.Add(card);
        }
        _content.Content = stack;
    }
}
