using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using AutomationPortal.App.Logic;
using AutomationPortal.Editor;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Dialogs;

/// <summary>
/// Ctrl+K: one box that finds a shortcut (Enter runs it, Ctrl+Enter edits it), a Gallery item, a page or a command.
/// Closes when it loses focus or on Esc. Never changes anything by itself; the owner decides what a choice does.
/// </summary>
public sealed class CommandPaletteWindow : FluentWindow
{
    private readonly IReadOnlyList<PaletteEntry> _all;
    private readonly Action<PaletteEntry, bool> _choose;
    private readonly Action<string>? _create;
    private readonly Wpf.Ui.Controls.TextBox _box;
    private readonly ListBox _list;
    private readonly TextBlock _empty;
    private bool _done;

    private CommandPaletteWindow(Window? owner, IReadOnlyList<PaletteEntry> all, Action<PaletteEntry, bool> choose, Action<string>? create)
    {
        _all = all;
        _choose = choose;
        _create = create;
        Title = Strings.Get("CommandPaletteWindow_CommandPalette");
        Width = 620;
        Height = 480;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        // Acrylic like Quick Run and Quick Panel: this surface pops up and goes, so it lets the backdrop show through.
        WindowBackdropType = WindowBackdropType.Acrylic;
        ExtendsContentIntoTitleBar = true;
        UseLayoutRounding = true;
        SetResourceReference(ForegroundProperty, "DsTextPrimaryBrush");
        FontFamily = Application.Current?.TryFindResource("FlowFont") as System.Windows.Media.FontFamily ?? new System.Windows.Media.FontFamily(Strings.Get("CommandPaletteWindow_SegoeUIVariable"));
        if (owner is { IsVisible: true }) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TitleBar { Title = Strings.Get("CommandPaletteWindow_CommandPalette"), ShowMinimize = false, ShowMaximize = false, CanMaximize = false });

        _box = new Wpf.Ui.Controls.TextBox { PlaceholderText = Strings.Get("CommandPaletteWindow_TypeAShortcutA"), Margin = new Thickness(16, 4, 16, 8), FontSize = 16 };
        AutomationProperties.SetName(_box, "Command palette search");
        _box.TextChanged += (_, _) => Refresh();
        _box.PreviewKeyDown += OnKeyDown;
        Grid.SetRow(_box, 1);
        root.Children.Add(_box);

        _list = new ListBox { BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, Margin = new Thickness(8, 0, 8, 0), Focusable = false };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(_list, d) is ListBoxItem item) Choose(item.DataContext as PaletteEntry, false);
        };
        Grid.SetRow(_list, 2);
        root.Children.Add(_list);

        _empty = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24, 12, 24, 0), Visibility = Visibility.Collapsed };
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "DsTextSecondaryBrush");
        Grid.SetRow(_empty, 2);
        root.Children.Add(_empty);

        var hint = new TextBlock { Text = Strings.Get("CommandPaletteWindow_EnterRunOrOpen"), FontSize = 13, Margin = new Thickness(18, 8, 18, 12) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "DsTextTertiaryBrush");
        Grid.SetRow(hint, 3);
        root.Children.Add(hint);
        var slide = new System.Windows.Media.TranslateTransform();
        root.RenderTransform = slide;
        Content = root;

        _list.ItemTemplate = BuildTemplate();
        Loaded += (_, _) =>
        {
            _box.Focus(); Refresh();
            // Slides in like Quick Run; closing is always instant.
            Motion.Animate(slide, System.Windows.Media.TranslateTransform.YProperty, -10, 0, MotionRole.Enter);
            Motion.Animate(this, OpacityProperty, 0, 1, MotionRole.Enter);
        };
        Deactivated += (_, _) => Finish();
    }

    public static void Open(Window? owner, IReadOnlyList<PaletteEntry> all, Action<PaletteEntry, bool> choose, System.Diagnostics.Stopwatch? watch = null, Action<string>? create = null)
    {
        var window = new CommandPaletteWindow(owner, all, choose, create);
        if (watch is not null)
            window.ContentRendered += (_, _) => AutomationPortal.App.Infrastructure.SpeedBudget.Log("palette ready", watch, AutomationPortal.App.Infrastructure.SpeedBudget.PaletteReadyMs);
        window.Show();
    }

    private static DataTemplate BuildTemplate()
    {
        // Built in code like the rest of this window: a title, with a plain-words kind and detail under it.
        var template = new DataTemplate(typeof(PaletteEntry));
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 6, 10, 6));
        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(PaletteEntry.Title)));
        title.SetValue(TextBlock.FontSizeProperty, 14.0);
        title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        var detail = new FrameworkElementFactory(typeof(TextBlock));
        detail.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(PaletteEntry.Detail)));
        detail.SetValue(TextBlock.FontSizeProperty, 12.0);
        detail.SetResourceReference(TextBlock.ForegroundProperty, "DsTextSecondaryBrush");
        detail.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        panel.AppendChild(title);
        panel.AppendChild(detail);
        template.VisualTree = panel;
        return template;
    }

    private void Refresh()
    {
        _list.ItemsSource = PaletteSearch.Rank(_all, _box.Text).ToList();
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        var typed = _box.Text.Trim();
        _empty.Text = _create is null ? Strings.Format("CommandPaletteWindow_NothingCalled", typed) : Strings.Format("CommandPaletteWindow_CreateOnEnter", typed);
        _empty.Visibility = typed.Length > 0 && _list.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: e.Handled = true; Finish(); break;
            case Key.Down: Move(1); e.Handled = true; break;
            case Key.Up: Move(-1); e.Handled = true; break;
            case Key.Enter:
                e.Handled = true;
                var enter = PaletteEnter.Decide(_list.SelectedItem as PaletteEntry, _box.Text);
                if (enter.NewShortcutName is { } name && _create is not null) { Finish(); _create(name); }
                else Choose(enter.Entry, Keyboard.Modifiers == ModifierKeys.Control);
                break;
        }
    }

    private void Move(int delta)
    {
        if (_list.Items.Count == 0) return;
        var i = (_list.SelectedIndex + delta + _list.Items.Count) % _list.Items.Count;
        _list.SelectedIndex = i;
        _list.ScrollIntoView(_list.SelectedItem);
    }

    private void Choose(PaletteEntry? entry, bool secondary)
    {
        if (entry is null) return;
        Finish();
        _choose(entry, secondary);
    }

    private void Finish()
    {
        if (_done) return;
        _done = true;
        Close();
    }
}
