using System.Text.Json;
using AutomationPortal.Core;

namespace AutomationPortal.Editor.Logic;

/// <summary>Copy / paste / duplicate of actions as JSON, with fresh Uids and rewritten internal variable references.</summary>
internal static class ActionClipboard
{
    public const string Format = "autoportal.actions";

    public static string Serialize(IEnumerable<ActionInstance> actions)
    {
        var list = actions.ToList();
        return JsonSerializer.Serialize(new Envelope { Format = Format, Version = 1, Actions = list }, AutomationPortalJson.Options);
    }

    /// <summary>Null when the text isn't an Automation Portal action list.</summary>
    public static List<ActionInstance>? TryDeserialize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.TrimStart().StartsWith('{') || !text.Contains(Format)) return null;
        try
        {
            var env = JsonSerializer.Deserialize<Envelope>(text, AutomationPortalJson.Options);
            return env is { Format: Format, Actions.Count: > 0 } ? env.Actions : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Deep-copies the actions giving every one a new Uid and every group a new GroupId. References to actions that are
    /// part of the copied set follow the new Uids; references to other actions are left alone.
    /// </summary>
    public static List<ActionInstance> Remap(IReadOnlyList<ActionInstance> items)
    {
        var clones = items.Select(i => i.Clone()).ToList();
        var uidMap = new Dictionary<string, string>();
        var groupMap = new Dictionary<string, string>();
        foreach (var c in clones)
        {
            var newUid = ActionInstance.NewUid();
            uidMap.TryAdd(c.Uid, newUid);
            c.Uid = newUid;
        }
        foreach (var c in clones)
        {
            if (c.Flow is { } f)
            {
                if (!groupMap.TryGetValue(f.GroupId, out var g)) groupMap[f.GroupId] = g = ActionFactory.NewGroupId();
                f.GroupId = g;
            }
        }
        VarRef Map(VarRef r) => r.Kind == VarKind.Output && r.Uid is { } u && uidMap.TryGetValue(u, out var nu) ? r with { Uid = nu } : r;
        foreach (var c in clones)
            foreach (var key in c.Parameters.Keys.ToList())
                c.Parameters[key] = c.Parameters[key].MapVars(Map);
        return clones;
    }

    private sealed class Envelope
    {
        public string Format { get; set; } = "";
        public int Version { get; set; }
        public List<ActionInstance> Actions { get; set; } = new();
    }
}
