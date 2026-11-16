using System.Windows;
using System.Windows.Controls;
using AutomationPortal.Core;
using AutomationPortal.Editor.Logic;
using Wpf.Ui.Controls;

namespace AutomationPortal.Editor.UI;

/// <summary>
/// The full editor for one parameter: the literal editor for its type, or a pill when the value comes from a variable /
/// Ask each time, plus "Variable" and "Ask each time" buttons. <c>compact</c> puts a small variable button beside the editor instead.
/// </summary>
internal sealed class ParamEditorView : StackPanel
{
    private readonly ParameterDescriptor _p;
    private readonly EditorEnv _env;
    private readonly Func<IReadOnlyList<VariableOption>> _vars;
    private readonly Action<ParamValue?> _commit;
    private readonly bool _compact;
    private ParamValue? _value;
    private TokenTextBox? _token;

    public ParamEditorView(ParameterDescriptor p, ParamValue? value, EditorEnv env, Func<IReadOnlyList<VariableOption>> variables,
        Action<ParamValue?> commit, bool compact = false)
    {
        _p = p; _env = env; _vars = variables; _commit = commit; _compact = compact; _value = value;
        Rebuild();
    }

    public ParamValue? Value => _value;

    private bool CanUseVariables => _p.AllowVariables && _env.WithVariables;
    private bool CanAsk => _p.AllowAskEachTime && _env.WithVariables && !_compact;

    private void SetValue(ParamValue? v, bool rebuild)
    {
        _value = v;
        _commit(v);
        if (rebuild) Rebuild();
    }

    private void Rebuild()
    {
        Children.Clear();
        _token = null;
        var body = BuildBody();
        if (_compact && CanUseVariables && _value is not AskValue && !(_value is VarValue && !ValueEditors.IsTokenCapable(_p.Type)))
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(body);
            var vb = Ui.IconButton(SymbolRegular.Braces24, "Use a variable", PickVariable, 30, 15);
            vb.Margin = new Thickness(4, 0, 0, 0);
            Grid.SetColumn(vb, 1);
            row.Children.Add(vb);
            Children.Add(row);
        }
        else Children.Add(body);

        if (!string.IsNullOrEmpty(_p.Hint) && !_compact)
        {
            var hint = Ui.Text(_p.Hint!, Ui.Secondary, 12, wrap: true);
            hint.Margin = new Thickness(0, 6, 0, 0);
            Children.Add(hint);
        }
        var showFooter = !_compact && (CanUseVariables || CanAsk) && _value is not AskValue
            && !(_value is VarValue && !ValueEditors.IsTokenCapable(_p.Type));
        if (showFooter)
        {
            var footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            if (CanUseVariables)
            {
                var b = Ui.TextButton("Variable", PickVariable, SymbolRegular.Braces24);
                b.ToolTip = "Use the result of an earlier action, the clipboard and more";
                footer.Children.Add(b);
            }
            if (CanAsk)
            {
                var ask = Ui.TextButton("Ask each time", () => SetValue(ParamValue.Ask(), true), SymbolRegular.QuestionCircle24);
                ask.Margin = new Thickness(8, 0, 0, 0);
                ask.ToolTip = "Be asked for this value every time the shortcut runs";
                footer.Children.Add(ask);
            }
            if (footer.Children.Count > 0) Children.Add(footer);
        }
    }

    private FrameworkElement BuildBody()
    {
        if (_value is AskValue)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Pills.Ask(13));
            var t = Ui.Text("You'll be asked each time it runs.", Ui.Secondary, 13);
            t.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(t);
            var back = Ui.TextButton("Type a value instead", () => SetValue(null, true));
            back.Margin = new Thickness(10, 0, 0, 0);
            var panel = new StackPanel();
            panel.Children.Add(row);
            back.HorizontalAlignment = HorizontalAlignment.Left;
            back.Margin = new Thickness(0, 10, 0, 0);
            panel.Children.Add(back);
            return panel;
        }
        if (_value is VarValue vv && !ValueEditors.IsTokenCapable(_p.Type))
        {
            var panel = new StackPanel();
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var pill = Pills.Create(_env.Describe(vv.Ref), 13);
            pill.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                PillOptions.Show(pill, _env, ((VarValue)_value!).Ref, _vars, updated =>
                {
                    _value = new VarValue(updated);
                    _commit(_value);
                    Pills.Update(pill, _env.Describe(updated), 13);
                }, () => SetValue(null, true));
            };
            row.Children.Add(pill);
            var t = Ui.Text("is used for this value.", Ui.Secondary, 13);
            t.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(t);
            panel.Children.Add(row);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            buttons.Children.Add(Ui.TextButton("Choose another", PickVariable));
            var back = Ui.TextButton("Type a value instead", () => SetValue(null, true));
            back.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(back);
            panel.Children.Add(buttons);
            return panel;
        }

        var literal = _value is VarValue or AskValue ? null : _value;
        var element = ValueEditors.Build(new EditorArgs(_p, literal ?? (ValueEditors.IsTokenCapable(_p.Type) ? _value : null), _env, _vars, v =>
        {
            _value = v;
            _commit(v);
        }), out _token);
        return element;
    }

    private void PickVariable()
    {
        var anchor = (FrameworkElement?)_token ?? this;
        VariablePicker.Show(anchor, _vars(), allowAsk: _compact && _p.AllowAskEachTime && _env.WithVariables, result =>
        {
            if (result is { Ask: true }) { SetValue(ParamValue.Ask(), true); return; }
            if (result?.Option is not { } option) return;
            if (_token is not null) { _token.InsertVariable(option.Ref); return; }
            SetValue(new VarValue(option.Ref), true);
        });
    }
}
