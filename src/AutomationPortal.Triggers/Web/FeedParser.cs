using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HtmlAgilityPack;

namespace AutomationPortal.Triggers;

public sealed record FeedItem(string Id, string Title, string? Link, string? Summary, DateTimeOffset? Published);

public sealed record Feed(string Title, IReadOnlyList<FeedItem> Items);

/// <summary>Reads RSS 2.0, RSS 1.0 (RDF) and Atom feeds, and turns web pages into plain text for change detection.</summary>
public static class FeedParser
{
    public static Feed Parse(string xml)
    {
        var doc = XDocument.Parse(xml.TrimStart('﻿', ' ', '\r', '\n', '\t'));
        var root = doc.Root ?? throw new FormatException("This isn't a feed.");
        string Name(XElement e) => e.Name.LocalName.ToLowerInvariant();
        string? Child(XElement e, string name) => e.Elements().FirstOrDefault(c => Name(c) == name)?.Value.Trim();

        var items = new List<FeedItem>();
        string title;
        if (Name(root) == "feed")
        {
            title = Child(root, "title") ?? "Feed";
            foreach (var e in root.Elements().Where(e => Name(e) == "entry"))
            {
                var link = e.Elements().Where(l => Name(l) == "link").OrderBy(l => (string?)l.Attribute("rel") is null or "alternate" ? 0 : 1)
                    .Select(l => (string?)l.Attribute("href")).FirstOrDefault(h => !string.IsNullOrWhiteSpace(h));
                var summary = Child(e, "summary") ?? Child(e, "content");
                items.Add(Make(Child(e, "id"), Child(e, "title"), link, summary, Child(e, "published") ?? Child(e, "updated")));
            }
        }
        else
        {
            var channel = root.Elements().FirstOrDefault(c => Name(c) == "channel") ?? root;
            title = Child(channel, "title") ?? "Feed";
            var source = root.Elements().Where(c => Name(c) == "item").Concat(channel.Elements().Where(c => Name(c) == "item"));
            foreach (var e in source)
                items.Add(Make(Child(e, "guid"), Child(e, "title"), Child(e, "link"), Child(e, "description") ?? Child(e, "encoded"), Child(e, "pubdate") ?? Child(e, "date")));
        }
        return new Feed(title, items);
    }

    private static FeedItem Make(string? id, string? title, string? link, string? summary, string? date)
    {
        DateTimeOffset? published = DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;
        var cleanTitle = HtmlToText(title ?? "");
        var key = !string.IsNullOrWhiteSpace(id) ? id! : !string.IsNullOrWhiteSpace(link) ? link! : cleanTitle + "|" + date;
        return new FeedItem(key, cleanTitle.Length > 0 ? cleanTitle : "(no title)", link, summary is null ? null : HtmlToText(summary), published);
    }

    /// <summary>Which items are new, oldest first. With no history (first look at a feed) nothing is new: that look only records the current items.</summary>
    public static IReadOnlyList<FeedItem> NewItems(IReadOnlyList<FeedItem> items, IReadOnlyCollection<string>? seen)
    {
        if (seen is null) return Array.Empty<FeedItem>();
        var set = seen as ISet<string> ?? new HashSet<string>(seen);
        return items.Where(i => !set.Contains(i.Id)).Reverse().ToList();
    }

    public static string HtmlToText(string html)
    {
        if (html.IndexOf('<') < 0 && html.IndexOf('&') < 0) return html.Trim();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return TextOf(doc);
    }

    private static string TextOf(HtmlDocument doc)
    {
        // Join text pieces with a space so "<h1>Title</h1><p>Body</p>" doesn't become "TitleBody".
        var pieces = doc.DocumentNode.Descendants().Where(n => n.NodeType == HtmlNodeType.Text).Select(n => HtmlEntity.DeEntitize(n.InnerText));
        return Regex.Replace(string.Join(" ", pieces), @"\s+", " ").Trim();
    }

    /// <summary>The readable text of a web page (no scripts, styles or markup), whitespace squeezed.</summary>
    public static string PageText(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        foreach (var n in doc.DocumentNode.Descendants().Where(n => n.Name is "script" or "style" or "noscript" or "svg" or "iframe" or "#comment").ToList())
            n.Remove();
        return TextOf(doc);
    }
}
