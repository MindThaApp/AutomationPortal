using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AutomationPortal.Core;
using AutomationPortal.Editor.Logic;
using AutomationPortal.Editor.UI;
using AutomationPortal.Editor.ViewModels;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;

namespace AutomationPortal.Editor;

/// <summary>The shortcut editor window: header, action list, action panel, results drawer.</summary>
internal sealed partial class ShortcutEditorWindow : FluentWindow
{
    private const string RowsFormat = "autoportal.rows";
    private static readonly Dictionary<Guid, ShortcutEditorWindow> OpenWindows = new();

    private readonly EditorViewModel _vm;
    private readonly ResultsDrawer _drawer;
    private Point _pressPoint;
    private ActionRowViewModel? _pressedRow;
    private bool _pressedInteractive;
    private bool _startedBlank;
    private bool _describing;

    public ShortcutEditorWindow(EditorViewModel vm)
    {
        EditorResources.Ensure();
        _vm = vm;
        DataContext = vm;
        InitializeComponent();

        ActionList.ItemsSource = vm.Rows;
        ActionPanel.DataContext = vm.Panel;
        _drawer = new ResultsDrawer(vm);
        DrawerHost.Content = _drawer;

        ApplyMetadata();
        InitWhenCard();
        vm.MetadataChanged += ApplyMetadata;
        vm.PropertyChanged += OnVmChanged;
        RecordButton.Visibility = vm.Context.Host.CanRecord ? Visibility.Visible : Visibility.Collapsed;
        RecordButton.Click += async (_, _) => await RecordAsync();
        InitRowMotion();
        vm.RequestScrollTo += ScrollTo;
        vm.StepAdded += FocusNewStep;
        vm.DeletedExternally += () => Dispatcher.BeginInvoke(new Action(Close));
        UpdateRunButton();
        UpdateEmptyState();

        NameBox.LostKeyboardFocus += (_, _) => CommitName();
        NameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { CommitName(); ActionList.Focus(); e.Handled = true; }
            else if (e.Key == Key.Escape) { NameBox.Text = _vm.Name; ActionList.Focus(); e.Handled = true; }
        };
        IconButton.Click += (_, _) => ChooseIcon();
        RunButton.Click += (_, _) => { if (_vm.IsRunning) _vm.Stop(); else _ = _vm.RunAsync(); };
        RunMenuButton.Click += (_, _) => ShowRunMenu();
        NextStepButton.Click += (_, _) => _vm.NextStep();
        RunToEndButton.Click += (_, _) => _vm.RunToEnd();
        DetailsButton.Click += (_, _) => ShowDetails();
        MoreButton.Click += (_, _) => ShowMoreMenu();
        AddActionButton.Click += (_, _) => QuickInsert.Show(AddActionButton, _vm, _vm.Doc.Actions.Count);

        ActionList.PreviewMouseLeftButtonDown += OnListMouseDown;
        ActionList.PreviewMouseMove += OnListMouseMove;
        ActionList.DragOver += OnDragOver;
        ActionList.DragLeave += (_, _) => HideDropLine();
        ActionList.Drop += OnDrop;
        PreviewKeyDown += OnKeyDown;
        Closing += OnClosing;
        Loaded += (_, _) =>
        {
            if (_vm.IsNew && _vm.IsEmpty) NameBox.Focus(); else ActionList.Focus();
            if (_vm.StartRecording) { _vm.StartRecording = false; _ = RecordAsync(); }
            if (_vm.SampleFiles is not null && NoteHost.Content is null) ShowSampleNote();
        };
    }

    /// <summary>The note for a shortcut started from files (automationportal://new, ap new): which files Run hands to it, and a way to stop using them.</summary>
    private void ShowSampleNote()
    {
        var files = _vm.SampleFiles!;
        var box = new Border { CornerRadius = Radius.Card, BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 12, 12) };
        box.SetResourceReference(Border.BackgroundProperty, UI.Ui.CardFill);
        box.SetResourceReference(Border.BorderBrushProperty, UI.Ui.CardStroke);
        var headline = (files.Count == 1 ? "Run tries this on 1 file" : $"Run tries this on {files.Count} files")
                       + (string.IsNullOrEmpty(_vm.SampleFrom) ? "" : $" from {_vm.SampleFrom}");
        System.Windows.Automation.AutomationProperties.SetName(box, headline);

        var stack = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = UI.Ui.Icon(Wpf.Ui.Controls.SymbolRegular.DocumentMultiple24, 18, UI.Ui.Accent);
        icon.Margin = new Thickness(0, 0, 10, 0);
        head.Children.Add(icon);
        var title = UI.Ui.Text(headline, UI.Ui.Primary, 14, FontWeights.SemiBold, wrap: true);
        Grid.SetColumn(title, 1);
        head.Children.Add(title);
        var stop = UI.Ui.TextButton("Don't use them", () => { _vm.SampleFiles = null; NoteHost.Content = null; }, null, Wpf.Ui.Controls.ControlAppearance.Transparent);
        stop.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(stop, 2);
        head.Children.Add(stop);
        stack.Children.Add(head);

        var shown = string.Join("\n", files.Take(5).Select(f => _vm.SampleMissing.Contains(f, StringComparer.OrdinalIgnoreCase) ? f + "  (not found)" : f));
        if (files.Count > 5) shown += $"\n…and {files.Count - 5} more";
        var list = UI.Ui.Text(shown, UI.Ui.Secondary, 12.5, wrap: true);
        list.Margin = new Thickness(28, 4, 0, 0);
        stack.Children.Add(list);
        var detail = Strings.Get("ShortcutEditorWindow_AddStepsThenPress");
        if (_vm.SampleMissing.Count > 0)
            detail += _vm.SampleMissing.Count == 1 ? " One of them couldn't be found." : $" {_vm.SampleMissing.Count} of them couldn't be found.";
        var d = UI.Ui.Text(detail, UI.Ui.Secondary, 12.5, wrap: true);
        d.Margin = new Thickness(28, 6, 0, 0);
        stack.Children.Add(d);
        box.Child = stack;
        NoteHost.Content = box;
    }

    /// <summary>Selects the step with this id and scrolls it into view. False when the shortcut no longer has that step.</summary>
    internal bool SelectStep(string uid)
    {
        var action = _vm.Doc.Actions.FirstOrDefault(a => a.Uid == uid);
        if (action is null) return false;
        _vm.SelectActions(new[] { action });
        ScrollTo(action);
        return true;
    }

    public static ShortcutEditorWindow? FindOpen(Guid id) => OpenWindows.TryGetValue(id, out var w) ? w : null;

    internal void Register()
    {
        OpenWindows[_vm.Shortcut.Id] = this;
        Closed += (_, _) => { if (OpenWindows.TryGetValue(_vm.Shortcut.Id, out var w) && w == this) OpenWindows.Remove(_vm.Shortcut.Id); };
    }

    // ---- header --------------------------------------------------------------------------------

    private void ApplyMetadata()
    {
        Title = _vm.Name;
        TitleBar.Title = _vm.Name;
        if (!NameBox.IsKeyboardFocused) NameBox.Text = _vm.Name;
        HeaderIcon.Symbol = IconNames.Resolve(_vm.Icon);
        IconButton.Background = Palette.Gradient(_vm.Color);
    }

    private void CommitName()
    {
        var error = _vm.TryRename(NameBox.Text);
        if (error is not null)
        {
            SaveText.Text = error;
            SaveText.SetResourceReference(TextBlock.ForegroundProperty, Ui.Critical);
            NameBox.Text = _vm.Name;
            return;
        }
        NameBox.Text = _vm.Name;
        UpdateSaveText();
    }

    private void UpdateSaveText()
    {
        if (_vm.SaveError is { Length: > 0 } err)
        {
            SaveText.Text = err;
            SaveText.SetResourceReference(TextBlock.ForegroundProperty, Ui.Critical);
        }
        else
        {
            SaveText.Text = _vm.SaveStatus;
            SaveText.SetResourceReference(TextBlock.ForegroundProperty, Ui.Tertiary);
        }
    }

    private void ChooseIcon()
    {
        var result = IconPickerDialog.Show(this, _vm.Icon, _vm.Color);
        if (result is { } r) _vm.SetAppearance(r.Icon, r.Color);
    }

    private void ShowDetails()
    {
        CommitName();
        var sheet = new DetailsSheet(_vm) { Owner = this };
        sheet.ShowDialog();
        ApplyMetadata();
    }

    /// <summary>The split button's menu: the less common ways to run. Next step and Run to end appear in the header only while paused.</summary>
    private void ShowRunMenu()
    {
        var menu = new ContextMenu { PlacementTarget = RunMenuButton, Placement = PlacementMode.Bottom };
        var selected = _vm.SelectedRows.Select(r => _vm.Doc.IndexOf(r.Action)).Where(i => i >= 0).DefaultIfEmpty(-1).Min();
        MenuItem Item(string header, SymbolRegular icon, Action click, string? gesture = null, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, Icon = new SymbolIcon { Symbol = icon }, InputGestureText = gesture ?? "", IsEnabled = enabled };
            mi.Click += (_, _) => click();
            return mi;
        }
        menu.Items.Add(Item("Run", SymbolRegular.Play24, () => { if (!_vm.IsRunning) _ = _vm.RunAsync(); }, "F5"));
        menu.Items.Add(Item(Strings.Get("ShortcutEditorWindow_RunStepByStep"), SymbolRegular.ArrowStepIn24, () => { if (!_vm.IsRunning) _ = _vm.RunStepByStepAsync(); }));
        menu.Items.Add(Item(Strings.Get("ShortcutEditorWindow_RunFromTheSelected"), SymbolRegular.Play24, () => { if (!_vm.IsRunning) _ = _vm.RunAsync(selected); }, null, selected >= 0));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Strings.Get("ShortcutEditorWindow_PreviewFileChanges"), SymbolRegular.Eye24, () => { if (!_vm.IsRunning) _ = PreviewFileChangesAsync(); }));
        menu.IsOpen = true;
    }

    /// <summary>Lists what a run would move, rename, make or delete, without changing anything; "Run for real" runs it after.</summary>
    private async Task PreviewFileChangesAsync()
    {
        FileChangePlan plan;
        try { plan = await _vm.PreviewFileChangesAsync(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            EditorDialog.Tell(this, Strings.Get("ShortcutEditorWindow_PreviewFileChanges"), "Couldn't make the preview: " + ex.Message);
            return;
        }
        var lines = plan.Changes.Select(c => "• " + c.Describe()).ToList();
        const int max = 200;
        var list = string.Join("\n", lines.Take(max)) + (lines.Count > max ? $"\n… and {lines.Count - max} more" : "");
        var message = plan.Summary() + (lines.Count > 0 ? "\n\n" + list : "") + "\n\nNothing has been changed.";
        // "Run for real" uses the files and folders picked for the preview (the shortcut's own and each step's), so it does what the
        // preview showed; anything picked that changed since is asked for again.
        if (EditorDialog.Ask(this, "Preview file changes", message, "Run for real", "Close") == DialogChoice.Primary && !_vm.IsRunning)
            await _vm.RunAsync(previewPicks: plan.Picks);
    }

    private void ShowMoreMenu()
    {
        var menu = new ContextMenu { PlacementTarget = MoreButton, Placement = PlacementMode.Bottom };
        MenuItem Item(string header, SymbolRegular icon, Action click, string? gesture = null, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, Icon = new SymbolIcon { Symbol = icon }, InputGestureText = gesture ?? "", IsEnabled = enabled };
            mi.Click += (_, _) => click();
            return mi;
        }
        menu.Items.Add(Item("Undo", SymbolRegular.ArrowUndo24, _vm.Undo, "Ctrl+Z", _vm.CanUndo));
        menu.Items.Add(Item("Redo", SymbolRegular.ArrowRedo24, _vm.Redo, "Ctrl+Y", _vm.CanRedo));
        if (_vm.Context.Host.CanDescribeWithAi)
            menu.Items.Add(Item(_vm.IsEmpty ? Strings.Get("ShortcutEditorWindow_DescribeIt2") : Strings.Get("ShortcutEditorWindow_DescribeAChange2"), SymbolRegular.Sparkle24, DescribeChange));
        else
            menu.Items.Add(Item("Describe a change… (set up AI in Settings › AI)", SymbolRegular.Sparkle24, _vm.Context.Host.OpenAiSettings));
        menu.Items.Add(Item(Strings.Get("ExplainFix_ExplainShortcut"), SymbolRegular.Sparkle24, ExplainShortcut, null, !_vm.IsEmpty));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Strings.Get("ShortcutEditorWindow_DuplicateShortcut"), SymbolRegular.Copy24, DuplicateShortcut));
        if (_vm.Context.Library is JsonShortcutLibrary)
            menu.Items.Add(Item(Strings.Get("ShortcutEditorWindow_VersionHistory"), SymbolRegular.History24, ShowVersionHistory, null, !_vm.DraftPending));
        menu.Items.Add(Item(Strings.Get("DetailsSheet_AddToDesktop"), SymbolRegular.Desktop24, () => { _vm.SaveNow(); _vm.Context.Host.AddToDesktop(_vm.Shortcut); }));
        menu.Items.Add(Item(Strings.Get("DetailsSheet_AddToStartMenu"), SymbolRegular.Apps24, () => { _vm.SaveNow(); _vm.Context.Host.AddToStartMenu(_vm.Shortcut); }));
        menu.Items.Add(Item("Export…", SymbolRegular.ArrowExportLtr24, () => { _vm.SaveNow(); _vm.Context.Host.Export(_vm.Shortcut); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Strings.Get("KeySheet_KeyboardShortcuts"), SymbolRegular.Keyboard24, () => UI.KeySheet.Show(this), "F1"));
        menu.Items.Add(Item(Strings.Get("ShortcutEditorWindow_ClearRunResults"), SymbolRegular.Broom24, _vm.ClearResults, null, _vm.LastResult is not null));
        menu.IsOpen = true;
    }

    private void DuplicateShortcut()
    {
        _vm.SaveNow();
        if (!_vm.Context.Host.AllowNewShortcut(this)) return;
        try
        {
            var result = _vm.Context.Library.Import(_vm.Shortcut.Clone());
            ShortcutEditor.Open(result.Shortcut.Id, _vm.Context, this);
        }
        catch (LibraryException ex)
        {
            SaveText.Text = ex.Message;
            SaveText.SetResourceReference(TextBlock.ForegroundProperty, Ui.Critical);
        }
    }

    private void ShowVersionHistory()
    {
        if (_vm.Context.Library is not JsonShortcutLibrary lib) return;
        CommitName();
        if (!_vm.SaveNow()) return;
        var dialog = new VersionHistoryWindow(lib, _vm.Shortcut.Id, _vm.Name, _vm.Context.Host.AllowNewShortcut) { Owner = this };
        _vm.AutosavePaused = true;
        try
        {
            dialog.ShowDialog();
            if (dialog.OpenedCopy is { } copyId) ShortcutEditor.Open(copyId, _vm.Context, this);
            else if (dialog.Restored && _vm.ReloadFromLibrary(ConfirmDiscardForRestore)) ApplyMetadata();
        }
        finally { _vm.AutosavePaused = false; }
    }

    private bool ConfirmDiscardForRestore() =>
        EditorDialog.Ask(this, Strings.Get("ShortcutEditorWindow_UnsavedChanges"),
            "Discard your unsaved changes and load the restored version?\n\nIf you keep your changes, they stay here and are saved over the restored version. The restored version stays in Version history.",
            Strings.Get("ShortcutEditorWindow_DiscardMyChanges"), Strings.Get("ShortcutEditorWindow_KeepMyChanges"), danger: true)
        == DialogChoice.Primary;

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(EditorViewModel.SaveStatus):
            case nameof(EditorViewModel.SaveError):
                UpdateSaveText();
                break;
            case nameof(EditorViewModel.IsRunning):
            case nameof(EditorViewModel.IsPaused):
                UpdateRunButton();
                break;
            case nameof(EditorViewModel.IsEmpty):
                UpdateEmptyState();
                break;
            case nameof(EditorViewModel.AiNote):
                OnAiNoteChanged();
                break;
        }
    }

    private void UpdateRunButton()
    {
        RunMenuButton.Visibility = _vm.IsRunning ? Visibility.Collapsed : Visibility.Visible;
        NextStepButton.Visibility = RunToEndButton.Visibility = _vm.IsRunning && _vm.IsPaused ? Visibility.Visible : Visibility.Collapsed;
        if (_vm.IsRunning)
        {
            RunButton.Content = "_Stop";
            RunButton.Icon = new SymbolIcon { Symbol = SymbolRegular.Stop24 };
            RunButton.Appearance = ControlAppearance.Danger;
            RunButton.ToolTip = Strings.Get("ShortcutEditorWindow_StopThisRunShiftF5");
            RunButton.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "Stop");
        }
        else
        {
            RunButton.Content = "_Run";
            RunButton.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "Run");
            RunButton.Icon = new SymbolIcon { Symbol = SymbolRegular.Play24 };
            RunButton.Appearance = ControlAppearance.Primary;
            RunButton.ToolTip = Strings.Get("ShortcutEditorWindow_RunThisShortcutF5");
            RunButton.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "Run");
        }
    }

    // ---- empty state / new shortcut ------------------------------------------------------------

    private void UpdateEmptyState()
    {
        var show = _vm.IsEmpty;
        EmptyState.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        AddActionButton.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        if (!show) return;
        EmptyState.Children.Clear();
        var chip = Ui.IconChip(_vm.IsNew && _vm.Context.Host.CanDescribeWithAi && !_startedBlank ? "Sparkle24" : "Add24", "blue", 64, 32, 16);
        chip.HorizontalAlignment = HorizontalAlignment.Center;
        chip.Margin = new Thickness(0, 0, 0, 16);
        EmptyState.Children.Add(chip);

        if (_vm.IsNew && !_startedBlank && _vm.Context.Host.CanDescribeWithAi)
        {
            BuildStartChoices();
            return;
        }
        var t = Ui.Text(Strings.Get("ShortcutEditorWindow_AddYourFirstAction"), Ui.Primary, 20, FontWeights.SemiBold);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        EmptyState.Children.Add(t);
        var d = Ui.Text(Strings.Get("ShortcutEditorWindow_PickAnActionFrom"), Ui.Secondary, 14, wrap: true);
        d.TextAlignment = TextAlignment.Center;
        d.Margin = new Thickness(0, 8, 0, 16);
        EmptyState.Children.Add(d);
        var find = Ui.TextButton(Strings.Get("QuickInsert_SearchActions"), () => ActionPanel.FocusSearch(), SymbolRegular.Search24, ControlAppearance.Primary);
        find.HorizontalAlignment = HorizontalAlignment.Center;
        EmptyState.Children.Add(find);
        if (_vm.IsNew && !_vm.Context.Host.CanDescribeWithAi) EmptyState.Children.Add(BuildAiHint());
    }

    // ---- scrolling -----------------------------------------------------------------------------

    private void ScrollTo(ActionInstance action)
    {
        var row = _vm.Rows.FirstOrDefault(r => ReferenceEquals(r.Action, action));
        if (row is null) return;
        Dispatcher.BeginInvoke(new Action(() => ActionList.ScrollIntoView(row)), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ---- keyboard ------------------------------------------------------------------------------

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

        if (UI.KeySheet.IsOpenKey(key, Keyboard.Modifiers)) { e.Handled = true; UI.KeySheet.Show(this); return; }
        if (key == Key.F5)
        {
            e.Handled = true;
            if (shift) _vm.Stop(); else if (!_vm.IsRunning) _ = _vm.RunAsync();
            return;
        }
        if (ctrl && key == Key.F) { e.Handled = true; ActionPanel.FocusSearch(); return; }

        if (Keyboard.FocusedElement is TextBoxBase or System.Windows.Controls.ComboBox) return;
        if (e.OriginalSource is DependencyObject src && Ui.FindAncestor<Popup>(src) is not null) return;

        switch (key)
        {
            case Key.C when ctrl: _vm.CopySelection(); e.Handled = true; break;
            case Key.X when ctrl: _vm.CutSelection(); e.Handled = true; break;
            case Key.V when ctrl: _vm.Paste(); e.Handled = true; break;
            case Key.D when ctrl: _vm.DuplicateSelection(); e.Handled = true; break;
            case Key.A when ctrl: _vm.SelectAll(); e.Handled = true; break;
            case Key.Z when ctrl && !shift: _vm.Undo(); e.Handled = true; break;
            case Key.Y when ctrl: _vm.Redo(); e.Handled = true; break;
            case Key.Z when ctrl && shift: _vm.Redo(); e.Handled = true; break;
            case Key.Delete: _vm.DeleteSelection(); e.Handled = true; break;
            case Key.Up when alt: _vm.MoveSelection(-1); e.Handled = true; break;
            case Key.Down when alt: _vm.MoveSelection(+1); e.Handled = true; break;
            case Key.Apps:
            case Key.F10 when shift:
                if (_vm.SelectedRows.FirstOrDefault() is { } row && ActionList.ItemContainerGenerator.ContainerFromItem(row) is DependencyObject c)
                    Ui.Descendants<ActionRowView>(c).FirstOrDefault()?.OpenMenu();
                e.Handled = true;
                break;
            case Key.Escape:
                foreach (var r in _vm.Rows) r.IsSelected = false;
                break;
        }
    }

    // ---- drag & drop ---------------------------------------------------------------------------

    private static bool IsInteractive(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is ButtonBase or TextBoxBase or System.Windows.Controls.ComboBox or ScrollBar or TokenView or ProgressRing or Popup or Thumb) return true;
            if (d is ListBoxItem) return false;
            d = d is Visual ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    private void OnListMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressPoint = e.GetPosition(ActionList);
        var src = e.OriginalSource as DependencyObject;
        _pressedInteractive = IsInteractive(src);
        _pressedRow = Ui.FindAncestor<ListBoxItem>(src)?.DataContext as ActionRowViewModel;
    }

    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressedRow is null || _pressedInteractive || _pressedRow.IsBar) return;
        var delta = e.GetPosition(ActionList) - _pressPoint;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var row = _pressedRow;
        _pressedRow = null;
        if (!row.IsSelected) { foreach (var r in _vm.Rows) r.IsSelected = false; row.IsSelected = true; }
        var uids = _vm.SelectedRows.Where(r => !r.IsBar).Select(r => r.Uid).ToList();
        if (uids.Count == 0) return;
        var data = new DataObject(RowsFormat, string.Join(",", uids));
        var lifted = LiftRows(_vm.SelectedRows.Where(r => !r.IsBar));
        try { DragDrop.DoDragDrop(ActionList, data, DragDropEffects.Move); }
        finally { HideDropLine(); SettleRows(lifted); }
    }

    private bool TryGetDropTarget(DragEventArgs e, out int index, out double y, out double x, out double width)
    {
        index = _vm.Doc.Actions.Count; y = 0; x = 0; width = 0;
        var pos = e.GetPosition(ActionList);
        ListBoxItem? item = null;
        var hit = VisualTreeHelper.HitTest(ActionList, pos);
        if (hit?.VisualHit is DependencyObject v) item = Ui.FindAncestor<ListBoxItem>(v);
        ActionRowViewModel? row = item?.DataContext as ActionRowViewModel;
        bool after = true;

        if (item is null || row is null)
        {
            // between or outside rows: find the nearest realised row
            ListBoxItem? best = null; var bestDist = double.MaxValue;
            for (var i = 0; i < ActionList.Items.Count; i++)
            {
                if (ActionList.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem c || !c.IsVisible) continue;
                var top = c.TranslatePoint(new Point(0, 0), ActionList).Y;
                var mid = top + c.ActualHeight / 2;
                var dist = Math.Abs(mid - pos.Y);
                if (dist < bestDist) { bestDist = dist; best = c; }
            }
            item = best;
            row = item?.DataContext as ActionRowViewModel;
            if (item is null || row is null) { index = 0; return _vm.Doc.Actions.Count == 0; }
        }
        var local = e.GetPosition(item);
        after = local.Y >= item.ActualHeight / 2;
        var rowIndex = _vm.Doc.IndexOf(row.Action);
        index = after ? rowIndex + 1 : rowIndex;
        index = FlowGroups.ClampInsertIndex(_vm.Doc.Actions, index);

        var rowView = Ui.Descendants<ActionRowView>(item).FirstOrDefault();
        var depth = row.Depth;
        if (after && (row.Role == FlowRole.Start)) depth = row.Depth + 1;
        else if (after && row.Role is FlowRole.Middle) depth = row.Depth + 1;
        else if (!after && row.Role is FlowRole.Middle or FlowRole.End) depth = row.Depth + 1;
        var origin = (rowView ?? (FrameworkElement)item).TranslatePoint(new Point(0, 0), DropOverlay);
        x = origin.X + depth * ActionRowView.GutterWidth;
        width = Math.Max(40, (rowView?.ActualWidth ?? item.ActualWidth) - depth * ActionRowView.GutterWidth);
        var itemTop = item.TranslatePoint(new Point(0, 0), DropOverlay).Y;
        y = after ? itemTop + item.ActualHeight - 8 : itemTop - 8;
        if (y < 0) y = 1;
        return true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var isPanel = e.Data.GetDataPresent(ActionPanelView.DragFormat);
        var isRows = e.Data.GetDataPresent(RowsFormat);
        if (!isPanel && !isRows) { e.Effects = DragDropEffects.None; HideDropLine(); return; }

        // auto-scroll near the edges
        var pos = e.GetPosition(ActionList);
        var sv = Ui.Descendants<ScrollViewer>(ActionList).FirstOrDefault();
        if (sv is not null)
        {
            if (pos.Y < 44) sv.ScrollToVerticalOffset(sv.VerticalOffset - 18);
            else if (pos.Y > ActionList.ActualHeight - 44) sv.ScrollToVerticalOffset(sv.VerticalOffset + 18);
        }

        if (!TryGetDropTarget(e, out var index, out var y, out var x, out var width)) { e.Effects = DragDropEffects.None; HideDropLine(); return; }
        if (isRows && !CanMoveHere(e, index)) { e.Effects = DragDropEffects.None; HideDropLine(); return; }
        e.Effects = isPanel ? DragDropEffects.Copy : DragDropEffects.Move;
        ShowDropLine(y, x, width);
    }

    private IReadOnlyList<ActionInstance> DraggedActions(DragEventArgs e)
    {
        if (e.Data.GetData(RowsFormat) is not string s) return Array.Empty<ActionInstance>();
        var uids = s.Split(',').ToHashSet();
        return _vm.Doc.Actions.Where(a => uids.Contains(a.Uid)).ToList();
    }

    private bool CanMoveHere(DragEventArgs e, int index)
    {
        var items = DraggedActions(e);
        if (items.Count == 0) return false;
        var ranges = FlowGroups.ExpandToBlocks(_vm.Doc.Actions, items.Select(_vm.Doc.IndexOf));
        foreach (var (s, en) in ranges) if (index > s && index <= en) return false;
        if (ranges.Count == 1 && (index == ranges[0].Start || index == ranges[0].End + 1)) return false;
        return true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        HideDropLine();
        if (!TryGetDropTarget(e, out var index, out _, out _, out _)) return;
        if (e.Data.GetData(ActionPanelView.DragFormat) is string id)
        {
            if (_vm.Context.Actions.Describe(id) is { } d) _vm.InsertAt(d, index);
            return;
        }
        var items = DraggedActions(e);
        if (items.Count == 0 || !CanMoveHere(e, index)) return;
        var blocks = _vm.Doc.BlocksOf(items);
        if (_vm.Doc.Move(items, index)) { _vm.SelectActions(blocks); }
    }

    private void ShowDropLine(double y, double x, double width)
    {
        DropLine.Visibility = Visibility.Visible;
        DropLine.Width = width;
        Canvas.SetLeft(DropLine, x);
        Canvas.SetTop(DropLine, y);
    }

    private void HideDropLine() => DropLine.Visibility = Visibility.Collapsed;

    // ---- closing -------------------------------------------------------------------------------

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        CommitName();
        if (!ResolveUnkeptDraft()) { e.Cancel = true; return; }
        _vm.Stop();
        if (!_vm.SaveNow())
        {
            var answer = EditorDialog.Ask(this, "Couldn't save this shortcut",
                (_vm.SaveError ?? Strings.Get("ShortcutEditorWindow_YourChangesCouldntBe")) + "\n\nClose anyway and lose your latest changes?",
                Strings.Get("ShortcutEditorWindow_CloseAndLoseThem"), Strings.Get("ShortcutEditorWindow_KeepEditing"), danger: true);
            if (answer != DialogChoice.Primary) { e.Cancel = true; return; }
        }
        _describeCts?.Cancel();
        _vm.Detach();
    }
}
