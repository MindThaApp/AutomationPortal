namespace Flowmate.App.Logic;

/// <summary>Drag-to-reorder maths for the library grid.</summary>
public static class ShortcutOrdering
{
    /// <summary>
    /// Returns the ids in their new order after dragging <paramref name="moved"/> to just before
    /// <paramref name="beforeId"/> (null = to the end). Ids not in the list are ignored.
    /// </summary>
    public static List<Guid> MoveBefore(IReadOnlyList<Guid> order, Guid moved, Guid? beforeId)
    {
        if (!order.Contains(moved)) return order.ToList();
        var list = order.Where(i => i != moved).ToList();
        var at = beforeId is { } b ? list.IndexOf(b) : -1;
        if (at < 0) list.Add(moved); else list.Insert(at, moved);
        return list;
    }

    /// <summary>
    /// The grid may show only some of the shortcuts (a folder, Pinned). Puts the re-ordered visible ones back into
    /// the slots the visible ones used in the full list, leaving everything else where it was.
    /// </summary>
    public static List<Guid> ApplyToGlobal(IReadOnlyList<Guid> globalOrder, IReadOnlyList<Guid> visibleNewOrder)
    {
        var visible = new HashSet<Guid>(visibleNewOrder);
        var result = globalOrder.ToList();
        var next = 0;
        for (var i = 0; i < result.Count; i++)
            if (visible.Contains(result[i]) && next < visibleNewOrder.Count) result[i] = visibleNewOrder[next++];
        return result;
    }
}
