using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Flowmate.Core;

namespace Flowmate.Actions.Content.Utilities;

/// <summary>Follows short links to their final address (up to 10 redirects; HEAD first, GET if the site refuses HEAD).</summary>
public static class LinkExpander
{
    private static readonly Lazy<HttpClient> Shared = new(() =>
    {
        var c = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
        }) { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Flowmate/1.0");
        return c;
    });

    /// <summary>An HttpClient that never follows redirects by itself.</summary>
    public static HttpClient NoRedirectClient => Shared.Value;

    public static async Task<Uri> ExpandAsync(HttpClient client, Uri start, CancellationToken ct = default, int maxRedirects = 10)
    {
        var current = start;
        for (var hop = 0; hop <= maxRedirects; hop++)
        {
            ct.ThrowIfCancellationRequested();
            var next = await NextAsync(client, current, ct).ConfigureAwait(false);
            if (next is null) return current;
            current = next;
        }
        throw new ActionException("That link redirects too many times (more than 10), so Flowmate stopped following it.");
    }

    private static async Task<Uri?> NextAsync(HttpClient client, Uri url, CancellationToken ct)
    {
        try
        {
            using (var head = new HttpRequestMessage(HttpMethod.Head, url))
            using (var response = await client.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                if (RedirectTarget(response, url) is { } target) return target;
                if (response.IsSuccessStatusCode && !IsHtml(response)) return null;
                // HEAD says "OK" for a web page: it might still hold a meta refresh, so look with GET below.
            }
            using var get = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await client.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (RedirectTarget(resp, url) is { } t2) return t2;
            if (resp.IsSuccessStatusCode && IsHtml(resp))
            {
                var html = await ReadStartAsync(resp, ct).ConfigureAwait(false);
                var refresh = HttpHelpers.FindMetaRefresh(html, url);
                if (refresh is not null && refresh != url && (refresh.Scheme == Uri.UriSchemeHttp || refresh.Scheme == Uri.UriSchemeHttps)) return refresh;
            }
            return null;
        }
        catch (HttpRequestException ex) { throw HttpHelpers.Unreachable(url, ex); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw HttpHelpers.TimedOut(url); }
    }

    private static bool IsHtml(HttpResponseMessage r) => r.Content.Headers.ContentType?.MediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true;

    private static async Task<string> ReadStartAsync(HttpResponseMessage r, CancellationToken ct)
    {
        await using var s = await r.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buf = new byte[16384];
        var total = 0;
        int n;
        while (total < buf.Length && (n = await s.ReadAsync(buf.AsMemory(total), ct).ConfigureAwait(false)) > 0) total += n;
        return System.Text.Encoding.UTF8.GetString(buf, 0, total);
    }

    private static Uri? RedirectTarget(HttpResponseMessage response, Uri from)
    {
        var code = (int)response.StatusCode;
        if (code is not (301 or 302 or 303 or 307 or 308)) return null;
        var location = response.Headers.Location;
        if (location is null) return null;
        var target = location.IsAbsoluteUri ? location : new Uri(from, location);
        return target.Scheme is "http" or "https" ? target : null;
    }
}

/// <summary>Checks whether a website answers.</summary>
public static class SiteCheck
{
    public sealed record Result(bool IsUp, int? Status, string Message, TimeSpan Elapsed);

    public static async Task<Result> CheckAsync(HttpClient client, Uri url, TimeSpan timeout, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            using var head = new HttpRequestMessage(HttpMethod.Head, url);
            using var r1 = await client.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            var status = (int)r1.StatusCode;
            if (status is 405 or 501 or 400 or 403 or 404 or 406)
            {
                using var get = new HttpRequestMessage(HttpMethod.Get, url);
                using var r2 = await client.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                status = (int)r2.StatusCode;
            }
            var up = status < 500;
            return new Result(up, status, up ? $"{url.Host} is up (answered with {status})." : $"{url.Host} answered with an error ({status}).", DateTime.UtcNow - started);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new Result(false, null, $"{url.Host} didn't answer within {timeout.TotalSeconds:0} seconds.", DateTime.UtcNow - started);
        }
        catch (HttpRequestException ex)
        {
            return new Result(false, null, $"Couldn't reach {url.Host} ({ex.InnerException?.Message ?? ex.Message}).", DateTime.UtcNow - started);
        }
    }
}

public sealed record FeedItem(string Title, string Link, DateTimeOffset? Published, string Summary, string Author, string Id);

public sealed record Feed(string Title, IReadOnlyList<FeedItem> Items);

/// <summary>Parses RSS 2.0, RSS 1.0 (RDF) and Atom feeds.</summary>
public static class RssParser
{
    public static Feed Parse(string xml, Uri? feedUrl = null)
    {
        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, CheckCharacters = false };
            using var reader = XmlReader.Create(new StringReader(xml.TrimStart('﻿', ' ', '\r', '\n', '\t')), settings);
            doc = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            throw new ActionException("That address doesn't hold an RSS or Atom feed. Look for the feed link on the site (often ending in /feed or /rss).");
        }
        var root = doc.Root ?? throw new ActionException("That feed is empty.");
        var name = root.Name.LocalName.ToLowerInvariant();
        var items = new List<FeedItem>();
        var title = "";

        if (name == "feed")
        {
            title = Text(root.Elements().FirstOrDefault(e => e.Name.LocalName == "title"));
            foreach (var e in root.Elements().Where(e => e.Name.LocalName == "entry"))
            {
                var link = e.Elements().Where(l => l.Name.LocalName == "link")
                    .OrderBy(l => (string?)l.Attribute("rel") is null or "alternate" ? 0 : 1)
                    .Select(l => (string?)l.Attribute("href")).FirstOrDefault(h => !string.IsNullOrWhiteSpace(h)) ?? "";
                var summary = Child(e, "summary") is { Length: > 0 } s ? s : Child(e, "content");
                var author = e.Elements().FirstOrDefault(a => a.Name.LocalName == "author") is { } au ? Child(au, "name") : "";
                items.Add(new FeedItem(Clean(Child(e, "title")), Absolute(link, feedUrl), ParseDate(Child(e, "published") is { Length: > 0 } p ? p : Child(e, "updated")),
                    Summarise(summary), author, Child(e, "id")));
            }
        }
        else if (name is "rss" or "rdf")
        {
            var channel = name == "rss" ? root.Elements().FirstOrDefault(e => e.Name.LocalName == "channel") : root;
            if (channel is null) throw new ActionException("That feed has no channel. It may not be a valid RSS feed.");
            var titleHost = name == "rss" ? channel : root.Elements().FirstOrDefault(e => e.Name.LocalName == "channel") ?? channel;
            title = Text(titleHost.Elements().FirstOrDefault(e => e.Name.LocalName == "title"));
            var entries = name == "rss" ? channel.Elements().Where(e => e.Name.LocalName == "item") : root.Elements().Where(e => e.Name.LocalName == "item");
            foreach (var e in entries)
            {
                var summary = Child(e, "description") is { Length: > 0 } d ? d : Child(e, "encoded");
                var date = Child(e, "pubDate") is { Length: > 0 } pd ? pd : Child(e, "date");
                var author = Child(e, "creator") is { Length: > 0 } c ? c : Child(e, "author");
                var link = Child(e, "link");
                if (link.Length == 0 && Child(e, "guid") is { } g && Uri.IsWellFormedUriString(g, UriKind.Absolute)) link = g;
                items.Add(new FeedItem(Clean(Child(e, "title")), Absolute(link, feedUrl), ParseDate(date), Summarise(summary), Clean(author), Child(e, "guid")));
            }
        }
        else throw new ActionException("That address doesn't hold an RSS or Atom feed. Look for the feed link on the site (often ending in /feed or /rss).");
        return new Feed(Clean(title), items);
    }

    private static string Text(XElement? e) => e?.Value.Trim() ?? "";
    private static string Child(XElement e, string local) =>
        e.Elements().FirstOrDefault(x => x.Name.LocalName.Equals(local, StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "";
    private static string Clean(string s) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(s, "<[^>]+>", "")), @"\s+", " ").Trim();

    private static string Absolute(string link, Uri? baseUri)
    {
        link = link.Trim();
        if (baseUri is not null && link.Length > 0 && Uri.TryCreate(baseUri, link, out var u)) return u.AbsoluteUri;
        return link;
    }

    private static string Summarise(string html)
    {
        var text = ActionHelpers.StripHtml(html);
        return text.Length > 1000 ? text[..1000].TrimEnd() + "…" : text;
    }

    private static readonly Dictionary<string, string> Zones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UT"] = "+0000", ["UTC"] = "+0000", ["GMT"] = "+0000", ["Z"] = "+0000", ["EST"] = "-0500", ["EDT"] = "-0400", ["CST"] = "-0600",
        ["CDT"] = "-0500", ["MST"] = "-0700", ["MDT"] = "-0600", ["PST"] = "-0800", ["PDT"] = "-0700", ["BST"] = "+0100", ["CET"] = "+0100", ["CEST"] = "+0200",
    };

    public static DateTimeOffset? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        if (DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)) return d;
        var m = Regex.Match(t, @"^(.*\S)\s+([A-Za-z]{1,4})$");
        if (m.Success && Zones.TryGetValue(m.Groups[2].Value, out var offset)
            && DateTimeOffset.TryParse(m.Groups[1].Value + " " + offset, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out d)) return d;
        return null;
    }
}
