using System.Windows;
using AutomationPortal.App.Integration;
using AutomationPortal.App.Logic;
using AutomationPortal.Core;
using AutomationPortal.Editor;

namespace AutomationPortal.App.Services;

/// <summary>Options form for triggers and conditions, built from the editor's <see cref="ParameterForm"/> (literal values only).</summary>
public sealed class EditorParameterFormFactory : IParameterFormFactory
{
    public IParameterForm Create(IReadOnlyList<ParameterDescriptor> parameters, Dictionary<string, ParamValue> values, AutomationPortalServices services) =>
        new EditorParameterFormAdapter(parameters, values, services);
}

internal sealed class EditorParameterFormAdapter : IParameterForm
{
    private readonly IReadOnlyList<ParameterDescriptor> _parameters;
    private readonly Dictionary<string, ParamValue> _values;
    private readonly ParameterForm _form;

    public EditorParameterFormAdapter(IReadOnlyList<ParameterDescriptor> parameters, Dictionary<string, ParamValue> values, AutomationPortalServices services)
    {
        _parameters = parameters;
        _values = values;
        _form = new ParameterForm { Mode = ParameterFormMode.LiteralOnly };
        _form.Load(parameters, values, new ParameterFormEnvironment { Services = services });
        _form.ParameterChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public FrameworkElement Element => _form;
    public event EventHandler? Changed;
    public IReadOnlyList<string> MissingRequired() => LiteralFormatter.MissingRequired(_parameters, _values);
}
