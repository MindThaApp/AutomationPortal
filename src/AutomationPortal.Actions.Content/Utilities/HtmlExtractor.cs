using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Flowmate.Actions.Content.Utilities;

public sealed record PageLink(Uri Url, string Text);

/// <summary>Pulls readable text, links and images out of HTML (HtmlAgilityPack).</summary>
public static class HtmlExtractor
{
    private static readonly HashSet<string> Junk = new(StringComparer.OrdinalIgnoreCase)
        { "script", "style", "noscript", "template", "svg", "iframe", "canvas", "nav", "footer", "aside", "button", "select", "dialog", "object", "embed", "head" };

    private static readonly HashSet<string> Blocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "section", "article", "main", "header", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "tr", "table", "blockquote",
        "pre", "hr", "figure", "figcaption", "dl", "dt", "dd", "address", "details", "summary", "fieldset",
    };

    public static bool LooksLikeHtml(string text)
    {
        var t = text.TrimStart();
        return t.StartsWith('<') && (t.Contains("</", StringComparison.Ordinal) || t.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase) || t.Contains("<br", StringComparison.OrdinalIgnoreCase));
    }

    private static HtmlDocument Load(string html)
    {
        var doc = new HtmlDocument { OptionFixNestedTags = true };
        doc.LoadHtml(html);
        return doc;
    }

    public static string? GetTitle(string html) => GetTitle(Load(html));

    private static string? GetTitle(HtmlDocument doc)
    {
        var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText;
        if (string.IsNullOrWhiteSpace(title))
            title = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']")?.GetAttributeValue("content", "");
        if (string.IsNullOrWhiteSpace(title))
            title = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText;
        if (string.IsNullOrWhiteSpace(title)) return null;
        return Regex.Replace(HtmlEntity.DeEntitize(title), @"\s+", " ").Trim();
    }

    /// <summary>The readable text of a page (main article if there is one), with the page title.</summary>
    public static (string? Title, string Text) ExtractText(string html)
    {
        var doc = Load(html);
        var title = GetTitle(doc);
        var body = doc.DocumentNode;

        foreach (var c in body.Descendants().Where(n => n.NodeType == HtmlNodeType.Comment).ToList()) c.Remove();
        var root = PickRoot(body);

        var sb = new StringBuilder();
        Walk(root, sb, false, root);
        var text = Normalize(sb.ToString());
        return (title, text);
    }

    private static HtmlNode PickRoot(HtmlNode doc)
    {
        var articles = doc.SelectNodes("//article");
        if (articles is not null)
        {
            var best = articles.OrderByDescending(a => a.InnerText.Length).First();
            if (best.InnerText.Trim().Length > 200) return best;
        }
        var main = doc.SelectSingleNode("//main") ?? doc.SelectSingleNode("//*[@role='main']");
        if (main is not null && main.InnerText.Trim().Length > 100) return main;
        return doc.SelectSingleNode("//body") ?? doc;
    }

    private static bool IsHidden(HtmlNode n)
    {
        if (n.NodeType != HtmlNodeType.Element) return false;
        if (n.Attributes.Contains("hidden")) return true;
        if (n.GetAttributeValue("aria-hidden", "") == "true") return true;
        var role = n.GetAttributeValue("role", "");
        if (role is "navigation" or "banner" or "contentinfo" or "complementary" or "search") return true;
        var style = n.GetAttributeValue("style", "");
        return style.Length > 0 && Regex.IsMatch(style, @"display\s*:\s*none|visibility\s*:\s*hidden", RegexOptions.IgnoreCase);
    }

    private static void Walk(HtmlNode node, StringBuilder sb, bool inPre, HtmlNode root)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child.NodeType)
            {
                case HtmlNodeType.Text:
                    var raw = HtmlEntity.DeEntitize(((HtmlTextNode)child).Text);
                    if (inPre) sb.Append(raw);
                    else
                    {
                        var collapsed = Regex.Replace(raw, @"[\s ]+", " ");
                        if (collapsed == " " && (sb.Length == 0 || sb[^1] is '\n' or ' ')) break;
                        if (sb.Length == 0 || sb[^1] == '\n') collapsed = collapsed.TrimStart();
                        sb.Append(collapsed);
                    }
                    break;
                case HtmlNodeType.Element:
                    var name = child.Name;
                    if (Junk.Contains(name) || IsHidden(child)) break;
                    if (name.Equals("header", StringComparison.OrdinalIgnoreCase) && !HasAncestor(child, root, "article", "main")) break;
                    if (name.Equals("br", StringComparison.OrdinalIgnoreCase)) { sb.Append('\n'); break; }
                    var block = Blocks.Contains(name);
                    var heading = name.Length == 2 && name[0] is 'h' or 'H' && char.IsDigit(name[1]);
                    if (block) EnsureBreak(sb, heading || name.Equals("p", StringComparison.OrdinalIgnoreCase) ? 2 : 1);
                    if (name.Equals("li", StringComparison.OrdinalIgnoreCase)) sb.Append("• ");
                    Walk(child, sb, inPre || name.Equals("pre", StringComparison.OrdinalIgnoreCase), root);
                    if (block) EnsureBreak(sb, heading || name.Equals("p", StringComparison.OrdinalIgnoreCase) ? 2 : 1);
                    else if (name.Equals("td", StringComparison.OrdinalIgnoreCase) || name.Equals("th", StringComparison.OrdinalIgnoreCase)) sb.Append(" \t");
                    break;
            }
        }
    }

    private static bool HasAncestor(HtmlNode node, HtmlNode root, params string[] names)
    {
        for (var p = node.ParentNode; p is not null; p = p.ParentNode)
        {
            if (names.Contains(p.Name, StringComparer.OrdinalIgnoreCase)) return true;
            if (p == root) return names.Contains(root.Name, StringComparer.OrdinalIgnoreCase);
        }
        return false;
    }

    private static void EnsureBreak(StringBuilder sb, int newlines)
    {
        if (sb.Length == 0) return;
        var have = 0;
        for (var i = sb.Length - 1; i >= 0 && sb[i] == '\n'; i--) have++;
        // trim a trailing space before the break
        while (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
        for (var i = have; i < newlines; i++) sb.Append('\n');
    }

    private static string Normalize(string s)
    {
        var lines = s.Replace("\r", "").Split('\n').Select(l => l.TrimEnd().TrimStart(' ', '\t')).ToList();
        var sb = new StringBuilder();
        var blank = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0) { blank++; continue; }
            if (sb.Length > 0) sb.Append('\n').Append(blank > 0 ? "\n" : "");
            blank = 0;
            sb.Append(line);
        }
        return sb.ToString().Trim();
    }

    private static Uri? Resolve(Uri baseUri, string? href)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;
        href = HtmlEntity.DeEntitize(href.Trim());
        if (href.StartsWith("//")) href = baseUri.Scheme + ":" + href;
        return Uri.TryCreate(baseUri, href, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps) ? u : null;
    }

    private static Uri BaseOf(HtmlDocument doc, Uri pageUrl)
    {
        var href = doc.DocumentNode.SelectSingleNode("//base[@href]")?.GetAttributeValue("href", "");
        return Resolve(pageUrl, href) ?? pageUrl;
    }

    public static IReadOnlyList<PageLink> ExtractLinks(string html, Uri pageUrl)
    {
        var doc = Load(html);
        var baseUri = BaseOf(doc, pageUrl);
        var list = new List<PageLink>();
        var anchors = doc.DocumentNode.SelectNodes("//a[@href]");
        if (anchors is null) return list;
        foreach (var a in anchors)
        {
            var href = a.GetAttributeValue("href", "");
            if (href.StartsWith('#')) continue;
            if (Resolve(baseUri, href) is not { } url) continue;
            var text = Regex.Replace(HtmlEntity.DeEntitize(a.InnerText), @"\s+", " ").Trim();
            if (text.Length == 0) text = a.GetAttributeValue("aria-label", a.GetAttributeValue("title", ""));
            list.Add(new PageLink(url, text));
        }
        return list;
    }

    public static IReadOnlyList<Uri> ExtractImageUrls(string html, Uri pageUrl)
    {
        var doc = Load(html);
        var baseUri = BaseOf(doc, pageUrl);
        var result = new List<Uri>();
        var seen = new HashSet<string>();
        void Add(Uri? u) { if (u is not null && seen.Add(u.AbsoluteUri)) result.Add(u); }

        var imgs = doc.DocumentNode.SelectNodes("//img");
        if (imgs is not null)
        {
            foreach (var img in imgs)
            {
                if (IsTinyImage(img)) continue;
                var src = img.GetAttributeValue("srcset", "") is { Length: > 0 } ss ? BestFromSrcSet(ss)
                    : img.GetAttributeValue("data-srcset", "") is { Length: > 0 } ds ? BestFromSrcSet(ds) : null;
                src ??= First(img, "src", "data-src", "data-original", "data-lazy-src", "data-lazy");
                Add(Resolve(baseUri, src));
            }
        }
        var sources = doc.DocumentNode.SelectNodes("//picture/source[@srcset]");
        if (sources is not null && imgs is null)
            foreach (var s in sources) Add(Resolve(baseUri, BestFromSrcSet(s.GetAttributeValue("srcset", ""))));

        if (result.Count == 0)
        {
            foreach (var prop in new[] { "og:image", "twitter:image" })
            {
                var content = doc.DocumentNode.SelectSingleNode($"//meta[@property='{prop}' or @name='{prop}']")?.GetAttributeValue("content", "");
                Add(Resolve(baseUri, content));
            }
        }
        return result;
    }

    private static string? First(HtmlNode n, params string[] attrs)
    {
        foreach (var a in attrs)
        {
            var v = n.GetAttributeValue(a, "");
            if (!string.IsNullOrWhiteSpace(v) && !v.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return v;
        }
        return null;
    }

    private static bool IsTinyImage(HtmlNode img)
    {
        static bool Small(string v) => int.TryParse(v.Trim().TrimEnd('p', 'x'), out var n) && n <= 2;
        var w = img.GetAttributeValue("width", ""); var h = img.GetAttributeValue("height", "");
        return (w.Length > 0 && Small(w)) || (h.Length > 0 && Small(h));
    }

    private static string? BestFromSrcSet(string srcset)
    {
        string? best = null;
        double bestScore = -1;
        foreach (var part in srcset.Split(','))
        {
            var bits = part.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (bits.Length == 0 || bits[0].StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            double score = 1;
            if (bits.Length > 1 && double.TryParse(bits[1].TrimEnd('w', 'x'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) score = d;
            if (score > bestScore) { bestScore = score; best = bits[0]; }
        }
        return best;
    }
}
