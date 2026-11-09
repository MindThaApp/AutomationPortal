using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AutomationPortal.Core;
using AutomationPortal.Editor.Logic;
using Wpf.Ui.Controls;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace AutomationPortal.Editor.UI;

/// <summary>The "If" conditions builder: match all / any of rows "left, operator, right (, right2)".</summary>
internal sealed class ConditionsEditor : StackPanel
{
    private readonly EditorArgs _a;
    private ConditionsValue _value;

    public ConditionsEditor(EditorArgs a)
    {
        _a = a;
        MinWidth = 440;
        _value = (a.Value ?? a.Param.Default) as ConditionsValue ?? new ConditionsValue(ConditionMatch.All, Array.Empty<ConditionRow>());
        if (_value.Items.Count == 0)
        {
            _value = ConditionModel.AddRow(_value, ConditionModel.NewRow());
            _a.Commit(_value);
        }
        Rebuild();
    }

    private void Commit(ConditionsValue v, bool rebuild)
    {
        _value = v;
        _a.Commit(v);
        if (rebuild) Rebuild();
    }

    private void Rebuild()
    {
        Children.Clear();
        if (_value.Items.Count > 1)
        {
            var match = new WpfComboBox { Width = 90, ItemsSource = new[] { "all", "any" }, SelectedIndex = _value.Match == ConditionMatch.All ? 0 : 1 };
            match.SelectionChanged += (_, _) => Commit(ConditionModel.WithMatch(_value, match.SelectedIndex == 0 ? ConditionMatch.All : ConditionMatch.Any), false);
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            head.Children.Add(Ui.Text("Match ", Ui.Primary, 14));
            head.Children.Add(match);
            head.Children.Add(Ui.Text(" of these conditions", Ui.Primary, 14));
            Children.Add(head);
        }
        for (var i = 0; i < _value.Items.Count; i++) Children.Add(RowView(i));
        var add = Ui.TextButton("Add condition", () =>
        {
            var prev = _a.Variables().FirstOrDefault(o => o.Group == VariableGroup.Actions);
            Commit(ConditionModel.AddRow(_value, ConditionModel.NewRow(prev)), true);
        }, SymbolRegular.Add16);
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 4, 0, 0);
        Children.Add(add);
    }

    private FrameworkElement RowView(int index)
    {
        var row = _value.Items[index];
        var card = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 8) }
            .Dyn(Border.BorderBrushProperty, Ui.CardStroke).Dyn(Border.BackgroundProperty, "ControlFillColorSecondaryBrush");
        var stack = new StackPanel();
        card.Child = stack;

        // left side
        var left = new TokenTextBox(_a.Env, _a.Variables, false, true) { Placeholder = "Choose a variable or type a value" };
        left.SetValue(row.Left);
        left.ValueEdited += v =>
        {
            var current = _value.Items[index];
            var kindBefore = LeftKind(current.Left);
            var updated = current with { Left = v };
            var changedKind = kindBefore != LeftKind(v);
            _value = ConditionModel.ReplaceRow(_value, index, updated);
            _a.Commit(_value);
            if (changedKind && v is VarValue) Rebuild();
        };
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(left);
        var remove = Ui.IconButton(SymbolRegular.Delete16, "Remove this condition", () => Commit(ConditionModel.RemoveRow(_value, index), true), 28, 14);
        remove.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(remove, 1);
        if (_value.Items.Count > 1) top.Children.Add(remove);
        stack.Children.Add(top);

        // operator
        var kind = LeftKind(row.Left);
        var ops = ConditionModel.OperatorsFor(kind);
        var view = new ListCollectionView(ops.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(OperatorInfo.Group)));
        var combo = new WpfComboBox { ItemsSource = view, DisplayMemberPath = nameof(OperatorInfo.Label), Margin = new Thickness(0, 8, 0, 0), MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Left };
        combo.GroupStyle.Add(new GroupStyle
        {
            HeaderTemplate = HeaderTemplate(),
        });
        combo.SelectedItem = ops.FirstOrDefault(o => o.Id == row.Op);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not OperatorInfo op || op.Id == _value.Items[index].Op) return;
            Commit(ConditionModel.ReplaceRow(_value, index, ConditionModel.WithOperator(_value.Items[index], op.Id)), true);
        };
        stack.Children.Add(combo);

        // right side
        var operands = ConditionModel.Operands(row.Op);
        var group = ConditionOperators.Find(row.Op)?.Group ?? "Text";
        if (operands >= 1)
        {
            stack.Children.Add(Operand(index, group, second: false, row.Right));
            if (operands >= 2)
            {
                var and = Ui.Text("and", Ui.Secondary, 13);
                and.Margin = new Thickness(2, 6, 0, 0);
                stack.Children.Add(and);
                stack.Children.Add(Operand(index, group, second: true, row.Right2));
            }
        }
        return card;
    }

    private ContentKind? LeftKind(ParamValue? left) => left is VarValue v ? _a.Env.Describe(v.Ref).Kind : null;

    private FrameworkElement Operand(int index, string group, bool second, ParamValue? value)
    {
        var type = group switch { "Date" => ParamType.Date, "Number" => ParamType.Number, _ => ParamType.Text };
        var descriptor = new ParameterDescriptor
        {
            Key = second ? "right2" : "right", Label = "Value", Type = type, Placeholder = group switch { "Number" => "Number", "Date" => "Date", _ => "Type a value" },
        };
        var view = new ParamEditorView(descriptor, value, _a.Env, _a.Variables, v =>
        {
            var current = _value.Items[index];
            var next = second ? current with { Right2 = v ?? ParamValue.Text() } : current with { Right = v ?? ParamValue.Text() };
            _value = ConditionModel.ReplaceRow(_value, index, next);
            _a.Commit(_value);
        }, compact: true);
        view.Margin = new Thickness(0, 6, 0, 0);
        return view;
    }

    private static DataTemplate HeaderTemplate()
    {
        var t = new DataTemplate();
        var f = new FrameworkElementFactory(typeof(TextBlock));
        f.SetBinding(TextBlock.TextProperty, new Binding("Name"));
        f.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        f.SetValue(TextBlock.FontSizeProperty, 12.0);
        f.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 4, 0, 2));
        f.SetResourceReference(TextBlock.ForegroundProperty, Ui.Secondary);
        t.VisualTree = f;
        return t;
    }
}
