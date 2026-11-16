namespace AutomationPortal.Core;

/// <summary>The text a person can copy to start a shortcut from outside the app: the <c>ap run</c> command and the automationportal:// link.</summary>
public static class RunFromElsewhere
{
    private const char Backslash = (char)92;

    /// <summary><c>ap run "Name"</c>, with a quote (and any backslashes before it) escaped so a shell passes the whole name on as one argument.</summary>
    public static string Command(string name)
    {
        var sb = new System.Text.StringBuilder("ap run \"");
        var backslashes = 0;
        foreach (var c in name ?? "")
        {
            if (c == Backslash) { backslashes++; continue; }
            if (c == '"') { sb.Append(Backslash, backslashes * 2 + 1).Append('"'); backslashes = 0; continue; }
            sb.Append(Backslash, backslashes).Append(c);
            backslashes = 0;
        }
        // Backslashes right before the closing quote must be doubled too.
        sb.Append(Backslash, backslashes * 2).Append('"');
        return sb.ToString();
    }

    /// <summary><c>automationportal://run?name=My%20Shortcut</c>: the name is percent-encoded, so spaces, quotes, ampersands and # survive.</summary>
    public static string Link(string name) => "automationportal://run?name=" + Uri.EscapeDataString(name ?? "");
}
