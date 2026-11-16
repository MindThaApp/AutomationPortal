using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AutomationPortal.Core.AppControl;

/// <summary>How a step of a <see cref="UiSelector"/> path recognises its control, strongest first.</summary>
public enum UiMatchKey { ById, ByName, ByType }

/// <summary>
/// One level of the path from a window down to a control. <see cref="Index"/> tells apart siblings that look the same under the
/// node's strongest key (the second "OK" button in the same pane is index 1).
/// </summary>
public sealed record UiNode(string ControlType, string AutomationId = "", string Name = "", string ClassName = "", int Index = 0)
{
    public UiMatchKey Key => AutomationId.Length > 0 ? UiMatchKey.ById : Name.Length > 0 ? UiMatchKey.ByName : UiMatchKey.ByType;

    /// <summary>
    /// The keys to try in order: the id, then the name. The type alone is used only for a node that has neither, because a
    /// named control that is gone must not be swapped for another control of the same type.
    /// </summary>
    public IEnumerable<UiMatchKey> Keys
    {
        get
        {
            if (AutomationId.Length > 0) yield return UiMatchKey.ById;
            if (Name.Length > 0) yield return UiMatchKey.ByName;
            if (AutomationId.Length == 0 && Name.Length == 0) yield return UiMatchKey.ByType;
        }
    }

    public bool Matches(IUiElement e, UiMatchKey key) => key switch
    {
        UiMatchKey.ById => Eq(e.AutomationId, AutomationId) && Eq(e.ControlType, ControlType),
        UiMatchKey.ByName => Eq(e.Name, Name) && Eq(e.ControlType, ControlType),
        _ => Eq(e.ControlType, ControlType) && Eq(e.ClassName, ClassName),
    };

    private static bool Eq(string? a, string? b) => string.Equals(a ?? "", b ?? "", StringComparison.Ordinal);

    /// <summary>The node for an element, with its index among the siblings that share its strongest key.</summary>
    public static UiNode Of(IUiElement e)
    {
        var node = new UiNode(e.ControlType ?? "", e.AutomationId ?? "", e.Name ?? "", e.ClassName ?? "");
        if (e.Parent is not { } parent) return node;
        var index = 0;
        foreach (var sibling in parent.Children)
        {
            if (sibling.SameAs(e)) return node with { Index = index };
            if (node.Matches(sibling, node.Key)) index++;
        }
        return node;
    }
}

/// <summary>
/// Where a control is: the app's process, a window title pattern ("* - Notepad"), and the path from that window down to the
/// control. No screen positions: a window that moved or changed size is still found.
/// </summary>
public sealed record UiSelector(string Process, string WindowTitle, IReadOnlyList<UiNode> Path)
{
    public UiNode? Target => Path.Count > 0 ? Path[^1] : null;

    /// <summary>The control has neither an id nor a name, so only its place in the window identifies it.</summary>
    public bool IsWeak => Target is { AutomationId.Length: 0, Name.Length: 0 };

    public string PathText => UiPathText.Format(Path);

    /// <summary>Same process, window pattern and path.</summary>
    public bool SameAs(UiSelector other) =>
        string.Equals(Process, other.Process, StringComparison.OrdinalIgnoreCase) && WindowTitle == other.WindowTitle && PathText == other.PathText;

    /// <summary>"notepad" from "notepad", "notepad.exe" or "C:\Windows\notepad.exe". An app id that names no program file stays as it is.</summary>
    public static string ProcessFromAppId(string appId)
    {
        var id = (appId ?? "").Trim().Trim('"');
        if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || id.Contains('\\') || id.Contains('/'))
        {
            try { return System.IO.Path.GetFileNameWithoutExtension(id); } catch (ArgumentException) { return id; }
        }
        return id;
    }
}

/// <summary>Window title patterns: "*" stands for any text, case is ignored.</summary>
public static class UiTitlePattern
{
    private static readonly string[] Separators = { " - ", " — ", " – ", " | " };

    /// <summary>
    /// "Untitled - Notepad" becomes "* - Notepad": the part that names the app stays, the part that names the document may change.
    /// A title without such a separator is kept as it is.
    /// </summary>
    public static string From(string? title)
    {
        var t = (title ?? "").Trim();
        if (t.Length == 0) return "";
        var best = -1;
        var sep = "";
        foreach (var s in Separators)
        {
            var i = t.LastIndexOf(s, StringComparison.Ordinal);
            if (i > 0 && i > best) { best = i; sep = s; }
        }
        if (best <= 0) return t.Replace("*", "", StringComparison.Ordinal);
        var tail = t[(best + sep.Length)..].Replace("*", "", StringComparison.Ordinal);
        return tail.Length == 0 ? t.Replace("*", "", StringComparison.Ordinal) : "*" + sep + tail;
    }

    public static bool Matches(string? pattern, string? title)
    {
        var p = (pattern ?? "").Trim();
        if (p.Length == 0) return true;
        var regex = "^" + Regex.Escape(p).Replace("\\*", ".*", StringComparison.Ordinal) + "$";
        return Regex.IsMatch((title ?? "").Trim(), regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromSeconds(1));
    }
}

/// <summary>
/// The text form of a path, stored in a step: <c>Pane(id=Main) / Button(name=Save; index=1)</c>. Keys are id, name, class and
/// index; a backslash escapes <c>\ ; ) /</c> inside values.
/// </summary>
public static class UiPathText
{
    public static string Format(IReadOnlyList<UiNode> path)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < path.Count; i++)
        {
            if (i > 0) sb.Append(" / ");
            var n = path[i];
            sb.Append(Escape(n.ControlType)).Append('(');
            var first = true;
            void Field(string key, string value)
            {
                if (value.Length == 0) return;
                if (!first) sb.Append("; ");
                sb.Append(key).Append('=').Append(Escape(value));
                first = false;
            }
            Field("id", n.AutomationId);
            Field("name", n.Name);
            Field("class", n.ClassName);
            if (n.Index > 0) Field("index", n.Index.ToString(CultureInfo.InvariantCulture));
            sb.Append(')');
        }
        return sb.ToString();
    }

    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c is '\\' or ';' or ')' or '/' or '(' or '=') sb.Append('\\');
            sb.Append(c is '\r' or '\n' ? ' ' : c);
        }
        return sb.ToString();
    }

    /// <summary>The path, or null when the text isn't a path this format describes. "" is the window itself (an empty path).</summary>
    public static IReadOnlyList<UiNode>? Parse(string? text)
    {
        var s = text ?? "";
        var list = new List<UiNode>();
        var i = 0;
        SkipSpaces(s, ref i);
        if (i >= s.Length) return list;
        while (true)
        {
            SkipSpaces(s, ref i);
            var type = ReadUntil(s, ref i, '(');
            if (type is null || i >= s.Length || s[i] != '(') return null;
            i++;
            string id = "", name = "", cls = "";
            var index = 0;
            SkipSpaces(s, ref i);
            while (i < s.Length && s[i] != ')')
            {
                var key = ReadUntil(s, ref i, '=');
                if (key is null || i >= s.Length || s[i] != '=') return null;
                i++;
                var value = ReadValue(s, ref i);
                if (value is null) return null;
                switch (key.Trim())
                {
                    case "id": id = value; break;
                    case "name": name = value; break;
                    case "class": cls = value; break;
                    case "index":
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out index) || index < 0) return null;
                        break;
                    default: return null;
                }
                if (i < s.Length && s[i] == ';') { i++; SkipSpaces(s, ref i); }
            }
            if (i >= s.Length) return null;
            i++; // ')'
            var trimmedType = type.Trim();
            if (trimmedType.Length == 0) return null;
            list.Add(new UiNode(trimmedType, id, name, cls, index));
            SkipSpaces(s, ref i);
            if (i >= s.Length) return list;
            if (s[i] != '/') return null;
            i++;
        }
    }

    private static void SkipSpaces(string s, ref int i) { while (i < s.Length && s[i] == ' ') i++; }

    private static string? ReadUntil(string s, ref int i, char stop)
    {
        var sb = new StringBuilder();
        while (i < s.Length && s[i] != stop)
        {
            if (s[i] is ';' or ')' or '/') return null;
            if (s[i] == '\\') { if (i + 1 >= s.Length) return null; i++; }
            sb.Append(s[i]);
            i++;
        }
        return sb.ToString();
    }

    private static string? ReadValue(string s, ref int i)
    {
        var sb = new StringBuilder();
        while (i < s.Length && s[i] is not (';' or ')'))
        {
            if (s[i] == '/') return null;
            if (s[i] == '\\') { if (i + 1 >= s.Length) return null; i++; }
            sb.Append(s[i]);
            i++;
        }
        return sb.ToString();
    }
}

/// <summary>Builds a <see cref="UiSelector"/> for an element the user clicked or typed into.</summary>
public static class UiSelectorBuilder
{
    private const int MaxDepth = 64;

    /// <summary>The clicked element, or the nearest ancestor (up to three levels) that can be clicked through UI Automation: a click on the text inside a button is a click on the button.</summary>
    public static IUiElement ClickTarget(IUiElement element)
    {
        var e = element;
        for (var i = 0; i <= 3 && e is not null; i++)
        {
            if (CanClick(e)) return e;
            e = e.Parent;
        }
        return element;
    }

    public static bool CanClick(IUiElement e) =>
        e.Supports(UiPattern.Invoke) || e.Supports(UiPattern.Toggle) || e.Supports(UiPattern.SelectionItem) || e.Supports(UiPattern.ExpandCollapse);

    /// <summary>The top-level window the element is in (the element itself when it is one).</summary>
    public static IUiElement WindowOf(IUiElement element)
    {
        var e = element;
        for (var i = 0; i < MaxDepth && e.Parent is { } p; i++) e = p;
        return e;
    }

    public static UiSelector Build(IUiElement element, IUiDesktop desktop)
    {
        var chain = new List<IUiElement>();
        var e = element;
        for (var i = 0; i < MaxDepth && e.Parent is { } p; i++) { chain.Add(e); e = p; }
        chain.Reverse();
        var window = e;
        var path = chain.Select(UiNode.Of).ToList();
        return new UiSelector(desktop.ProcessName(window.ProcessId), UiTitlePattern.From(window.Name), path);
    }
}

/// <summary>What looking for a control found: the element, or a plain reason it wasn't found.</summary>
public sealed record UiFindResult(IUiElement? Element, string Problem)
{
    public bool Found => Element is not null;
}

/// <summary>
/// Finds the control a <see cref="UiSelector"/> describes. It walks the recorded path first (strongest key, then weaker keys when
/// exactly one control fits), then searches the whole window for the control's id or name. When more than one control fits equally
/// well it reports that instead of picking one: it never guesses.
/// </summary>
public static class UiSelectorMatcher
{
    private const int MaxSearched = 5000;

    public static UiFindResult Find(IUiDesktop desktop, UiSelector selector, string appName)
    {
        var app = string.IsNullOrWhiteSpace(appName) ? selector.Process : appName;
        var windows = desktop.TopWindows().Where(w => string.Equals(desktop.ProcessName(w.ProcessId), selector.Process, StringComparison.OrdinalIgnoreCase)).ToList();
        if (windows.Count == 0) return new(null, $"{app} isn't open.");
        var titled = windows.Where(w => UiTitlePattern.Matches(selector.WindowTitle, w.Name)).ToList();
        if (titled.Count == 0)
        {
            if (windows.Count == 1 && selector.Path.Count > 0) titled = windows;
            else return new(null, $"No {app} window has a title like \"{selector.WindowTitle}\".");
        }
        if (selector.Path.Count == 0) return new(titled[0], "");

        string? ambiguity = null;
        foreach (var window in titled)
        {
            var walked = Walk(window, selector.Path);
            if (walked is not null) return new(walked, "");
            var (found, problem) = Search(window, selector.Path);
            if (found is not null) return new(found, "");
            ambiguity ??= problem;
        }
        return new(null, ambiguity ?? "It isn't in the window right now.");
    }

    private static IUiElement? Walk(IUiElement window, IReadOnlyList<UiNode> path)
    {
        var current = window;
        foreach (var node in path)
        {
            var children = current.Children;
            IUiElement? next = null;
            var strongest = children.Where(c => node.Matches(c, node.Key)).ToList();
            if (strongest.Count > node.Index) next = strongest[node.Index];
            else if (strongest.Count == 0)
            {
                foreach (var key in node.Keys.Skip(1))
                {
                    var fits = children.Where(c => node.Matches(c, key)).ToList();
                    if (fits.Count == 1) { next = fits[0]; break; }
                    if (fits.Count > 1) break;
                }
            }
            if (next is null) return null;
            current = next;
        }
        return current;
    }

    private static (IUiElement? Found, string? Problem) Search(IUiElement window, IReadOnlyList<UiNode> path)
    {
        var target = path[^1];
        if (target.Key == UiMatchKey.ByType) return (null, null);
        var hits = new List<IUiElement>();
        var queue = new Queue<IUiElement>(window.Children);
        var seen = 0;
        while (queue.Count > 0 && seen++ < MaxSearched)
        {
            var e = queue.Dequeue();
            if (target.Matches(e, target.Key)) hits.Add(e);
            foreach (var c in e.Children) queue.Enqueue(c);
        }
        if (hits.Count == 1) return (hits[0], null);
        if (hits.Count == 0) return (null, null);
        if (path.Count >= 2)
        {
            var parentNode = path[^2];
            var underParent = hits.Where(h => h.Parent is { } p && parentNode.Matches(p, parentNode.Key)).ToList();
            if (underParent.Count == 1) return (underParent[0], null);
        }
        return (null, $"{hits.Count} controls look like it, so Automation Portal didn't pick one.");
    }
}
