using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AutomationPortal.Core;
using AutomationPortal.Editor.Logic;
using AutomationPortal.Editor.ViewModels;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;

namespace AutomationPortal.Editor.UI;

/// <summary>One row of the action list: an action card, a slim bar (Otherwise / menu option / End) or an "unavailable" card, plus the hover "+".</summary>
internal sealed class ActionRowView : Grid
{
    public const double GutterWidth = 22;

    private readonly Grid _gutter = new();
    private readonly StackPanel _stack = new();
    private readonly Dictionary<string, TokenView> _tokens = new();
    private ActionRowViewModel? _vm;
    private Border? _card;
    private Wpf.Ui.Controls.Button? _menuButton;
    private ContentControl? _statusHost;
    private StackPanel? _banners;
    private ContentControl? _moreHost;
    private TextBlock? _moreLink;
    private ParameterForm? _moreForm;
    private IReadOnlyList<ParameterDescriptor> _remaining = Array.Empty<ParameterDescriptor>();
    private int _editing;
    private bool _dirtyWhileEditing;
    private bool _hover;
    private bool _subscribedChoices;

    public ActionRowView()
    {
        EditorResources.Ensure();
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition());
        Children.Add(_gutter);
        Grid.SetColumn(_stack, 1);
        Children.Add(_stack);
        Background = Brushes.Transparent;
        MaxWidth = 880;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        DataContextChanged += (_, _) => Bind(DataContext as ActionRowViewModel);
        Unloaded += (_, _) => Unsubscribe();
        Loaded += (_, _) => { if (_vm is not null) Subscribe(_vm); };
        MouseEnter += (_, _) => { _hover = true; UpdateChrome(); };
        MouseLeave += (_, _) => { _hover = false; UpdateChrome(); };
        PreviewMouseRightButtonDown += (_, _) => EnsureSelected();
    }

    public ActionRowViewModel? ViewModel => _vm;

    /// <summary>Opens the card menu (keyboard: Menu key / Shift+F10).</summary>
    public void OpenMenu() { if (_vm is not null) ShowMenu((FrameworkElement?)_card ?? (FrameworkElement?)_bar ?? this); }

    // ---- binding -------------------------------------------------------------------------------

    private void Bind(ActionRowViewModel? vm)
    {
        Unsubscribe();
        _vm = vm;
        if (vm is null) return;
        Subscribe(vm);
        Rebuild();
    }

    private void Subscribe(ActionRowViewModel vm)
    {
        vm.PropertyChanged -= OnVmChanged;
        vm.PropertyChanged += OnVmChanged;
        vm.Refresh -= OnRefresh;
        vm.Refresh += OnRefresh;
        if (!_subscribedChoices) { vm.Owner.Env.ChoicesLoaded += OnChoicesLoaded; _subscribedChoices = true; }
    }

    private void Unsubscribe()
    {
        if (_vm is null) return;
        _vm.PropertyChanged -= OnVmChanged;
        _vm.Refresh -= OnRefresh;
        if (_subscribedChoices) { _vm.Owner.Env.ChoicesLoaded -= OnChoicesLoaded; _subscribedChoices = false; }
    }

    private void OnChoicesLoaded() => UpdateTokenValues();

    private void OnRefresh()
    {
        if (_editing > 0 || _moreForm is { IsKeyboardFocusWithin: true } || (_bar is { IsKeyboardFocusWithin: true }))
        {
            _dirtyWhileEditing = true;
            UpdateTokenValues();
            return;
        }
        Rebuild();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ActionRowViewModel.IsSelected): UpdateChrome(); break;
            case nameof(ActionRowViewModel.HasBreakpoint): UpdateStatus(); break;
            case nameof(ActionRowViewModel.State):
            case nameof(ActionRowViewModel.DurationText):
            case nameof(ActionRowViewModel.Error): UpdateStatus(); UpdateBanners(); UpdateChrome(); break;
            case nameof(ActionRowViewModel.Issues): UpdateBanners(); UpdateTokenValues(); break;
            case nameof(ActionRowViewModel.Depth): UpdateGutter(); break;
            case nameof(ActionRowViewModel.ShowMore): UpdateShowMore(); break;
        }
    }

    private void EnsureSelected()
    {
        if (_vm is null || _vm.IsSelected) return;
        foreach (var r in _vm.Owner.Rows) r.IsSelected = false;
        _vm.IsSelected = true;
    }

    // ---- building ------------------------------------------------------------------------------

    private FrameworkElement? _body;
    private Border? _bar;

    private void Rebuild()
    {
        if (_vm is null) return;
        _dirtyWhileEditing = false;
        _stack.Children.Clear();
        _tokens.Clear();
        _card = null; _bar = null; _menuButton = null; _statusHost = null; _banners = null; _moreHost = null; _moreLink = null; _moreForm = null;
        _body = _vm.IsUnknown ? BuildUnknown() : _vm.IsBar ? BuildBar() : BuildCard();
        _stack.Children.Add(_body);
        _stack.Children.Add(BuildInsertZone());
        UpdateGutter();
        UpdateTokenValues();
        UpdateStatus();
        UpdateBanners();
        UpdateShowMore();
        UpdateChrome();
    }

    private void UpdateGutter()
    {
        if (_vm is null) return;
        _gutter.Children.Clear();
        _gutter.Width = _vm.Depth * GutterWidth;
        for (var i = 0; i < _vm.Depth; i++)
        {
            var line = new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(i * GutterWidth + GutterWidth / 2 - 1, 0, 0, 0), CornerRadius = new CornerRadius(1) };
            line.SetResourceReference(Border.BackgroundProperty, "ControlStrongStrokeColorDefaultBrush");
            line.Opacity = 0.45;
            _gutter.Children.Add(line);
        }
    }

    private string? CurrentText(string key)
    {
        var a = _vm!.Action;
        var v = a.Parameters.TryGetValue(key, out var x) ? x : _vm.Descriptor?.FindParameter(key)?.Default;
        return v switch
        {
            ChoiceValue c => c.Value,
            TextValue t => t.ToLiteralText(),
            BoolValue b => b.Value ? "true" : "false",
            NumberValue n => ContentFormat.Number(n.Value),
            null => null,
            _ => v.ToLiteralText(),
        };
    }

    private FrameworkElement BuildCard()
    {
        var vm = _vm!;
        var d = vm.Descriptor!;
        var env = vm.Owner.Env;
        _card = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(12, 10, 10, 10) };
        _card.SetResourceReference(Border.BackgroundProperty, Ui.CardFill);
        _card.SetResourceReference(Border.BorderBrushProperty, Ui.CardStroke);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _card.Child = grid;

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(top);

        var chip = Ui.IconChip(d.Icon, d.Color, 34, 19, 9);
        chip.Margin = new Thickness(0, 0, 12, 0);
        chip.VerticalAlignment = VerticalAlignment.Top;
        chip.ToolTip = d.Title;
        top.Children.Add(chip);

        // sentence
        var sentenceBox = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MinHeight = 34 };
        var wrap = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var segments = SummaryLayout.Agree(SummaryLayout.Visible(d.SummarySegments, k => d.FindParameter(k) is not { } vp || vp.IsVisible(CurrentText)),
            k => vm.Action.Parameters.TryGetValue(k, out var pv) ? pv : d.FindParameter(k)?.Default, out _);
        var summaryKeys = new HashSet<string>(segments.Where(s => s.IsToken).Select(s => s.ParamKey!));
        if (segments.Count == 0 || segments[0].IsToken)
        {
            var title = Ui.Text(d.Title + (segments.Count == 0 ? "" : " "), Ui.Primary, 14, FontWeights.SemiBold);
            title.TextTrimming = TextTrimming.None;
            wrap.Children.Add(title);
        }
        foreach (var seg in segments)
        {
            if (!seg.IsToken)
            {
                var t = Ui.Text(seg.Text, Ui.Primary, 14);
                t.TextTrimming = TextTrimming.None;
                wrap.Children.Add(t);
                continue;
            }
            var p = d.FindParameter(seg.ParamKey!);
            if (p is null) { wrap.Children.Add(Ui.Text(seg.Text, Ui.Secondary, 14)); continue; }
            if (!p.IsVisible(CurrentText)) continue;
            var token = new TokenView(p, env);
            token.Activated += OpenTokenEditor;
            _tokens[p.Key] = token;
            wrap.Children.Add(token);
        }
        sentenceBox.Children.Add(wrap);

        _remaining = d.Parameters
            .Where(p => !summaryKeys.Contains(p.Key) && p.IsVisible(CurrentText) && !(d.FlowKind == FlowKind.Menu && p.Key == "items"))
            .ToList();
        if (_remaining.Count > 0)
        {
            _moreLink = new TextBlock { FontSize = 13, Margin = new Thickness(4, 2, 0, 0), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left, Focusable = false };
            _moreLink.SetResourceReference(TextBlock.ForegroundProperty, Ui.Secondary);
            _moreLink.MouseEnter += (_, _) => _moreLink.TextDecorations = TextDecorations.Underline;
            _moreLink.MouseLeave += (_, _) => _moreLink.TextDecorations = null;
            _moreLink.MouseLeftButtonUp += (_, e) => { e.Handled = true; vm.ShowMore = !vm.ShowMore; };
            sentenceBox.Children.Add(_moreLink);
        }
        Grid.SetColumn(sentenceBox, 1);
        top.Children.Add(sentenceBox);

        // right side: badges, status, menu
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        if (d.IsDestructiveFor(vm.Action))
        {
            var warn = Ui.Icon(SymbolRegular.Warning20, 18, Ui.Caution);
            warn.Margin = new Thickness(0, 7, 6, 0);
            warn.ToolTip = "This action can change or delete things. Double-check before you run it.";
            if (vm.Owner.AiStepUids.Contains(vm.Uid))
            {
                var check = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 2), Margin = new Thickness(0, 6, 6, 0), ToolTip = "The AI added this step and it can change or delete things. Check it before you run the shortcut." };
                check.SetResourceReference(Border.BackgroundProperty, "SystemFillColorCautionBackgroundBrush");
                check.Child = Ui.Text("Check this step", Ui.Caution, 12, FontWeights.SemiBold);
                right.Children.Add(check);
                _card.SetResourceReference(Border.BorderBrushProperty, Ui.Caution);
                _card.BorderThickness = new Thickness(2);
            }
            right.Children.Add(warn);
        }
        if (vm.Action.Disabled)
        {
            var off = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 2), Margin = new Thickness(0, 6, 6, 0), ToolTip = "This action is turned off and is skipped when the shortcut runs." };
            off.SetResourceReference(Border.BackgroundProperty, Ui.SubtleHover);
            off.Child = Ui.Text("Off", Ui.Secondary, 12);
            right.Children.Add(off);
        }
        _statusHost = new ContentControl { Margin = new Thickness(8, 4, 4, 0), Focusable = false };
        right.Children.Add(_statusHost);
        _menuButton = Ui.IconButton(SymbolRegular.MoreHorizontal24, "More options for this action", () => ShowMenu(_menuButton!), 30, 16);
        right.Children.Add(_menuButton);
        Grid.SetColumn(right, 2);
        top.Children.Add(right);

        _banners = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        Grid.SetRow(_banners, 1);
        grid.Children.Add(_banners);
        _moreHost = new ContentControl { Focusable = false };
        Grid.SetRow(_moreHost, 2);
        grid.Children.Add(_moreHost);

        _card.MouseLeftButtonUp += (_, e) => { };
        _card.ContextMenuOpening += (_, e) => e.Handled = true;
        _card.MouseRightButtonUp += (_, e) => { e.Handled = true; ShowMenu(_card); };
        if (vm.Action.Disabled) _card.Opacity = 0.6;
        return _card;
    }

    private FrameworkElement BuildUnknown()
    {
        var vm = _vm!;
        _card = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(12, 10, 10, 10) };
        _card.SetResourceReference(Border.BackgroundProperty, "SystemFillColorCautionBackgroundBrush");
        _card.SetResourceReference(Border.BorderBrushProperty, Ui.Caution);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Ui.Icon(SymbolRegular.Warning24, 24, Ui.Caution);
        icon.Margin = new Thickness(5, 0, 14, 0);
        grid.Children.Add(icon);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Ui.Text("This action isn't available", Ui.Primary, 14, FontWeights.SemiBold));
        texts.Children.Add(Ui.Text($"Automation Portal doesn't know \"{vm.Action.ActionId}\". Its settings are kept, so it works again if the action comes back. You can also delete it.", Ui.Secondary, 12.5, wrap: true));
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        _menuButton = Ui.IconButton(SymbolRegular.MoreHorizontal24, "More options", () => ShowMenu(_menuButton!), 30, 16);
        _menuButton.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(_menuButton, 2);
        grid.Children.Add(_menuButton);
        _card.Child = grid;
        _banners = new StackPanel();
        _card.MouseRightButtonUp += (_, e) => { e.Handled = true; ShowMenu(_card); };
        return _card;
    }

    private FrameworkElement BuildBar()
    {
        var vm = _vm!;
        var doc = vm.Owner.Doc;
        var a = vm.Action;
        var d = vm.Descriptor!;
        var kind = vm.FlowKind;
        _bar = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), MinHeight = 34 };
        _bar.SetResourceReference(Border.BackgroundProperty, Ui.SubtleHover);
        _bar.SetResourceReference(Border.BorderBrushProperty, Ui.CardStroke);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var stripe = new Border { Width = 4, CornerRadius = new CornerRadius(7, 0, 0, 7), Background = Palette.Solid(d.Color), Margin = new Thickness(-1, -1, 0, -1) };
        grid.Children.Add(stripe);

        FrameworkElement label;
        if (vm.Role == FlowRole.Middle && kind == FlowKind.Menu)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(Ui.Text("Option", Ui.Secondary, 13));
            var box = new System.Windows.Controls.TextBox
            {
                Text = a.Flow?.Label ?? "", MinWidth = 160, BorderThickness = new Thickness(0, 0, 0, 1), Background = Brushes.Transparent, Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center, FontSize = 14, FontWeight = FontWeights.SemiBold, Padding = new Thickness(2, 2, 2, 2),
                ToolTip = "The text of this menu option",
            };
            box.SetResourceReference(Control.ForegroundProperty, Ui.Primary);
            box.SetResourceReference(Control.BorderBrushProperty, Ui.ControlStroke);
            box.TextChanged += (_, _) => { if (box.IsKeyboardFocused || box.IsFocused) doc.SetMenuLabel(a, box.Text); };
            box.LostKeyboardFocus += (_, _) => { if (_dirtyWhileEditing) Dispatcher.BeginInvoke(new Action(Rebuild)); };
            System.Windows.Automation.AutomationProperties.SetName(box, "Menu option text");
            stack.Children.Add(box);
            label = stack;
        }
        else
        {
            var text = vm.Role == FlowRole.Middle ? "Otherwise" : kind switch
            {
                FlowKind.If => "End If", FlowKind.Menu => "End Menu", FlowKind.Repeat => "End Repeat", FlowKind.RepeatEach => "End Repeat", _ => "End",
            };
            var t = Ui.Text(text, Ui.Secondary, 13.5, FontWeights.SemiBold);
            t.Margin = new Thickness(12, 0, 0, 0);
            label = t;
        }
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var gid = a.Flow?.GroupId ?? "";
        if (vm.Role == FlowRole.End && kind == FlowKind.Menu)
            right.Children.Add(Ui.TextButton("Add option", () => { doc.AddMenuItem(gid); }, SymbolRegular.Add16, ControlAppearance.Transparent));
        if (vm.Role == FlowRole.End && kind == FlowKind.If && !doc.Actions.Any(x => x.Flow is { Role: FlowRole.Middle } f && f.GroupId == gid))
            right.Children.Add(Ui.TextButton("Add Otherwise", () => { doc.AddOtherwise(gid); }, SymbolRegular.Add16, ControlAppearance.Transparent));
        if (vm.Role == FlowRole.Middle)
        {
            var del = Ui.IconButton(SymbolRegular.Dismiss16, kind == FlowKind.Menu ? "Remove this menu option and its actions" : "Remove Otherwise and its actions", () =>
            {
                doc.Delete(new[] { a });
            }, 28, 12);
            right.Children.Add(del);
        }
        right.Margin = new Thickness(0, 0, 4, 0);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        _bar.Child = grid;
        _bar.MouseRightButtonUp += (_, e) => { e.Handled = true; ShowMenu(_bar); };
        return _bar;
    }

    private FrameworkElement BuildInsertZone()
    {
        var zone = new Grid { Height = 16, Background = Brushes.Transparent, Focusable = true, FocusVisualStyle = null };
        System.Windows.Automation.AutomationProperties.SetName(zone, "Add step below");
        var circle = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0, Cursor = Cursors.Hand, ToolTip = "Add an action here" };
        circle.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
        var plus = Ui.Icon(SymbolRegular.Add12, 12);
        plus.SetResourceReference(Control.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
        plus.HorizontalAlignment = HorizontalAlignment.Center;
        circle.Child = plus;
        zone.Children.Add(circle);
        void Fade(double to) => circle.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(120)));
        zone.GotKeyboardFocus += (_, _) => Fade(1);
        zone.LostKeyboardFocus += (_, _) => Fade(0);
        zone.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space) || _vm is null) return;
            e.Handled = true;
            QuickInsert.Show(circle, _vm.Owner, _vm.Owner.Doc.IndexOf(_vm.Action) + 1);
        };
        zone.MouseEnter += (_, _) => Fade(1);
        zone.MouseLeave += (_, _) => Fade(0);
        zone.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_vm is null) return;
            QuickInsert.Show(circle, _vm.Owner, _vm.Owner.Doc.IndexOf(_vm.Action) + 1);
        };
        return zone;
    }

    // ---- updates -------------------------------------------------------------------------------

    private void UpdateTokenValues()
    {
        if (_vm is null) return;
        var a = _vm.Action;
        var descriptor = _vm.Descriptor!;
        SummaryLayout.Agree(SummaryLayout.Visible(descriptor.SummarySegments, k => descriptor.FindParameter(k) is not { } vp || vp.IsVisible(CurrentText)),
            k => a.Parameters.TryGetValue(k, out var pv) ? pv : descriptor.FindParameter(k)?.Default, out var singular);
        foreach (var (key, token) in _tokens)
        {
            token.Singular = singular.Contains(key);
            a.Parameters.TryGetValue(key, out var v);
            var missing = _vm.Issues.Any(i => i.Code == "missingParameter" && i.ParameterKey == key);
            token.SetValue(v, missing);
        }
    }

    private void UpdateStatus()
    {
        if (_statusHost is null || _vm is null) return;
        FrameworkElement? content = null;
        switch (_vm.State)
        {
            case StepState.Running:
                content = new ProgressRing { IsIndeterminate = true, Width = 18, Height = 18, Margin = new Thickness(0, 5, 6, 0), ToolTip = "Running" };
                break;
            case StepState.Paused:
                content = new Border { Margin = new Thickness(0, 4, 6, 0), ToolTip = "Waiting for you. Press Next step to run this step.", Child = Ui.Icon(SymbolRegular.PauseCircle20, 18, "AccentFillColorDefaultBrush") };
                break;
            case StepState.Succeeded:
            case StepState.Failed:
                {
                    var ok = _vm.State == StepState.Succeeded;
                    var s = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 6, 0) };
                    s.Children.Add(Ui.Icon(ok ? SymbolRegular.CheckmarkCircle20 : SymbolRegular.DismissCircle20, 18, ok ? Ui.Success : Ui.Critical));
                    if (_vm.DurationText is { } dt)
                    {
                        var t = Ui.Text(dt, Ui.Secondary, 12);
                        t.Margin = new Thickness(5, 0, 0, 0);
                        s.Children.Add(t);
                    }
                    s.ToolTip = ok ? "This step finished" : "This step failed";
                    content = s;
                    break;
                }
        }
        if (content is null && _vm.HasBreakpoint)
            content = new Border { Margin = new Thickness(0, 4, 6, 0), ToolTip = "This step pauses when you run it here", Child = Ui.Icon(SymbolRegular.PauseCircle20, 18, Ui.Caution) };
        _statusHost.Content = content;
    }

    private void UpdateBanners()
    {
        if (_banners is null || _vm is null) return;
        _banners.Children.Clear();
        if (_vm.State == StepState.Failed && !string.IsNullOrWhiteSpace(_vm.Error))
            _banners.Children.Add(Banner(_vm.Error!, true, SymbolRegular.ErrorCircle20));
        foreach (var issue in _vm.Issues)
            _banners.Children.Add(Banner(issue.Message, issue.Severity == IssueSeverity.Error, issue.Severity == IssueSeverity.Error ? SymbolRegular.ErrorCircle20 : SymbolRegular.Warning20));
        _banners.Visibility = _banners.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static FrameworkElement Banner(string text, bool error, SymbolRegular icon)
    {
        var b = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 4) };
        b.SetResourceReference(Border.BackgroundProperty, error ? "SystemFillColorCriticalBackgroundBrush" : "SystemFillColorCautionBackgroundBrush");
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var i = Ui.Icon(icon, 16, error ? Ui.Critical : Ui.Caution);
        i.VerticalAlignment = VerticalAlignment.Top;
        i.Margin = new Thickness(0, 1, 8, 0);
        g.Children.Add(i);
        var t = Ui.Text(text, Ui.Primary, 13, wrap: true);
        t.TextTrimming = TextTrimming.None;
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        b.Child = g;
        return b;
    }

    private void UpdateShowMore()
    {
        if (_vm is null || _moreHost is null) return;
        if (_moreLink is not null) _moreLink.Text = _vm.ShowMore ? "Show Less ▴" : "Show more ▾";
        if (!_vm.ShowMore || _remaining.Count == 0)
        {
            _moreHost.Content = null;
            _moreForm = null;
            return;
        }
        var vm = _vm;
        var a = vm.Action;
        var form = new ParameterForm { ApplyChangesToValues = false };
        form.LoadInternal(_remaining, a.Parameters, vm.Owner.Env, () => vm.Owner.Env.VariablesAt(vm.Owner.Doc.IndexOf(a)));
        form.ParameterChanged += (_, e) => vm.Owner.Doc.SetParameter(a, e.Key, e.Value);
        _moreForm = form;
        var host = new Border { Margin = new Thickness(46, 12, 6, 2), Padding = new Thickness(0, 12, 0, 0), BorderThickness = new Thickness(0, 1, 0, 0), Child = form };
        host.SetResourceReference(Border.BorderBrushProperty, "DividerStrokeColorDefaultBrush");
        form.IsKeyboardFocusWithinChanged += (_, e) =>
        {
            if (e.NewValue is false && _dirtyWhileEditing) Dispatcher.BeginInvoke(new Action(Rebuild), System.Windows.Threading.DispatcherPriority.Background);
        };
        _moreHost.Content = host;
    }

    private void UpdateChrome()
    {
        if (_vm is null) return;
        var target = _card ?? _bar;
        if (target is null) return;
        var selected = _vm.IsSelected;
        var failed = _vm.State == StepState.Failed;
        var running = _vm.State is StepState.Running or StepState.Paused;
        var thick = selected || failed || running ? 2.0 : 1.0;
        if (_card is not null && !_vm.IsUnknown)
        {
            _card.BorderThickness = new Thickness(thick);
            _card.Padding = new Thickness(13 - thick, 11 - thick, 11 - thick, 11 - thick);
            _card.SetResourceReference(Border.BorderBrushProperty, failed ? Ui.Critical : selected || running ? "AccentFillColorDefaultBrush" : Ui.CardStroke);
        }
        else if (_bar is not null)
        {
            _bar.SetResourceReference(Border.BorderBrushProperty, selected ? "AccentFillColorDefaultBrush" : Ui.CardStroke);
        }
        if (_menuButton is not null) _menuButton.Opacity = selected || _hover || _menuButton.IsKeyboardFocusWithin ? 1 : 0.0;
    }

    // ---- editing a token -----------------------------------------------------------------------

    private void OpenTokenEditor(TokenView token)
    {
        if (_vm is null) return;
        var vm = _vm;
        var a = vm.Action;
        var doc = vm.Owner.Doc;
        var p = token.Param;
        a.Parameters.TryGetValue(p.Key, out var current);
        var view = new ParamEditorView(p, current, vm.Owner.Env, () => vm.Owner.Env.VariablesAt(doc.IndexOf(a)), value => doc.SetParameter(a, p.Key, value));
        var content = new StackPanel { MinWidth = 300 };
        var title = Ui.Text(p.Label, Ui.Primary, 15, FontWeights.SemiBold);
        title.Margin = new Thickness(0, 0, 0, 10);
        content.Children.Add(title);
        content.Children.Add(view);
        _editing++;
        var panel = FloatingPanel.Open(token, content);
        panel.Closed += () =>
        {
            _editing--;
            if (_editing == 0) Dispatcher.BeginInvoke(new Action(Rebuild), System.Windows.Threading.DispatcherPriority.Background);
        };
    }

    // ---- menu ----------------------------------------------------------------------------------

    private void ShowMenu(FrameworkElement anchor)
    {
        if (_vm is null) return;
        EnsureSelected();
        var vm = _vm;
        var owner = vm.Owner;
        var a = vm.Action;
        var d = vm.Descriptor;
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };

        MenuItem Item(string header, SymbolRegular icon, Action click, string? gesture = null, bool check = false)
        {
            var mi = new MenuItem { Header = header, Icon = Ui.Icon(icon, 16), InputGestureText = gesture ?? "", IsChecked = check };
            mi.Click += (_, _) => click();
            return mi;
        }

        if (vm.IsBar)
        {
            if (vm.Role == FlowRole.Middle) menu.Items.Add(Item(vm.FlowKind == FlowKind.Menu ? "Remove option" : "Remove Otherwise", SymbolRegular.Delete24, () => owner.Doc.Delete(new[] { a })));
            else menu.Items.Add(Item("Delete whole block", SymbolRegular.Delete24, () => owner.DeleteSelection(), "Del"));
            if (vm.Role == FlowRole.End)
            {
                menu.Items.Add(Item("Duplicate block", SymbolRegular.Copy24, () => owner.DuplicateSelection(), "Ctrl+D"));
                menu.Items.Add(Item("Copy block", SymbolRegular.DocumentCopy24, () => owner.CopySelection(), "Ctrl+C"));
            }
            menu.IsOpen = true;
            return;
        }

        menu.Items.Add(Item("Duplicate", SymbolRegular.Copy24, () => owner.DuplicateSelection(), "Ctrl+D"));
        menu.Items.Add(Item(a.Disabled ? "Turn on" : "Turn off", a.Disabled ? SymbolRegular.Eye24 : SymbolRegular.EyeOff24, () => owner.ToggleDisabled(owner.SelectedActions)));
        if (d is { HasOutput: true } && vm.Role is null)
            menu.Items.Add(Item("Rename result…", SymbolRegular.Edit24, () => RenameOutput(anchor)));
        if (d is not null)
        {
            var onError = new MenuItem { Header = "If this fails", Icon = Ui.Icon(SymbolRegular.ShieldError20, 16) };
            onError.Items.Add(Item("Stop the shortcut", SymbolRegular.Stop24, () => owner.Doc.SetOnError(a, ErrorMode.Stop), null, a.OnError.Mode == ErrorMode.Stop));
            onError.Items.Add(Item("Carry on", SymbolRegular.ArrowNext24, () => owner.Doc.SetOnError(a, ErrorMode.Continue), null, a.OnError.Mode == ErrorMode.Continue));
            onError.Items.Add(Item("Try again (2 more times)", SymbolRegular.ArrowRepeatAll24, () => owner.Doc.SetOnError(a, ErrorMode.Retry, 2, 1), null, a.OnError.Mode == ErrorMode.Retry));
            menu.Items.Add(onError);
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Run from here", SymbolRegular.Play24, () => _ = owner.RunAsync(owner.Doc.IndexOf(a))));
            menu.Items.Add(Item(owner.Run.HasBreakpoint(a.Uid) ? "Don't pause here" : "Pause here", SymbolRegular.PauseCircle20, () => owner.ToggleBreakpoint(a.Uid)));
        }
        menu.Items.Add(Item("Copy", SymbolRegular.DocumentCopy24, () => owner.CopySelection(), "Ctrl+C"));
        if (d is not null) menu.Items.Add(Item("Help", SymbolRegular.Info24, () => FloatingPanel.Open(_card ?? anchor, HelpCard.Build(d))));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Delete", SymbolRegular.Delete24, () => owner.DeleteSelection(), "Del"));
        menu.IsOpen = true;
    }

    private void RenameOutput(FrameworkElement anchor)
    {
        if (_vm is null) return;
        var vm = _vm;
        var a = vm.Action;
        var d = vm.Descriptor!;
        var box = new Wpf.Ui.Controls.TextBox { Text = a.OutputName ?? "", PlaceholderText = d.EffectiveOutputName, MinWidth = 260 };
        var content = new StackPanel();
        content.Children.Add(Ui.Text("Rename result", Ui.Primary, 15, FontWeights.SemiBold));
        ((FrameworkElement)content.Children[0]).Margin = new Thickness(0, 0, 0, 6);
        content.Children.Add(Ui.Text("Give this action's result a name that's easy to spot when you pick variables.", Ui.Secondary, 12.5, wrap: true));
        ((FrameworkElement)content.Children[1]).Margin = new Thickness(0, 0, 0, 8);
        content.Children.Add(box);
        box.TextChanged += (_, _) => vm.Owner.Doc.SetOutputName(a, box.Text);
        _editing++;
        var panel = FloatingPanel.Open(_card ?? anchor, content);
        panel.Closed += () => { _editing--; if (_editing == 0) Dispatcher.BeginInvoke(new Action(Rebuild), System.Windows.Threading.DispatcherPriority.Background); };
    }
}
