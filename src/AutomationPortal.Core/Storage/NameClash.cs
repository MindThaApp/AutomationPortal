namespace AutomationPortal.Core;

/// <summary>Picks a name that isn't taken yet: "Name", then "Name (2)", "Name (3)" and so on. Nothing that exists is renamed or replaced.</summary>
public static class NameClash
{
    public static string Unique(string? wanted, Func<string, bool> isTaken, string fallback = "Imported Shortcut")
    {
        var baseName = string.IsNullOrWhiteSpace(wanted) ? fallback : wanted.Trim();
        var name = baseName;
        for (var n = 2; isTaken(name); n++) name = $"{baseName} ({n})";
        return name;
    }
}
