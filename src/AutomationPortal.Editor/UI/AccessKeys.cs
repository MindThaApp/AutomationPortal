using System.Windows.Automation;
using System.Windows.Controls.Primitives;

namespace AutomationPortal.Editor.UI;

/// <summary>Gives the buttons of one dialog distinct access keys (Alt+letter), earlier buttons first, and keeps each spoken name free of the underscore.</summary>
public static class AccessKeys
{
    public static void Apply(IEnumerable<ButtonBase> buttons)
    {
        var order = buttons.Where(b => b.Content is string).ToList();
        var used = new HashSet<char>();
        foreach (var b in order)
        {
            var s = (string)b.Content;
            var at = s.IndexOf('_');
            if (at >= 0 && at + 1 < s.Length) used.Add(char.ToUpperInvariant(s[at + 1]));
        }
        foreach (var b in order)
        {
            var text = (string)b.Content;
            if (text.Contains('_')) { AutomationProperties.SetName(b, text.Replace("_", "")); continue; }
            for (var i = 0; i < text.Length; i++)
            {
                if (!char.IsLetter(text[i]) || !used.Add(char.ToUpperInvariant(text[i]))) continue;
                b.Content = text.Insert(i, "_");
                break;
            }
            AutomationProperties.SetName(b, text);
        }
    }
}
