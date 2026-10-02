using Flowmate.Core;

namespace Flowmate.Editor.Logic;

internal static class ActionFactory
{
    public static string NewGroupId() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Creates the instance(s) for an action from the panel. Control-flow actions come as a whole group:
    /// If + Otherwise + End If, Menu with two options, Repeat + End Repeat.
    /// </summary>
    public static List<ActionInstance> Create(ActionDescriptor d, VariableOption? previous = null)
    {
        var start = new ActionInstance { ActionId = d.Id };
        if (d.FlowKind == FlowKind.None) return new List<ActionInstance> { start };

        var gid = NewGroupId();
        start.WithFlow(gid, FlowRole.Start);
        var list = new List<ActionInstance> { start };
        switch (d.FlowKind)
        {
            case FlowKind.If:
                start.Parameters["conditions"] = ParamValue.AllOf(ConditionModel.NewRow(previous));
                list.Add(new ActionInstance { ActionId = d.Id }.WithFlow(gid, FlowRole.Middle));
                break;
            case FlowKind.Menu:
                start.Parameters["items"] = ParamValue.List("Option 1", "Option 2");
                list.Add(new ActionInstance { ActionId = d.Id }.WithFlow(gid, FlowRole.Middle, "Option 1"));
                list.Add(new ActionInstance { ActionId = d.Id }.WithFlow(gid, FlowRole.Middle, "Option 2"));
                break;
        }
        list.Add(new ActionInstance { ActionId = d.Id }.WithFlow(gid, FlowRole.End));
        return list;
    }
}
