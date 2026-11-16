using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using AutomationPortal.App.Logic;
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
    private readonly Wpf.Ui.Controls.TextBox _box;
    private readonly ListBox _list;
    private bool _done;

    private CommandPaletteWindow(Window? owner, IReadOnlyList<PaletteEntry> all, Action<PaletteEntry, bool> choose)
    {
        _all = all;
        _choose = choose;
        Title = Strings.Get("CommandPaletteWindow_CommandPalette");
        Width = 620;
        Height = 480;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowBackdropType = WindowBackdropType.Mica;
        ExtendsContentIntoTitleBar = true;
        UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
        SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
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

        var hint = new TextBlock { Text = Strings.Get("CommandPaletteWindow_EnterRunOrOpen"), FontSize = 13, Margin = new Thickness(18, 8, 18, 12) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        Grid.SetRow(hint, 3);
        root.Children.Add(hint);
        Content = root;

        _list.ItemTemplate = BuildTemplate();
        Loaded += (_, _) => { _box.Focus(); Refresh(); };
        Deactivated += (_, _) => Finish();
    }

    public static void Open(Window? owner, IReadOnlyList<PaletteEntry> all, Action<PaletteEntry, bool> choose) =>
        new CommandPaletteWindow(owner, all, choose).Show();

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
        detail.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
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
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: e.Handled = true; Finish(); break;
            case Key.Down: Move(1); e.Handled = true; break;
            case Key.Up: Move(-1); e.Handled = true; break;
            case Key.Enter: e.Handled = true; Choose(_list.SelectedItem as PaletteEntry, Keyboard.Modifiers == ModifierKeys.Control); break;
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
