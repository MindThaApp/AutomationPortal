using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutomationPortal.Core;
using AutomationPortal.Editor.Logic;
using AutomationPortal.Editor.ViewModels;

namespace AutomationPortal.Editor.UI;

/// <summary>The small "add an action here" popup behind the hover "+" between cards.</summary>
internal static class QuickInsert
{
    public static void Show(FrameworkElement anchor, EditorViewModel vm, int insertIndex)
    {
        FloatingPanel? panel = null;
        var root = new StackPanel { Width = 340 };
        root.Children.Add(Ui.Text("Add an action here", Ui.Primary, 15, FontWeights.SemiBold));
        ((FrameworkElement)root.Children[0]).Margin = new Thickness(0, 0, 0, 8);
        var search = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Search actions", Margin = new Thickness(0, 0, 0, 8) };
        root.Children.Add(search);
        var list = new ListBox { MaxHeight = 330, Style = Application.Current?.TryFindResource("FmPlainListStyle") as Style };
        root.Children.Add(list);

        void Pick(ActionDescriptor d)
        {
            panel?.Close();
            vm.InsertAt(d, insertIndex);
        }

        void Fill()
        {
            list.Items.Clear();
            var q = search.Text.Trim();
            var advanced = vm.Prefs.ShowAdvanced;
            IReadOnlyList<ActionDescriptor> items = q.Length == 0
                ? ActionSearch.Suggest(vm.Context.Actions, vm.OutputKindAbove(insertIndex), vm.Prefs.Recents, vm.Prefs.Favourites.ToHashSet(), advanced, 8)
                : ActionSearch.Search(vm.Context.Actions, q, advanced, vm.Prefs.Favourites.ToHashSet(), vm.Prefs.Recents).Take(30).ToList();
            if (q.Length == 0 && items.Count > 0)
            {
                list.Items.Add(new ListBoxItem { Content = Ui.Text("Suggestions", Ui.Secondary, 12, FontWeights.SemiBold), IsHitTestVisible = false, Focusable = false, Margin = new Thickness(6, 2, 0, 2) });
            }
            if (q.Length > 0 && items.Count == 0)
                list.Items.Add(new ListBoxItem { Content = Ui.Text("No actions match.", Ui.Secondary, 13), IsHitTestVisible = false, Focusable = false, Margin = new Thickness(6, 2, 0, 2) });
            foreach (var d in items)
            {
                var grid = new Grid { Margin = new Thickness(6, 4, 8, 4) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var chip = Ui.IconChip(d.Icon, d.Color, 26, 15, 6);
                chip.Margin = new Thickness(0, 0, 10, 0);
                grid.Children.Add(chip);
                var title = Ui.Text(d.Title, Ui.Primary, 14);
                Grid.SetColumn(title, 1);
                grid.Children.Add(title);
                var cat = Ui.Text(d.Category, Ui.Tertiary, 12);
                Grid.SetColumn(cat, 2);
                grid.Children.Add(cat);
                var item = new ListBoxItem { Content = grid, Tag = d };
                System.Windows.Automation.AutomationProperties.SetName(item, d.Title + ", " + d.Category);
                item.MouseLeftButtonUp += (_, _) => Pick(d);
                list.Items.Add(item);
            }
            foreach (var i in list.Items.OfType<ListBoxItem>()) if (i.Tag is ActionDescriptor) { list.SelectedItem = i; break; }
        }

        void Move(int delta)
        {
            var selectable = list.Items.OfType<ListBoxItem>().Where(i => i.Tag is ActionDescriptor).ToList();
            if (selectable.Count == 0) return;
            var idx = list.SelectedItem is ListBoxItem cur ? selectable.IndexOf(cur) : -1;
            idx = Math.Clamp(idx + delta, 0, selectable.Count - 1);
            list.SelectedItem = selectable[idx];
            list.ScrollIntoView(selectable[idx]);
        }

        search.TextChanged += (_, _) => Fill();
        search.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Down: Move(+1); e.Handled = true; break;
                case Key.Up: Move(-1); e.Handled = true; break;
                case Key.Enter:
                    if (list.SelectedItem is ListBoxItem { Tag: ActionDescriptor d }) Pick(d);
                    e.Handled = true;
                    break;
            }
        };
        Fill();
        panel = FloatingPanel.Open(anchor, root, 340);
    }
}
