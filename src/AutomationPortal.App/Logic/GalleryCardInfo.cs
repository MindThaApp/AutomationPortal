using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

/// <summary>What a Gallery card says about its shortcut: whether it is already in the library and which other app it needs.</summary>
public static class GalleryCardInfo
{
    /// <summary>The app whose steps the shortcut uses ("Power Plan Manager", "Desk Pal"), or null when it needs nothing else.</summary>
    public static string? NeededApp(Shortcut shortcut)
    {
        foreach (var a in shortcut.Actions)
        {
            if (a.ActionId.StartsWith("ppm.", StringComparison.OrdinalIgnoreCase)) return "Power Plan Manager";
            if (a.ActionId.StartsWith("deskpal.", StringComparison.OrdinalIgnoreCase)) return "Desk Pal";
        }
        return null;
    }

    /// <summary>True when the library has a shortcut with this name or a renamed copy of it ("Name 2", "Name (2)").</summary>
    public static bool IsAdded(string name, IEnumerable<string> libraryNames)
    {
        foreach (var existing in libraryNames)
        {
            if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)) return true;
            if (!existing.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase)) continue;
            var rest = existing[(name.Length + 1)..].Trim('(', ')');
            if (rest.Length > 0 && rest.All(char.IsDigit)) return true;
        }
        return false;
    }
}
