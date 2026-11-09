namespace AutomationPortal.Actions.Documents;

/// <summary>Refuses to delete, move, rename or replace a drive, the Windows folder, the user profile or a known folder as a whole.</summary>
internal static class ProtectedPaths
{
    /// <param name="verb">"delete", "move", "rename" or "replace".</param>
    public static void Ensure(string path, string verb)
    {
        if (!PathSafety.IsProtected(path)) return;
        throw new ActionException($"Automation Portal won't {verb} {path.Trim()} itself (a protected folder). Check the path, and any variables in it, still point to what you meant.");
    }
}
