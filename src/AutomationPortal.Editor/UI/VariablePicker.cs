using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Flowmate.Core;
using Flowmate.Editor.Logic;
using Wpf.Ui.Controls;

namespace Flowmate.Editor.UI;

/// <summary>The result of the picker: a variable, or Ask Each Time.</summary>
internal sealed record PickResult(VariableOption? Option, bool Ask);

/// <summary>"Choose a variable" list: outputs of earlier actions, special variables, named variables.</summary>
internal static class VariablePicker
{
    /// <summary>
    /// Shows the picker anchored to <paramref name="anchor"/> (inside the surrounding floating panel when there is one).
    /// <paramref name="onDone"/> gets null when dismissed without a choice.
    /// </summary>
    public static void Show(FrameworkElement anchor, IReadOnlyList<VariableOption> options, bool allowAsk, Action<PickResult?> onDone)
    {
        var finished = false;
        void Finish(PickResult? r) { if (finished) return; finished = true; onDone(r); }
        var host = FloatingPanel.Containing(anchor);
        FloatingPanel? own = null;
        var view = Build(options, allowAsk, r =>
        {
            Finish(r);
            if (host is not null) host.Back(); else own?.Close();
        }, host is not null ? () => host.Back() : null);
        if (host is not null) host.Navigate(view, () => Finish(null));
        else
        {
            own = FloatingPanel.Open(anchor, view, 320);
            own.Closed += () => Finish(null);
        }
    }

    private static FrameworkElement Build(IReadOnlyList<VariableOption> options, bool allowAsk, Action<PickResult> pick, Action? back)
    {
        var root = new StackPanel { Width = 320 };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        if (back is not null)
        {
            var b = Ui.IconButton(SymbolRegular.ArrowLeft24, "Back", back, 28, 14);
            DockPanel.SetDock(b, Dock.Left);
            b.Margin = new Thickness(0, 0, 6, 0);
            header.Children.Add(b);
        }
        header.Children.Add(Ui.Text("Choose a variable", Ui.Primary, 15, FontWeights.SemiBold));
        root.Children.Add(header);

        var search = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Search variables", Margin = new Thickness(0, 0, 0, 8), ClearButtonEnabled = false };
        root.Children.Add(search);

        var list = new ListBox { MaxHeight = 320, Style = Application.Current?.TryFindResource("FmPlainListStyle") as Style };
        list.SetValue(VirtualizingPanel.IsVirtualizingProperty, false);
        root.Children.Add(list);

        var empty = Ui.Text("Nothing matches.", Ui.Secondary, 13);
        empty.Margin = new Thickness(6, 4, 0, 0);
        empty.Visibility = Visibility.Collapsed;
        root.Children.Add(empty);

        void Fill()
        {
            list.Items.Clear();
            var q = search.Text.Trim();
            bool Match(string s) => q.Length == 0 || s.Contains(q, StringComparison.CurrentCultureIgnoreCase);

            if (allowAsk && Match("Ask Each Time")) list.Items.Add(Row(null, true, pick));
            Section(list, "From earlier actions", options.Where(o => o.Group == VariableGroup.Actions && (Match(o.Name) || Match(o.Detail ?? ""))), pick);
            Section(list, "Special", options.Where(o => o.Group == VariableGroup.Special && Match(o.Name)), pick);
            Section(list, "Your variables", options.Where(o => o.Group == VariableGroup.Named && Match(o.Name)), pick);
            empty.Visibility = list.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SelectFirst(list);
        }

        search.TextChanged += (_, _) => Fill();
        search.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Down: Move(list, +1); e.Handled = true; break;
                case Key.Up: Move(list, -1); e.Handled = true; break;
                case Key.Enter:
                    if (list.SelectedItem is ListBoxItem { Tag: PickResult r }) pick(r);
                    e.Handled = true;
                    break;
            }
        };
        Fill();
        root.Loaded += (_, _) => search.Focus();
        return root;
    }

    private static void Section(ListBox list, string title, IEnumerable<VariableOption> items, Action<PickResult> pick)
    {
        var any = false;
        foreach (var o in items)
        {
            if (!any)
            {
                any = true;
                var h = new ListBoxItem
                {
                    Content = Ui.Text(title, Ui.Secondary, 12, FontWeights.SemiBold), IsHitTestVisible = false, Focusable = false,
                    Margin = new Thickness(6, 8, 0, 2),
                };
                System.Windows.Automation.AutomationProperties.SetName(h, title);
                list.Items.Add(h);
            }
            list.Items.Add(Row(o, false, pick));
        }
    }

    private static ListBoxItem Row(VariableOption? option, bool ask, Action<PickResult> pick)
    {
        var grid = new Grid { Margin = new Thickness(6, 4, 8, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var chip = Ui.IconChip(ask ? "QuestionCircle24" : option!.Icon, ask ? "indigo" : option!.ColorKey, 24, 14, 6);
        chip.Margin = new Thickness(0, 0, 10, 0);
        grid.Children.Add(chip);
        var name = Ui.Text(ask ? "Ask Each Time" : option!.Name, Ui.Primary, 14);
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        var detail = ask ? "Prompt when it runs" : option!.Detail;
        if (!string.IsNullOrEmpty(detail))
        {
            var d = Ui.Text(detail, Ui.Tertiary, 12);
            d.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(d, 2);
            grid.Children.Add(d);
        }
        var result = new PickResult(option, ask);
        var item = new ListBoxItem { Content = grid, Tag = result };
        System.Windows.Automation.AutomationProperties.SetName(item, ask ? "Ask Each Time" : option!.Name + (string.IsNullOrEmpty(option.Detail) ? "" : ", " + option.Detail));
        item.MouseLeftButtonUp += (_, e) => { e.Handled = true; pick(result); };
        return item;
    }

    private static void SelectFirst(ListBox list)
    {
        foreach (var i in list.Items) if (i is ListBoxItem { Tag: PickResult } li) { list.SelectedItem = li; return; }
        list.SelectedItem = null;
    }

    private static void Move(ListBox list, int delta)
    {
        var selectable = list.Items.OfType<ListBoxItem>().Where(i => i.Tag is PickResult).ToList();
        if (selectable.Count == 0) return;
        var idx = list.SelectedItem is ListBoxItem cur ? selectable.IndexOf(cur) : -1;
        idx = Math.Clamp(idx + delta, 0, selectable.Count - 1);
        list.SelectedItem = selectable[idx];
        list.ScrollIntoView(selectable[idx]);
    }
}
