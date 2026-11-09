namespace AutomationPortal.Core;

/// <summary>Well-known ids of the control-flow actions.</summary>
public static class FlowIds
{
    public const string Comment = "flow.comment";
    public const string If = "flow.if";
    public const string Menu = "flow.menu";
    public const string Repeat = "flow.repeat";
    public const string RepeatEach = "flow.repeatEach";
    public const string Wait = "flow.wait";
    public const string Stop = "flow.stop";
    public const string Output = "flow.output";
    public const string RunShortcut = "flow.runShortcut";
    public const string Nothing = "flow.nothing";

    public static FlowKind KindOf(string actionId) => actionId switch
    {
        If => FlowKind.If,
        Menu => FlowKind.Menu,
        Repeat => FlowKind.Repeat,
        RepeatEach => FlowKind.RepeatEach,
        _ => FlowKind.None,
    };
}

/// <summary>A node of the parsed shortcut tree.</summary>
public abstract record FlowNode;
public sealed record ActionNode(ActionInstance Action) : FlowNode;
public sealed record IfNode(ActionInstance Start, IReadOnlyList<FlowNode> Then, IReadOnlyList<FlowNode> Else, bool HasOtherwise) : FlowNode;
public sealed record MenuBranch(ActionInstance Marker, string Label, IReadOnlyList<FlowNode> Body);
public sealed record MenuNode(ActionInstance Start, IReadOnlyList<MenuBranch> Branches) : FlowNode;
public sealed record RepeatNode(ActionInstance Start, bool Each, IReadOnlyList<FlowNode> Body) : FlowNode;

/// <summary>Thrown when If/Menu/Repeat parts don't nest properly. The message is friendly.</summary>
public sealed class FlowStructureException : Exception
{
    public FlowStructureException(string message, string? uid) : base(message) { Uid = uid; }
    public string? Uid { get; }
}

/// <summary>Parses the flat action list into a tree (SPEC §3.2).</summary>
public static class FlowTree
{
    private sealed class Frame
    {
        public required ActionInstance Start;
        public FlowKind Kind;
        public List<FlowNode> Then = new();
        public List<FlowNode> Else = new();
        public bool InElse;
        public bool HasOtherwise;
        public ActionInstance? CurrentMarker;
        public string? CurrentLabel;
        public List<FlowNode>? CurrentBranch;
        public List<MenuBranch> Branches = new();
        public List<FlowNode> Current => Kind switch
        {
            FlowKind.If => InElse ? Else : Then,
            FlowKind.Menu => CurrentBranch ?? throw new FlowStructureException("Put the actions inside one of the menu options.", Start.Uid),
            _ => Then,
        };
    }

    public static IReadOnlyList<FlowNode> Parse(IReadOnlyList<ActionInstance> actions)
    {
        var root = new List<FlowNode>();
        var stack = new Stack<Frame>();
        List<FlowNode> Target() => stack.Count == 0 ? root : stack.Peek().Current;

        foreach (var a in actions)
        {
            var kind = FlowIds.KindOf(a.ActionId);
            if (a.Flow is null)
            {
                if (kind != FlowKind.None)
                    throw new FlowStructureException($"'{a.ActionId}' is missing its group information. Delete it and add it again.", a.Uid);
                Target().Add(new ActionNode(a));
                continue;
            }
            if (kind == FlowKind.None)
                throw new FlowStructureException($"'{a.ActionId}' can't be part of a group.", a.Uid);

            switch (a.Flow.Role)
            {
                case FlowRole.Start:
                    var f = new Frame { Start = a, Kind = kind };
                    stack.Push(f);
                    break;

                case FlowRole.Middle:
                    {
                        var top = Expect(stack, a, "an 'Otherwise' or menu option");
                        if (top.Kind == FlowKind.If)
                        {
                            if (top.InElse) throw new FlowStructureException("An If can only have one 'Otherwise'.", a.Uid);
                            top.InElse = true; top.HasOtherwise = true;
                        }
                        else if (top.Kind == FlowKind.Menu)
                        {
                            FinishBranch(top);
                            top.CurrentMarker = a;
                            top.CurrentLabel = a.Flow.Label ?? "";
                            top.CurrentBranch = new List<FlowNode>();
                        }
                        else throw new FlowStructureException("Repeat blocks don't have separate parts. Remove this item.", a.Uid);
                        break;
                    }

                case FlowRole.End:
                    {
                        var top = Expect(stack, a, "an end marker");
                        stack.Pop();
                        FlowNode node;
                        switch (top.Kind)
                        {
                            case FlowKind.If: node = new IfNode(top.Start, top.Then, top.Else, top.HasOtherwise); break;
                            case FlowKind.Menu: FinishBranch(top); node = new MenuNode(top.Start, top.Branches); break;
                            default: node = new RepeatNode(top.Start, top.Kind == FlowKind.RepeatEach, top.Then); break;
                        }
                        Target().Add(node);
                        break;
                    }
            }
        }

        if (stack.Count > 0)
        {
            var open = stack.Peek().Start;
            throw new FlowStructureException($"This {Describe(open.ActionId)} is missing its end. Add the matching end marker or delete it.", open.Uid);
        }
        return root;
    }

    private static void FinishBranch(Frame f)
    {
        if (f.CurrentMarker is not null)
            f.Branches.Add(new MenuBranch(f.CurrentMarker, f.CurrentLabel ?? "", f.CurrentBranch ?? new()));
        f.CurrentMarker = null; f.CurrentBranch = null;
    }

    private static Frame Expect(Stack<Frame> stack, ActionInstance a, string what)
    {
        if (stack.Count == 0)
            throw new FlowStructureException($"Found {what} without a matching start. Delete it or add the start back.", a.Uid);
        var top = stack.Peek();
        if (top.Start.Flow!.GroupId != a.Flow!.GroupId || top.Start.ActionId != a.ActionId)
            throw new FlowStructureException($"This {Describe(a.ActionId)} part doesn't line up with its group. Move or delete it.", a.Uid);
        return top;
    }

    private static string Describe(string actionId) => actionId switch
    {
        FlowIds.If => "If",
        FlowIds.Menu => "menu",
        FlowIds.Repeat => "Repeat",
        FlowIds.RepeatEach => "Repeat with Each",
        _ => "step",
    };
}
