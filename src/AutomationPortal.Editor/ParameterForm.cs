using System.Windows;
using System.Windows.Controls;
using Flowmate.Core;
using Flowmate.Editor.Logic;
using Flowmate.Editor.UI;

namespace Flowmate.Editor;

public enum ParameterFormMode
{
    /// <summary>Editor mode: every value can come from a variable or Ask Each Time.</summary>
    WithVariables,
    /// <summary>Literal values only (trigger and condition options).</summary>
    LiteralOnly,
}

public sealed class ParameterChangedEventArgs : EventArgs
{
    public ParameterChangedEventArgs(string key, ParamValue? value) { Key = key; Value = value; }
    public string Key { get; }
    /// <summary>The new value, or null when the parameter was cleared (back to its default).</summary>
    public ParamValue? Value { get; }
}

/// <summary>What the form needs to offer variables and dynamic choices. All optional.</summary>
public sealed class ParameterFormEnvironment
{
    public FlowmateServices? Services { get; init; }
    public ActionRegistry? Registry { get; init; }
    /// <summary>The shortcut the parameters belong to; needed for variables in <see cref="ParameterFormMode.WithVariables"/>.</summary>
    public Shortcut? Shortcut { get; init; }
    /// <summary>Position of the action in the shortcut: only actions above it offer their output as variables.</summary>
    public int ActionIndex { get; init; } = int.MaxValue;
}

/// <summary>
/// Renders a list of <see cref="ParameterDescriptor"/>s with an editor per parameter, honouring <c>VisibleWhen</c>.
/// Reusable for action parameters (<see cref="ParameterFormMode.WithVariables"/>) and for trigger / condition options (<see cref="ParameterFormMode.LiteralOnly"/>).
/// </summary>
public sealed class ParameterForm : UserControl
{
    private IReadOnlyList<ParameterDescriptor> _parameters = Array.Empty<ParameterDescriptor>();
    private IDictionary<string, ParamValue> _values = new Dictionary<string, ParamValue>();
    private EditorEnv? _env;
    private Func<IReadOnlyList<VariableOption>> _vars = () => Array.Empty<VariableOption>();
    private readonly StackPanel _stack = new();
    private HashSet<string> _controllers = new();
    private bool _built;

    public ParameterForm()
    {
        EditorResources.Ensure();
        Content = _stack;
        Mode = ParameterFormMode.WithVariables;
    }

    public ParameterFormMode Mode { get; set; }

    /// <summary>When true (default) the form writes changes into the dictionary passed to <see cref="Load"/>. Turn off to apply them yourself in <see cref="ParameterChanged"/>.</summary>
    public bool ApplyChangesToValues { get; set; } = true;

    public IDictionary<string, ParamValue> Values => _values;

    public event EventHandler<ParameterChangedEventArgs>? ParameterChanged;

    public void Load(IEnumerable<ParameterDescriptor> parameters, IDictionary<string, ParamValue> values, ParameterFormEnvironment? environment = null)
    {
        var shortcut = environment?.Shortcut;
        var env = new EditorEnv(environment?.Services, environment?.Registry ?? new ActionRegistry(), shortcut ?? new Shortcut(),
            withVariables: Mode == ParameterFormMode.WithVariables && shortcut is not null);
        var index = environment?.ActionIndex ?? int.MaxValue;
        LoadInternal(parameters.ToList(), values, env, () => env.VariablesAt(index));
    }

    internal void LoadInternal(IReadOnlyList<ParameterDescriptor> parameters, IDictionary<string, ParamValue> values, EditorEnv env, Func<IReadOnlyList<VariableOption>> variables)
    {
        _parameters = parameters;
        _values = values;
        _env = env;
        _vars = variables;
        _controllers = parameters.Where(p => p.VisibleWhen is not null).Select(p => p.VisibleWhen!.Key).ToHashSet();
        Rebuild();
    }

    private string? CurrentText(string key)
    {
        var v = _values.TryGetValue(key, out var x) ? x : _parameters.FirstOrDefault(p => p.Key == key)?.Default;
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

    private void Rebuild()
    {
        if (_env is null) return;
        _stack.Children.Clear();
        _built = true;
        foreach (var p in _parameters)
        {
            if (!p.IsVisible(CurrentText)) continue;
            _stack.Children.Add(Row(p));
        }
    }

    private FrameworkElement Row(ParameterDescriptor p)
    {
        var current = _values.TryGetValue(p.Key, out var v) ? v : null;
        var view = new ParamEditorView(p, current, _env!, _vars, value => OnCommit(p, value), compact: true);
        var label = Ui.Text(p.Label + (p.Required ? " *" : ""), Ui.Secondary, 12, FontWeights.SemiBold);
        if (p.Type == ParamType.Boolean)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            label.FontSize = 14;
            label.FontWeight = FontWeights.Normal;
            label.SetResourceReference(TextBlock.ForegroundProperty, Ui.Primary);
            row.Children.Add(label);
            Grid.SetColumn(view, 1);
            row.Children.Add(view);
            return row;
        }
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        label.Margin = new Thickness(0, 0, 0, 4);
        stack.Children.Add(label);
        stack.Children.Add(view);
        LeftAlignFixedWidth(view);
        return stack;
    }

    /// <summary>A control with its own Width inside a stretching panel is centred by WPF; in a form it should line up with the label.</summary>
    private static void LeftAlignFixedWidth(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is not DependencyObject d) continue;
            if (d is FrameworkElement { Width: > 0 and < double.PositiveInfinity, HorizontalAlignment: HorizontalAlignment.Stretch } fe) fe.HorizontalAlignment = HorizontalAlignment.Left;
            LeftAlignFixedWidth(d);
        }
    }

    private void OnCommit(ParameterDescriptor p, ParamValue? value)
    {
        if (ApplyChangesToValues)
        {
            if (value is null) _values.Remove(p.Key); else _values[p.Key] = value;
        }
        ParameterChanged?.Invoke(this, new ParameterChangedEventArgs(p.Key, value));
        if (_controllers.Contains(p.Key)) Dispatcher.BeginInvoke(new Action(() => { if (_built) Rebuild(); }), System.Windows.Threading.DispatcherPriority.Background);
    }
}
