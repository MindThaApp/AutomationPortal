using AutomationPortal.Core;

namespace AutomationPortal.Editor.Logic;

/// <summary>Helpers for the flat action list with If / Menu / Repeat groups (SPEC §3.2).</summary>
internal static class FlowGroups
{
    public static bool IsRole(ActionInstance a, FlowRole role) => a.Flow is { } f && f.Role == role;

    /// <summary>Nesting depth of every action: group contents are one deeper than their Start; Otherwise / End bars sit at their Start's depth.</summary>
    public static int[] Depths(IReadOnlyList<ActionInstance> actions)
    {
        var depths = new int[actions.Count];
        var open = 0;
        for (var i = 0; i < actions.Count; i++)
        {
            var f = actions[i].Flow;
            if (f is null) { depths[i] = open; continue; }
            switch (f.Role)
            {
                case FlowRole.Start: depths[i] = open; open++; break;
                case FlowRole.Middle: depths[i] = Math.Max(0, open - 1); break;
                default: open = Math.Max(0, open - 1); depths[i] = open; break;
            }
        }
        return depths;
    }

    /// <summary>First and last index (inclusive) of the whole group the action at <paramref name="index"/> belongs to; a plain action is its own group.</summary>
    public static (int Start, int End) GroupRange(IReadOnlyList<ActionInstance> actions, int index)
    {
        var f = actions[index].Flow;
        if (f is null) return (index, index);
        var gid = f.GroupId;
        var start = index;
        if (f.Role != FlowRole.Start)
        {
            start = -1;
            for (var i = index; i >= 0; i--)
                if (actions[i].Flow is { } g && g.GroupId == gid && g.Role == FlowRole.Start) { start = i; break; }
            if (start < 0) start = FirstWithGroup(actions, gid, index);
        }
        var end = -1;
        for (var i = Math.Max(index, start); i < actions.Count; i++)
            if (actions[i].Flow is { } g && g.GroupId == gid && g.Role == FlowRole.End) { end = i; break; }
        if (end < 0) end = LastWithGroup(actions, gid, index);
        return (start, Math.Max(end, start));
    }

    private static int FirstWithGroup(IReadOnlyList<ActionInstance> a, string gid, int fallback)
    {
        for (var i = 0; i < a.Count; i++) if (a[i].Flow?.GroupId == gid) return i;
        return fallback;
    }

    private static int LastWithGroup(IReadOnlyList<ActionInstance> a, string gid, int fallback)
    {
        for (var i = a.Count - 1; i >= 0; i--) if (a[i].Flow?.GroupId == gid) return i;
        return fallback;
    }

    /// <summary>For an Otherwise / menu-option bar: the bar plus the actions inside that branch (up to the next bar of the same group).</summary>
    public static (int Start, int End) BranchRange(IReadOnlyList<ActionInstance> actions, int middleIndex)
    {
        var gid = actions[middleIndex].Flow!.GroupId;
        var end = middleIndex;
        for (var i = middleIndex + 1; i < actions.Count; i++)
        {
            if (actions[i].Flow is { } g && g.GroupId == gid && g.Role != FlowRole.Start) break;
            end = i;
        }
        return (middleIndex, end);
    }

    /// <summary>Index of the outermost group start containing <paramref name="index"/>, or the index itself when it is top level.</summary>
    public static int TopLevelStart(IReadOnlyList<ActionInstance> actions, int index)
    {
        var depths = Depths(actions);
        var i = index;
        if (actions[i].Flow is { Role: not FlowRole.Start }) i = GroupRange(actions, i).Start;
        while (depths[i] > 0)
        {
            // walk back to the enclosing start (the nearest earlier Start at depth - 1 that is still open)
            var target = depths[i] - 1;
            var j = i - 1;
            while (j >= 0 && !(actions[j].Flow is { Role: FlowRole.Start } && depths[j] == target && GroupRange(actions, j).End >= i)) j--;
            if (j < 0) break;
            i = j;
        }
        return i;
    }

    /// <summary>Whole-group ranges (sorted, merged, nested ones dropped) covering the given action indexes.</summary>
    public static List<(int Start, int End)> ExpandToBlocks(IReadOnlyList<ActionInstance> actions, IEnumerable<int> indexes)
    {
        var ranges = indexes.Where(i => i >= 0 && i < actions.Count).Select(i => GroupRange(actions, i)).OrderBy(r => r.Start).ThenByDescending(r => r.End).ToList();
        var result = new List<(int Start, int End)>();
        foreach (var r in ranges)
        {
            if (result.Count > 0 && r.Start <= result[^1].End) { if (r.End > result[^1].End) result[^1] = (result[^1].Start, r.End); continue; }
            result.Add(r);
        }
        return result;
    }

    /// <summary>Makes an insertion point legal: nothing may sit between a Choose from Menu start and its first option.</summary>
    public static int ClampInsertIndex(IReadOnlyList<ActionInstance> actions, int index)
    {
        index = Math.Clamp(index, 0, actions.Count);
        if (index > 0 && index < actions.Count
            && actions[index - 1].Flow is { Role: FlowRole.Start } && FlowIds.KindOf(actions[index - 1].ActionId) == FlowKind.Menu
            && actions[index].Flow is { Role: FlowRole.Middle })
            index++;
        return index;
    }

    /// <summary>The open group starts above position <paramref name="index"/> (outermost first).</summary>
    public static List<ActionInstance> OpenGroupsAt(IReadOnlyList<ActionInstance> actions, int index)
    {
        var stack = new List<ActionInstance>();
        for (var i = 0; i < Math.Min(index, actions.Count); i++)
        {
            var a = actions[i];
            if (a.Flow is null) continue;
            if (a.Flow.Role == FlowRole.Start) stack.Add(a);
            else if (a.Flow.Role == FlowRole.End && stack.Count > 0) stack.RemoveAt(stack.Count - 1);
        }
        return stack;
    }
}
