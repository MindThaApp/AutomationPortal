using System.Windows;
using System.Windows.Controls;
using Flowmate.Core;
using Flowmate.Editor.Logic;
using Wpf.Ui.Controls;

namespace Flowmate.Editor.UI;

/// <summary>Edits a List parameter: one line per item, each can hold variables.</summary>
internal sealed class ListEditor : StackPanel
{
    private readonly EditorArgs _a;
    private readonly List<TokenTextBox> _boxes = new();
    private readonly StackPanel _rows = new();

    public ListEditor(EditorArgs a)
    {
        _a = a;
        MinWidth = 340;
        Children.Add(_rows);
        var items = ((a.Value ?? a.Param.Default) as ListValue)?.Items ?? Array.Empty<ParamValue>();
        foreach (var i in items) AddRow(i);
        if (_boxes.Count == 0) AddRow(null);
        var add = Ui.TextButton("Add item", () => { AddRow(null); _boxes[^1].Focus(); }, SymbolRegular.Add16);
        add.Margin = new Thickness(0, 6, 0, 0);
        add.HorizontalAlignment = HorizontalAlignment.Left;
        Children.Add(add);
    }

    private void AddRow(ParamValue? value)
    {
        var box = new TokenTextBox(_a.Env, _a.Variables, multiline: false, _a.Param.AllowVariables) { Placeholder = "Item" };
        box.SetValue(value);
        box.ValueEdited += _ => Push();
        _boxes.Add(box);
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(box);
        var remove = Ui.IconButton(SymbolRegular.Dismiss16, "Remove item", () =>
        {
            _boxes.Remove(box);
            _rows.Children.Remove(row);
            Push();
        }, 28, 12);
        remove.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetColumn(remove, 1);
        row.Children.Add(remove);
        _rows.Children.Add(row);
    }

    private void Push() => _a.Commit(new ListValue(_boxes.Select(b => b.Value).Where(v => !ParamValueEx.IsBlank(v) || _boxes.Count == 1).ToList()));
}

/// <summary>Edits a Dictionary parameter: key and value per row.</summary>
internal sealed class DictEditor : StackPanel
{
    private readonly EditorArgs _a;
    private readonly List<(TokenTextBox Key, TokenTextBox Value)> _boxes = new();
    private readonly StackPanel _rows = new();

    public DictEditor(EditorArgs a)
    {
        _a = a;
        MinWidth = 400;
        Children.Add(_rows);
        var items = ((a.Value ?? a.Param.Default) as DictValue)?.Items ?? Array.Empty<DictEntry>();
        foreach (var i in items) AddRow(i.Key, i.Value);
        if (_boxes.Count == 0) AddRow(null, null);
        var add = Ui.TextButton("Add item", () => { AddRow(null, null); _boxes[^1].Key.Focus(); }, SymbolRegular.Add16);
        add.Margin = new Thickness(0, 6, 0, 0);
        add.HorizontalAlignment = HorizontalAlignment.Left;
        Children.Add(add);
    }

    private void AddRow(ParamValue? key, ParamValue? value)
    {
        var k = new TokenTextBox(_a.Env, _a.Variables, false, _a.Param.AllowVariables) { Placeholder = "Name" };
        var v = new TokenTextBox(_a.Env, _a.Variables, false, _a.Param.AllowVariables) { Placeholder = "Value" };
        k.SetValue(key);
        v.SetValue(value);
        k.ValueEdited += _ => Push();
        v.ValueEdited += _ => Push();
        _boxes.Add((k, v));
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(k);
        Grid.SetColumn(v, 1);
        v.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(v);
        var remove = Ui.IconButton(SymbolRegular.Dismiss16, "Remove item", () =>
        {
            _boxes.RemoveAll(b => b.Key == k);
            _rows.Children.Remove(row);
            Push();
        }, 28, 12);
        remove.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetColumn(remove, 2);
        row.Children.Add(remove);
        _rows.Children.Add(row);
    }

    private void Push() => _a.Commit(new DictValue(_boxes.Select(b => new DictEntry(b.Key.Value, b.Value.Value))
        .Where(e => !ParamValueEx.IsBlank(e.Key) || !ParamValueEx.IsBlank(e.Value)).ToList()));
}

/// <summary>Edits a List parameter that has fixed choices ("On these days"): tick any number of them.</summary>
internal sealed class MultiChoiceEditor : WrapPanel
{
    public MultiChoiceEditor(EditorArgs a)
    {
        MaxWidth = 420;
        var selected = ((a.Value ?? a.Param.Default) as ListValue)?.Items.Select(i => i.ToLiteralText()).ToHashSet(StringComparer.OrdinalIgnoreCase)
                       ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var boxes = new List<(Choice Choice, System.Windows.Controls.CheckBox Box)>();
        foreach (var c in a.Param.Choices!)
        {
            var box = new System.Windows.Controls.CheckBox { Content = c.Label, IsChecked = selected.Contains(c.Id), Margin = new Thickness(0, 0, 14, 4), MinWidth = 0 };
            void Push() => a.Commit(new ListValue(boxes.Where(b => b.Box.IsChecked == true).Select(b => (ParamValue)ParamValue.Text(b.Choice.Id)).ToList()));
            box.Checked += (_, _) => Push();
            box.Unchecked += (_, _) => Push();
            boxes.Add((c, box));
            Children.Add(box);
        }
    }
}