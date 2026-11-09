namespace AutomationPortal.Core;

/// <summary>Fluent builder for shortcuts (tests, the gallery). Control-flow groups get matching GroupIds and roles.</summary>
public sealed class ShortcutBuilder
{
    public ShortcutBuilder(string name, string icon = "Flash24", string color = "blue")
    {
        Shortcut = new Shortcut { Name = name, Icon = icon, Color = color };
    }

    public Shortcut Shortcut { get; }
    public Shortcut Build() => Shortcut;

    /// <summary>Adds an action and returns its uid (use <c>VarRef.Output(uid)</c> later).</summary>
    public string Add(string actionId, params (string Key, ParamValue Value)[] parameters) =>
        Add(ActionInstance.Create(actionId, parameters));

    public string Add(ActionInstance a) { Shortcut.Actions.Add(a); return a.Uid; }

    public string Comment(string text) => Add("flow.comment", ("text", ParamValue.Text(text)));

    public string If(ConditionsValue conditions, Action<ShortcutBuilder> then, Action<ShortcutBuilder>? otherwise = null)
    {
        var g = Guid.NewGuid().ToString("N")[..8];
        var start = Add(ActionInstance.Create(FlowIds.If, ("conditions", conditions)).WithFlow(g, FlowRole.Start));
        then(this);
        if (otherwise is not null)
        {
            Add(ActionInstance.Create(FlowIds.If).WithFlow(g, FlowRole.Middle, "Otherwise"));
            otherwise(this);
        }
        Add(ActionInstance.Create(FlowIds.If).WithFlow(g, FlowRole.End));
        return start;
    }

    public string Menu(ParamValue prompt, params (string Label, Action<ShortcutBuilder> Body)[] options)
    {
        var g = Guid.NewGuid().ToString("N")[..8];
        var start = Add(ActionInstance.Create(FlowIds.Menu, ("prompt", prompt),
            ("items", ParamValue.List(options.Select(o => o.Label).ToArray()))).WithFlow(g, FlowRole.Start));
        foreach (var (label, body) in options)
        {
            Add(ActionInstance.Create(FlowIds.Menu).WithFlow(g, FlowRole.Middle, label));
            body(this);
        }
        Add(ActionInstance.Create(FlowIds.Menu).WithFlow(g, FlowRole.End));
        return start;
    }

    public string Repeat(ParamValue count, Action<ShortcutBuilder> body) =>
        Loop(FlowIds.Repeat, ("count", count), body);

    public string RepeatEach(ParamValue items, Action<ShortcutBuilder> body) =>
        Loop(FlowIds.RepeatEach, ("items", items), body);

    private string Loop(string id, (string, ParamValue) p, Action<ShortcutBuilder> body)
    {
        var g = Guid.NewGuid().ToString("N")[..8];
        var start = Add(ActionInstance.Create(id, p).WithFlow(g, FlowRole.Start));
        body(this);
        Add(ActionInstance.Create(id).WithFlow(g, FlowRole.End));
        return start;
    }
}
