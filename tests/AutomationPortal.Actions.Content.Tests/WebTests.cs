using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AutomationPortal.Actions.Content.Actions;
using AutomationPortal.Actions.Content.Utilities;
using AutomationPortal.Core;
using AutomationPortal.Core.Testing;
using static AutomationPortal.Actions.Content.Tests.Kit;

namespace AutomationPortal.Actions.Content.Tests;

public class LinkCleanerTests
{
    [Theory]
    [InlineData("https://example.com/page?utm_source=a&id=5&fbclid=xyz#top", "https://example.com/page?id=5#top")]
    [InlineData("https://example.com/?utm_source=x", "https://example.com/")]
    [InlineData("https://example.com/?a=1&gclid=x&msclkid=y&utm_campaign=z&dclid=1&igshid=2&mc_eid=3&ref_src=4", "https://example.com/?a=1")]
    [InlineData("https://www.youtube.com/watch?v=abc&si=tracker&t=30", "https://www.youtube.com/watch?v=abc&t=30")]
    [InlineData("https://youtu.be/abc?si=tracker", "https://youtu.be/abc")]
    [InlineData("https://open.spotify.com/track/123?si=abc&utm_source=copy-link", "https://open.spotify.com/track/123")]
    [InlineData("https://example.com/?si=keepme&q=1", "https://example.com/?si=keepme&q=1")]
    [InlineData("https://twitter.com/user/status/1?s=20&t=abc", "https://twitter.com/user/status/1")]
    [InlineData("https://example.com/?s=search&t=term", "https://example.com/?s=search&t=term")]
    [InlineData("https://www.amazon.com/dp/B000/ref=sr_1_1?tag=x&th=1&keywords=a%20b&pf_rd_r=zzz", "https://www.amazon.com/dp/B000?th=1&keywords=a%20b")]
    [InlineData("https://example.com/search?q=a%20b&page=2", "https://example.com/search?q=a%20b&page=2")]
    [InlineData("https://example.com/path", "https://example.com/path")]
    [InlineData("https://example.com/p?UTM_Source=x&id=1", "https://example.com/p?id=1")]
    [InlineData("https://example.com/p?id=1&id=2&utm_medium=m", "https://example.com/p?id=1&id=2")]
    [InlineData("https://example.com/p?utm_source=a#section", "https://example.com/p#section")]
    [InlineData("mailto:a@b.com?subject=hi&utm_source=x", "mailto:a@b.com?subject=hi&utm_source=x")]
    public void Cleans(string input, string expected) => Assert.Equal(expected, LinkCleaner.Clean(input));

    [Fact]
    public void Non_links_are_returned_unchanged_and_uri_overload_works()
    {
        Assert.Equal("hello?utm_source=x", LinkCleaner.Clean("hello?utm_source=x"));
        Assert.Equal("https://example.com/?a=1", LinkCleaner.Clean(new Uri("https://example.com/?a=1&utm_source=x")).AbsoluteUri);
    }

    [Fact]
    public async Task Action_cleans_links_found_in_text_and_url_items()
    {
        var r = await Run(new RemoveTrackingFromLinkAction(), ContentValue.Text("have a look at https://example.com/p?utm_source=x&id=1 today"));
        Assert.Equal("https://example.com/p?id=1", r.AsText());
        Assert.IsType<UrlItem>(r[0]);
        var many = await Run(new RemoveTrackingFromLinkAction(), ContentValue.Concat(ContentValue.Url("https://a.com/?fbclid=1"), ContentValue.Url("https://b.com/?x=1")));
        Assert.Equal(new[] { "https://a.com/", "https://b.com/?x=1" }, Lines(many));
        await Fails(Run(new RemoveTrackingFromLinkAction(), ContentValue.Text("no link here")));
    }
}

public class UrlAndSearchTests
{
    private const string Sample = "https://www.example.com:8080/a/b/report%20final.pdf?id=42&name=A%20B&flag#sec";

    [Theory]
    [InlineData("domain", "example.com")]
    [InlineData("host", "www.example.com")]
    [InlineData("scheme", "https")]
    [InlineData("path", "/a/b/report final.pdf")]
    [InlineData("fileName", "report final.pdf")]
    [InlineData("extension", "pdf")]
    [InlineData("query", "id=42&name=A%20B&flag")]
    [InlineData("fragment", "sec")]
    [InlineData("port", "8080")]
    public async Task Url_parts(string part, string expected) =>
        Assert.Equal(expected, (await Run(new GetPartOfUrlAction(), ContentValue.Text(Sample), ("part", C(part)))).AsText());

    [Fact]
    public async Task Url_query_parameters()
    {
        async Task<string> P(string name) => (await Run(new GetPartOfUrlAction(), ContentValue.Text(Sample), ("part", C("queryParam")), ("name", name))).AsText();
        Assert.Equal("42", await P("id"));
        Assert.Equal("A B", await P("NAME"));
        Assert.Equal("", await P("flag"));
        Assert.Equal("", await P("missing"));
        await Fails(Run(new GetPartOfUrlAction(), ContentValue.Text(Sample), ("part", C("queryParam"))));
        await Fails(Run(new GetPartOfUrlAction(), ContentValue.Text("no link")));
    }

    [Fact]
    public async Task Url_part_of_a_bare_domain_and_a_list()
    {
        Assert.Equal("example.org", (await Run(new GetPartOfUrlAction(), ContentValue.Text("example.org/x"), ("part", C("host")))).AsText());
        var many = await Run(new GetPartOfUrlAction(), ContentValue.Concat(ContentValue.Url("https://a.com/x"), ContentValue.Url("https://www.b.com/y")), ("part", C("domain")));
        Assert.Equal(new[] { "a.com", "b.com" }, Lines(many));
    }

    [Theory]
    [InlineData("google", "https://www.google.com/search?q=hello%20world%20%26%20more")]
    [InlineData("bing", "https://www.bing.com/search?q=hello%20world%20%26%20more")]
    [InlineData("duckduckgo", "https://duckduckgo.com/?q=hello%20world%20%26%20more")]
    [InlineData("youtube", "https://www.youtube.com/results?search_query=hello%20world%20%26%20more")]
    [InlineData("amazon", "https://www.amazon.com/s?k=hello%20world%20%26%20more")]
    [InlineData("maps", "https://www.google.com/maps/search/?api=1&query=hello%20world%20%26%20more")]
    public void Search_urls(string engine, string expected) =>
        Assert.Equal(expected, SearchTheWebAction.BuildUrl(engine, "hello world & more").AbsoluteUri);

    [Fact]
    public void Wikipedia_search_uses_a_language_subdomain()
    {
        var url = SearchTheWebAction.BuildUrl("wikipedia", "cats").AbsoluteUri;
        Assert.Matches(@"^https://[a-z]{2}\.wikipedia\.org/w/index\.php\?search=cats$", url);
    }

    [Fact]
    public async Task Opening_and_searching_launch_the_browser_only_when_asked()
    {
        var opened = new List<string>();
        var original = OpenUrlsAction.Launcher;
        OpenUrlsAction.Launcher = u => opened.Add(u.AbsoluteUri);
        try
        {
            var r = await Run(new SearchTheWebAction(), null, ("engine", C("duckduckgo")), ("query", "automationportal"));
            Assert.Equal(new[] { "https://duckduckgo.com/?q=automationportal" }, opened);
            Assert.Equal("https://duckduckgo.com/?q=automationportal", r.AsText());

            opened.Clear();
            await Run(new SearchTheWebAction(), null, ("query", "quiet"), ("open", false));
            Assert.Empty(opened);

            await Run(new OpenUrlsAction(), ContentValue.Text("https://one.test and https://two.test"));
            Assert.Equal(new[] { "https://one.test/", "https://two.test/" }, opened);

            opened.Clear();
            var viaItems = await Run(new OpenUrlsAction(), ContentValue.Concat(ContentValue.Url("https://a.test"), ContentValue.Url("mailto:me@x.test")));
            Assert.Equal(2, opened.Count);
            Assert.Equal(2, viaItems.Count);

            await Fails(Run(new OpenUrlsAction(), ContentValue.Text("nothing to open")));
            await Fails(Run(new SearchTheWebAction(), null, ("query", "   ")));
        }
        finally { OpenUrlsAction.Launcher = original; }
    }

    [Fact]
    public async Task Opening_refuses_unsafe_schemes()
    {
        var original = OpenUrlsAction.Launcher;
        var opened = new List<string>();
        OpenUrlsAction.Launcher = u => opened.Add(u.AbsoluteUri);
        try
        {
            var ex = await Fails(Run(new OpenUrlsAction(), ContentValue.Url(new Uri("file:///C:/Windows/System32/calc.exe"))));
            Assert.Contains("file:", ex.Message);
            Assert.Empty(opened);
        }
        finally { OpenUrlsAction.Launcher = original; }
    }
}

public class HtmlTests
{
    private static readonly string Article = ArticleTemplate.Replace("@@PRE@@", "  keep\n   this   layout");

    private const string ArticleTemplate = """
        <html><head><title>My &amp; Page</title><base href="https://example.com/dir/"></head>
        <body>
        <nav><a href="/home">Home</a> <a href="#top">Top</a></nav>
        <header>Site header</header>
        <div class="ad" style="display:none">Hidden advert</div>
        <article>
          <h1>Big Headline</h1>
          <p>First paragraph with <b>bold</b> text and <a href="story">a link</a>.</p>
          <p>Second paragraph that is long enough to make this article the main thing on the page, so the extractor prefers it over the rest of the body.</p>
          <ul><li>One</li><li>Two</li></ul>
          <pre>@@PRE@@</pre>
          <script>var secret = 1;</script><style>.a { color: red }</style>
          <a href="javascript:void(0)">js</a> <a href="mailto:x@y.z">mail</a> <a href="//cdn.test/a">cdn</a> <a href="https://other.test/x?y=1">other</a>
        </article>
        <footer>Copyright notice</footer>
        </body></html>
        """;

    [Fact]
    public void Extracts_the_readable_article_text()
    {
        var (title, text) = HtmlExtractor.ExtractText(Article);
        Assert.Equal("My & Page", title);
        Assert.Contains("Big Headline", text);
        Assert.Contains("First paragraph with bold text and a link.", text);
        Assert.Contains("Second paragraph", text);
        Assert.Contains("• One", text);
        Assert.Contains("• Two", text);
        Assert.Contains("keep\nthis   layout", text);
        foreach (var junk in new[] { "Home", "Site header", "Copyright", "secret", "color: red", "Hidden advert" })
            Assert.DoesNotContain(junk, text);
        Assert.Contains("Big Headline\n\nFirst paragraph", text);
        Assert.False(text.StartsWith('\n') || text.EndsWith('\n'));
        Assert.DoesNotContain("\n\n\n", text);
    }

    [Fact]
    public void Falls_back_to_main_then_body_and_decodes_entities()
    {
        var main = HtmlExtractor.ExtractText("<body><div>Side</div><main><p>Core text that is the point &amp; more, long enough to count as the main block of the page content area.</p></main></body>");
        Assert.StartsWith("Core text that is the point & more", main.Text);
        Assert.DoesNotContain("Side", main.Text);
        var body = HtmlExtractor.ExtractText("<p>One</p><p>Two<br>three</p>");
        Assert.Equal("One\n\nTwo\nthree", body.Text);
        Assert.Null(body.Title);
        Assert.Equal("Only heading", HtmlExtractor.ExtractText("<h1>Only heading</h1>").Title);
        Assert.Equal("", HtmlExtractor.ExtractText("").Text);
    }

    [Fact]
    public void Aspnet_style_pages_wrapped_in_a_form_still_give_text()
    {
        var (_, text) = HtmlExtractor.ExtractText("<body><form id=\"f\"><div><p>Inside a form</p></div></form></body>");
        Assert.Contains("Inside a form", text);
    }

    [Fact]
    public void Extracts_absolute_links_honouring_the_base_tag()
    {
        var links = HtmlExtractor.ExtractLinks(Article, new Uri("https://page.test/x/y"));
        var urls = links.Select(l => l.Url.AbsoluteUri).ToList();
        Assert.Contains("https://example.com/home", urls);
        Assert.Contains("https://example.com/dir/story", urls);
        Assert.Contains("https://cdn.test/a", urls);
        Assert.Contains("https://other.test/x?y=1", urls);
        Assert.Equal(4, urls.Count);
        Assert.Equal("a link", links.First(l => l.Url.AbsoluteUri.EndsWith("story")).Text);
    }

    [Fact]
    public void Extracts_image_urls()
    {
        const string html = """
            <html><body>
            <img src="a.png"><img src="/pixel.gif" width="1" height="1"><img data-src="lazy.png" src="data:image/gif;base64,R0lGOD">
            <img srcset="s.png 320w, l.png 1280w, m.png 640w" src="fallback.png">
            <img src="data:image/gif;base64,AAAA"><img src="a.png"><img src="https://cdn.test/abs.jpg">
            </body></html>
            """;
        var urls = HtmlExtractor.ExtractImageUrls(html, new Uri("https://example.com/dir/page")).Select(u => u.AbsoluteUri).ToList();
        Assert.Equal(new[] { "https://example.com/dir/a.png", "https://example.com/dir/lazy.png", "https://example.com/dir/l.png", "https://cdn.test/abs.jpg" }, urls);

        var og = HtmlExtractor.ExtractImageUrls("<head><meta property=\"og:image\" content=\"/social.png\"></head>", new Uri("https://example.com/"));
        Assert.Equal("https://example.com/social.png", og.Single().AbsoluteUri);
    }

    [Fact]
    public void Link_scope_and_filters()
    {
        const string html = "<a href='/a'>A</a><a href='https://www.example.com/b'>Bee</a><a href='https://other.test/c'>C</a><a href='/a'>again</a>";
        var page = new Uri("https://example.com/");
        Assert.Equal(new[] { "https://example.com/a", "https://www.example.com/b", "https://other.test/c" }, GetLinksFromWebPageAction.Extract(html, page, "all", "", true).Select(u => u.AbsoluteUri));
        Assert.Equal(4, GetLinksFromWebPageAction.Extract(html, page, "all", "", false).Count);
        Assert.Equal(new[] { "https://example.com/a", "https://www.example.com/b" }, GetLinksFromWebPageAction.Extract(html, page, "sameSite", "", true).Select(u => u.AbsoluteUri));
        Assert.Equal(new[] { "https://other.test/c" }, GetLinksFromWebPageAction.Extract(html, page, "otherSites", "", true).Select(u => u.AbsoluteUri));
        Assert.Equal(new[] { "https://www.example.com/b" }, GetLinksFromWebPageAction.Extract(html, page, "all", "bee", true).Select(u => u.AbsoluteUri));
    }

    [Fact]
    public void Pasted_html_is_recognised()
    {
        Assert.True(HtmlExtractor.LooksLikeHtml("<html><body>x</body></html>"));
        Assert.True(HtmlExtractor.LooksLikeHtml("  <p>hi</p>"));
        Assert.False(HtmlExtractor.LooksLikeHtml("https://example.com"));
        Assert.False(HtmlExtractor.LooksLikeHtml("1 < 2 and 3 > 2"));
    }
}

public class WebPageActionTests
{
    private static TestHost HostFor(Func<HttpRequestMessage, HttpResponseMessage> respond, out StubHandler handler)
    {
        handler = new StubHandler(respond);
        var host = new TestHost();
        host.Services.Http = handler.Client();
        return host;
    }

    private const string Page = "<html><head><title>Hello Page</title></head><body><nav>menu</nav><article><h1>Heading</h1><p>Body text that goes on for long enough to be chosen as the article of this page, definitely more than two hundred characters in total so that the article rule applies here. More filler goes here to be certain about the length.</p><a href=\"/next\">Next</a><img src=\"/i.png\"></article></body></html>";

    [Fact]
    public async Task Page_text_title_and_both()
    {
        using var host = HostFor(_ => Resp(HttpStatusCode.OK, Page, "text/html"), out var handler);
        var text = await Run(host, new GetWebPageTextAction(), null, ("url", "https://example.test/post"));
        Assert.StartsWith("Heading", text.AsText());
        Assert.DoesNotContain("menu", text.AsText());
        Assert.Equal("Hello Page", (await Run(host, new GetWebPageTextAction(), null, ("url", "https://example.test/post"), ("what", C("title")))).AsText());
        var both = (await Run(host, new GetWebPageTextAction(), null, ("url", "https://example.test/post"), ("what", C("both")))).AsText();
        Assert.StartsWith("Hello Page\n\nHeading", both);
        Assert.Equal("https://example.test/post", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Contains("text/html", handler.Requests[0].Headers.Accept.ToString());
    }

    [Fact]
    public async Task Page_text_accepts_html_from_an_earlier_action()
    {
        using var host = HostFor(_ => throw new InvalidOperationException("no request expected"), out var handler);
        var r = await Run(host, new GetWebPageTextAction(), ContentValue.Text(Page));
        Assert.StartsWith("Heading", r.AsText());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Page_text_errors_are_friendly()
    {
        using var host = HostFor(_ => Resp(HttpStatusCode.NotFound, "nope", "text/html"), out _);
        var ex = await Fails(Run(host, new GetWebPageTextAction(), null, ("url", "https://example.test/gone")));
        Assert.Contains("404", ex.Message);
        Assert.Contains("example.test", ex.Message);
        var none = await Fails(Run(host, new GetWebPageTextAction(), null, ("url", "")));
        Assert.Contains("web address", none.Message);
        var notALink = await Fails(Run(host, new GetWebPageTextAction(), null, ("url", "what is this")));
        Assert.Contains("doesn't look like a web address", notALink.Message);
    }

    [Fact]
    public async Task Page_links_action()
    {
        using var host = HostFor(_ => Resp(HttpStatusCode.OK, "<a href='/x'>X</a><a href='https://o.test/y'>Y</a>", "text/html"), out _);
        var r = await Run(host, new GetLinksFromWebPageAction(), null, ("url", "https://example.test/p"));
        Assert.Equal(new[] { "https://example.test/x", "https://o.test/y" }, Lines(r));
        Assert.All(r, i => Assert.IsType<UrlItem>(i));
        var other = await Run(host, new GetLinksFromWebPageAction(), null, ("url", "https://example.test/p"), ("scope", C("otherSites")));
        Assert.Equal(new[] { "https://o.test/y" }, Lines(other));
        var pasted = await Run(host, new GetLinksFromWebPageAction(), ContentValue.Text("<p><a href='/relative'>r</a><a href='https://abs.test/a'>a</a></p>"));
        Assert.Equal(new[] { "https://abs.test/a" }, Lines(pasted));
    }

    [Fact]
    public async Task Page_images_download_into_the_run_folder()
    {
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        using var host = HostFor(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("page")) return Resp(HttpStatusCode.OK, "<img src='/one.png'><img src='/missing.png'><img src='/two.png'><img src='/notimage.png'>", "text/html");
            if (path.EndsWith("missing.png")) return Resp(HttpStatusCode.NotFound, "", "text/plain");
            if (path.EndsWith("notimage.png")) return Resp(HttpStatusCode.OK, "<html>", "text/html");
            var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) };
            r.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return r;
        }, out _);

        var images = await Run(host, new GetImagesFromWebPageAction(), null, ("url", "https://example.test/page"));
        Assert.Equal(2, images.Count);
        Assert.All(images, i => { Assert.IsType<ImageItem>(i); Assert.True(File.Exists(((ImageItem)i).Path)); });
        Assert.Equal(new[] { "one.png", "two.png" }, images.Property("Full Name").AsTexts());

        var limited = await Run(host, new GetImagesFromWebPageAction(), null, ("url", "https://example.test/page"), ("limit", 1));
        Assert.Single(limited);

        var urls = await Run(host, new GetImagesFromWebPageAction(), null, ("url", "https://example.test/page"), ("download", false));
        Assert.Equal(4, urls.Count);
        Assert.All(urls, i => Assert.IsType<UrlItem>(i));
    }

    [Fact]
    public async Task Page_images_say_so_when_there_are_none()
    {
        using var host = HostFor(_ => Resp(HttpStatusCode.OK, "<p>no pictures</p>", "text/html"), out _);
        var ex = await Fails(Run(host, new GetImagesFromWebPageAction(), null, ("url", "https://example.test/page")));
        Assert.Contains("any pictures", ex.Message);
    }
}

public class FetchTests
{
    private static TestHost HostFor(Func<HttpRequestMessage, HttpResponseMessage> respond, out StubHandler handler)
    {
        handler = new StubHandler(respond);
        var host = new TestHost();
        host.Services.Http = handler.Client();
        return host;
    }

    [Fact]
    public async Task Json_becomes_a_dictionary_or_list()
    {
        using var host = HostFor(req => req.RequestUri!.AbsolutePath == "/list"
            ? Json("[{\"id\":1},{\"id\":2}]")
            : Json("{\"name\":\"Sam\",\"tags\":[\"a\",\"b\"],\"n\":{\"x\":1.5}}"), out _);
        var d = await Run(host, new GetContentsOfUrlAction(), null, ("url", "https://example.test/obj"));
        var dict = Assert.IsType<DictionaryItem>(d[0]);
        Assert.Equal("Sam", dict.Get("name").AsText());
        Assert.Equal(1.5, dict.GetPath("n.x").AsNumber());
        var list = await Run(host, new GetContentsOfUrlAction(), null, ("url", "https://example.test/list"));
        Assert.Equal(2, list.Count);
        Assert.All(list, i => Assert.IsType<DictionaryItem>(i));
    }

    [Fact]
    public async Task Text_html_and_plain_text_json_are_handled()
    {
        using var host = HostFor(req => req.RequestUri!.AbsolutePath switch
        {
            "/html" => Resp(HttpStatusCode.OK, "<h1>Hi</h1>", "text/html; charset=utf-8"),
            "/plainjson" => Resp(HttpStatusCode.OK, "{\"a\":1}", "text/plain"),
            "/plain" => Resp(HttpStatusCode.OK, "just [words]", "text/plain"),
            "/xml" => Resp(HttpStatusCode.OK, "<a/>", "application/xml"),
            "/badjson" => Resp(HttpStatusCode.OK, "{not json", "application/json"),
            _ => Resp(HttpStatusCode.OK, "héllo", "text/plain; charset=utf-8"),
        }, out _);
        Assert.Equal("<h1>Hi</h1>", (await Run(host, new GetContentsOfUrlAction(), null, ("url", "https://example.test/html"))).AsText());
        Assert.IsType<DictionaryItem>((await Run(host, new GetContentsOfUrlAction(), null, ("url", "https://example.test/plainjson")))[0]);
        Assert.IsType<TextItem>((await Run(host, new GetContentsOfUrlAction(), null, ("url", "https://example.test/plain")))[0]);
        Assert.Equal("<a/>", (await Run(host, new GetContentsOfUrlAction(), null, ("url", "https://example.test/xml"))).AsText());
        Assert.Equal("{not json", (await Run(host, new GetContentsOfUrlAction(), null, ("url", "https://example.test/badjson"))).AsText());
        Assert.Equal("héllo", (await Run(host, new GetContentsOfUrlAction(), null, ("url", "https://example.test/other"))).AsText());
    }

    [Fact]
    public async Task Binary_responses_are_saved_to_the_run_folder_with_the_right_name()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        using var host = HostFor(req =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            r.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            if (req.RequestUri!.AbsolutePath == "/named") r.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "\"Annual Report.pdf\"" };
            return r;
        }, out _);

        var named = await Run(host, new GetContentsOfUrlAction(), null, ("url", "https://example.test/named"));
        var file = Assert.IsAssignableFrom<PathItem>(named[0]);
        Assert.Equal("Annual Report.pdf", file.FullName);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(file.Path));

        var fromUrl = await Run(host, new GetContentsOfUrlAction(), null, ("url", "https://example.test/files/Invoice%20May.pdf?x=1"));
        Assert.Equal("Invoice May.pdf", ((PathItem)fromUrl[0]).FullName);

        var noName = await Run(host, new GetContentsOfUrlAction(), null, ("url", "https://example.test/"));
        Assert.Equal("download.pdf", ((PathItem)noName[0]).FullName);
    }

    [Fact]
    public async Task Images_come_back_as_image_items()
    {
        using var host = HostFor(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1 }) };
            r.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return r;
        }, out _);
        var r = await Run(host, new GetContentsOfUrlAction(), null, ("url", "https://example.test/photo"));
        Assert.IsType<ImageItem>(r[0]);
    }

    [Theory]
    [InlineData(400, "didn't understand")]
    [InlineData(401, "sign in")]
    [InlineData(403, "aren't allowed")]
    [InlineData(404, "couldn't find that page")]
    [InlineData(405, "kind of request")]
    [InlineData(408, "took too long")]
    [InlineData(410, "removed")]
    [InlineData(429, "too many requests")]
    [InlineData(500, "problem on its side")]
    [InlineData(502, "isn't answering")]
    [InlineData(503, "temporarily unavailable")]
    [InlineData(504, "took too long")]
    [InlineData(418, "error (418")]
    public async Task Errors_are_explained_in_plain_words(int status, string phrase)
    {
        using var host = HostFor(_ => Resp((HttpStatusCode)status, "x"), out _);
        var ex = await Fails(Run(host, new GetContentsOfUrlAction(), null, ("url", "https://example.test/x")));
        Assert.Contains(phrase, ex.Message);
        Assert.Contains("example.test", ex.Message);
        if (status != 418) Assert.Contains(status.ToString(), ex.Message);
    }

    [Fact]
    public async Task Network_failures_are_friendly()
    {
        using var host = HostFor(_ => throw new HttpRequestException("boom"), out _);
        var ex = await Fails(Run(host, new GetContentsOfUrlAction(), null, ("url", "https://example.test/x")));
        Assert.Contains("Couldn't reach example.test", ex.Message);
    }

    [Fact]
    public async Task Post_json_with_headers()
    {
        using var host = HostFor(_ => Json("{\"ok\":true}"), out var handler);
        var r = await Run(host, new GetContentsOfUrlAction(), null,
            ("url", "https://example.test/api"), ("method", C("POST")), ("bodyType", C("json")),
            ("json", ParamValue.Dict(("name", ParamValue.Text("Sam")), ("n", ParamValue.Number(5)), ("tags", ParamValue.List("a", "b")))),
            ("headers", ParamValue.Dict(("X-Api-Key", ParamValue.Text("secret")), ("Accept", ParamValue.Text("application/json")))));
        Assert.True(((DictionaryItem)r[0]).Get("ok").AsBool());
        var req = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("{\"name\":\"Sam\",\"n\":5,\"tags\":[\"a\",\"b\"]}", handler.Bodies[0]);
        Assert.Equal("application/json", req.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("secret", req.Headers.GetValues("X-Api-Key").Single());
        Assert.Equal("application/json", req.Headers.Accept.ToString());
    }

    [Fact]
    public async Task Json_body_can_be_text_that_is_already_json()
    {
        using var host = HostFor(_ => Json("{}"), out var handler);
        await Run(host, new GetContentsOfUrlAction(), null, ("url", "https://example.test/api"), ("method", C("PUT")), ("bodyType", C("json")), ("json", "{\"raw\": 1}"));
        Assert.Equal("{\"raw\": 1}", handler.Bodies[0]);
        Assert.Equal(HttpMethod.Put, handler.Requests[0].Method);
    }

    [Fact]
    public async Task Form_text_and_file_bodies()
    {
        using var host = HostFor(_ => Resp(HttpStatusCode.OK, "ok"), out var handler);
        await Run(host, new GetContentsOfUrlAction(), null, ("url", "https://example.test/f"), ("method", C("POST")), ("bodyType", C("form")),
            ("form", ParamValue.Dict(("a", ParamValue.Text("1")), ("b", ParamValue.Text("x y&z")))));
        Assert.Equal("a=1&b=x+y%26z", handler.Bodies[0]);
        Assert.Equal("application/x-www-form-urlencoded", handler.Requests[0].Content!.Headers.ContentType!.MediaType);

        await Run(host, new GetContentsOfUrlAction(), null, ("url", "https://example.test/t"), ("method", C("POST")), ("bodyType", C("text")), ("bodyText", "hello body"));
        Assert.Equal("hello body", handler.Bodies[1]);
        Assert.Equal("text/plain", handler.Requests[1].Content!.Headers.ContentType!.MediaType);

        Directory.CreateDirectory(host.Root);
        var path = Path.Combine(host.Root, "data.json");
        await File.WriteAllTextAsync(path, "{\"from\":\"file\"}");
        await Run(host, new GetContentsOfUrlAction(), null, ("url", "https://example.test/u"), ("method", C("PUT")), ("bodyType", C("file")), ("bodyFile", ParamValue.File(path)));
        Assert.Equal("{\"from\":\"file\"}", handler.Bodies[2]);
        Assert.Equal("application/json", handler.Requests[2].Content!.Headers.ContentType!.MediaType);

        await Fails(Run(host, new GetContentsOfUrlAction(), null, ("url", "https://example.test/u"), ("method", C("PUT")), ("bodyType", C("file")), ("bodyFile", ParamValue.File(Path.Combine(host.Root, "nope.txt")))));
    }

    [Fact]
    public async Task Head_requests_return_the_headers_and_get_cannot_have_a_body()
    {
        using var host = HostFor(_ =>
        {
            var r = Resp(HttpStatusCode.OK, "");
            r.Headers.Add("X-Thing", "abc");
            return r;
        }, out _);
        var head = (DictionaryItem)(await Run(host, new GetContentsOfUrlAction(), null, ("url", "https://example.test/h"), ("method", C("HEAD"))))[0];
        Assert.Equal("abc", head.Get("X-Thing").AsText());
        Assert.Equal(200, head.Get("Status").AsNumber());
        var ex = await Fails(Run(host, new GetContentsOfUrlAction(), null, ("url", "https://example.test/h"), ("bodyType", C("text")), ("bodyText", "x")));
        Assert.Contains("can't have a body", ex.Message);
    }

    [Fact]
    public async Task Url_can_come_from_the_previous_action()
    {
        using var host = HostFor(_ => Resp(HttpStatusCode.OK, "fine"), out var handler);
        var r = await Run(host, new GetContentsOfUrlAction(), ContentValue.Url("https://example.test/prev"));
        Assert.Equal("fine", r.AsText());
        Assert.Equal("/prev", handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Download_saves_with_good_names_and_never_overwrites()
    {
        var bytes = Encoding.UTF8.GetBytes("file body");
        using var host = HostFor(req =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            r.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return r;
        }, out _);

        var temp = await Run(host, new DownloadFileAction(), null, ("url", "https://example.test/img/pic.png"));
        var tempFile = Assert.IsType<ImageItem>(temp[0]);
        Assert.Equal("pic.png", tempFile.FullName);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(tempFile.Path));

        var folder = Path.Combine(host.Root, "keep");
        var a = await Run(host, new DownloadFileAction(), null, ("url", "https://example.test/img/pic.png"), ("folder", folder));
        var b = await Run(host, new DownloadFileAction(), null, ("url", "https://example.test/img/pic.png"), ("folder", folder));
        Assert.Equal(Path.Combine(folder, "pic.png"), ((PathItem)a[0]).Path);
        Assert.Equal(Path.Combine(folder, "pic (2).png"), ((PathItem)b[0]).Path);

        var renamed = await Run(host, new DownloadFileAction(), null, ("url", "https://example.test/x"), ("folder", folder), ("fileName", "mine.png"));
        Assert.Equal("mine.png", ((PathItem)renamed[0]).FullName);

        var noExt = await Run(host, new DownloadFileAction(), null, ("url", "https://example.test/download?id=5"));
        Assert.Equal("download.png", ((PathItem)noExt[0]).FullName);
    }

    [Fact]
    public async Task Download_errors_are_friendly()
    {
        using var host = HostFor(_ => Resp(HttpStatusCode.Forbidden, "no"), out _);
        var ex = await Fails(Run(host, new DownloadFileAction(), null, ("url", "https://example.test/secret.zip")));
        Assert.Contains("403", ex.Message);
        await Fails(Run(host, new DownloadFileAction(), null, ("url", "")));
    }
}

public class ExpandAndCheckTests
{
    [Fact]
    public async Task Follows_redirects_with_head_requests_and_relative_locations()
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsoluteUri switch
        {
            "https://short.test/abc" => Redirect(HttpStatusCode.MovedPermanently, "https://mid.test/x"),
            "https://mid.test/x" => Redirect(HttpStatusCode.Found, "/final"),
            _ => Resp(HttpStatusCode.OK, "pdf", "application/pdf"),
        });
        var final = await LinkExpander.ExpandAsync(handler.Client(), new Uri("https://short.test/abc"));
        Assert.Equal("https://mid.test/final", final.AbsoluteUri);
        Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Head, r.Method));
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Falls_back_to_get_when_head_is_refused()
    {
        var handler = new StubHandler(req =>
        {
            if (req.Method == HttpMethod.Head) return Resp(HttpStatusCode.MethodNotAllowed, "");
            return req.RequestUri!.AbsolutePath == "/start" ? Redirect(HttpStatusCode.TemporaryRedirect, "https://end.test/page") : Resp(HttpStatusCode.OK, "data", "application/json");
        });
        var final = await LinkExpander.ExpandAsync(handler.Client(), new Uri("https://start.test/start"));
        Assert.Equal("https://end.test/page", final.AbsoluteUri);
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task Follows_meta_refresh_pages()
    {
        var handler = new StubHandler(req => req.RequestUri!.Host == "t.test"
            ? Resp(HttpStatusCode.OK, "<html><head><meta http-equiv=\"refresh\" content=\"0;URL='https://real.test/landing'\"></head></html>", "text/html")
            : Resp(HttpStatusCode.OK, "{}", "application/json"));
        var final = await LinkExpander.ExpandAsync(handler.Client(), new Uri("https://t.test/abc"));
        Assert.Equal("https://real.test/landing", final.AbsoluteUri);
    }

    [Fact]
    public async Task A_link_that_is_not_a_redirect_is_returned_as_is()
    {
        var handler = new StubHandler(_ => Resp(HttpStatusCode.OK, "<html>plain page</html>", "text/html"));
        Assert.Equal("https://plain.test/page", (await LinkExpander.ExpandAsync(handler.Client(), new Uri("https://plain.test/page"))).AbsoluteUri);
    }

    [Fact]
    public async Task Gives_up_after_ten_redirects()
    {
        var handler = new StubHandler(req =>
        {
            var n = int.Parse(req.RequestUri!.AbsolutePath.TrimStart('/'));
            return Redirect(HttpStatusCode.Found, $"https://loop.test/{n + 1}");
        });
        var ex = await Assert.ThrowsAsync<ActionException>(() => LinkExpander.ExpandAsync(handler.Client(), new Uri("https://loop.test/0")));
        Assert.Contains("too many times", ex.Message);

        var tenIsFine = new StubHandler(req =>
        {
            var n = int.Parse(req.RequestUri!.AbsolutePath.TrimStart('/'));
            return n >= 10 ? Resp(HttpStatusCode.OK, "end", "text/plain") : Redirect(HttpStatusCode.Found, $"https://loop.test/{n + 1}");
        });
        Assert.Equal("https://loop.test/10", (await LinkExpander.ExpandAsync(tenIsFine.Client(), new Uri("https://loop.test/0"))).AbsoluteUri);
    }

    [Fact]
    public async Task Unreachable_sites_get_a_friendly_error()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("no route"));
        var ex = await Assert.ThrowsAsync<ActionException>(() => LinkExpander.ExpandAsync(handler.Client(), new Uri("https://gone.test/x")));
        Assert.Contains("gone.test", ex.Message);
    }

    private static HttpResponseMessage Redirect(HttpStatusCode code, string to)
    {
        var r = new HttpResponseMessage(code);
        r.Headers.Location = new Uri(to, UriKind.RelativeOrAbsolute);
        return r;
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(301, true)]
    [InlineData(401, true)]
    [InlineData(503, false)]
    [InlineData(500, false)]
    public async Task Site_check_status(int status, bool up)
    {
        var handler = new StubHandler(_ => Resp((HttpStatusCode)status, ""));
        var r = await SiteCheck.CheckAsync(handler.Client(), new Uri("https://site.test"), TimeSpan.FromSeconds(5));
        Assert.Equal(up, r.IsUp);
        Assert.Equal(status, r.Status);
        Assert.Contains("site.test", r.Message);
    }

    [Fact]
    public async Task Site_check_retries_with_get_and_survives_failures_and_timeouts()
    {
        var picky = new StubHandler(req => req.Method == HttpMethod.Head ? Resp(HttpStatusCode.MethodNotAllowed, "") : Resp(HttpStatusCode.OK, "hi"));
        Assert.True((await SiteCheck.CheckAsync(picky.Client(), new Uri("https://site.test"), TimeSpan.FromSeconds(5))).IsUp);

        var down = new StubHandler(_ => throw new HttpRequestException("refused"));
        var r = await SiteCheck.CheckAsync(down.Client(), new Uri("https://site.test"), TimeSpan.FromSeconds(5));
        Assert.False(r.IsUp);
        Assert.Contains("Couldn't reach", r.Message);

        var slow = new SlowHandler();
        var t = await SiteCheck.CheckAsync(new HttpClient(slow), new Uri("https://site.test"), TimeSpan.FromMilliseconds(100));
        Assert.False(t.IsUp);
        Assert.Contains("didn't answer", t.Message);
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Is_up_and_public_ip_actions()
    {
        using var up = new TestHost();
        up.Services.Http = new StubHandler(_ => Resp(HttpStatusCode.OK, "")).Client();
        Assert.True((await Run(up, new CheckWebsiteIsUpAction(), null, ("url", "example.test"))).AsBool());
        up.Services.Http = new StubHandler(_ => Resp(HttpStatusCode.BadGateway, "")).Client();
        var down = await Run(up, new CheckWebsiteIsUpAction(), null, ("url", "https://example.test"));
        Assert.IsType<BooleanItem>(down[0]);
        Assert.False(down.AsBool());

        using var ip = new TestHost();
        ip.Services.Http = new StubHandler(_ => Resp(HttpStatusCode.OK, "203.0.113.5\n")).Client();
        Assert.Equal("203.0.113.5", (await Run(ip, new GetPublicIpAction())).AsText());

        var calls = 0;
        ip.Services.Http = new StubHandler(req => ++calls == 1 ? Resp(HttpStatusCode.InternalServerError, "") : Resp(HttpStatusCode.OK, "2001:db8::1")).Client();
        Assert.Equal("2001:db8::1", (await Run(ip, new GetPublicIpAction())).AsText());

        ip.Services.Http = new StubHandler(_ => Resp(HttpStatusCode.OK, "<html>captive portal</html>")).Client();
        var ex = await Fails(Run(ip, new GetPublicIpAction()));
        Assert.Contains("public IP", ex.Message);
    }
}

public class RssTests
{
    private const string Rss = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:content="http://purl.org/rss/1.0/modules/content/">
          <channel>
            <title>Example &amp; Co News</title>
            <item>
              <title>First &lt;b&gt;post&lt;/b&gt;</title>
              <link>https://example.com/1</link>
              <description><![CDATA[<p>Hello <b>world</b> &amp; friends.</p><p>Second paragraph.</p>]]></description>
              <pubDate>Fri, 02 Oct 2026 09:00:00 GMT</pubDate>
              <dc:creator>Ann</dc:creator>
              <guid>abc-1</guid>
            </item>
            <item>
              <title>Second post</title>
              <link>/relative/2</link>
              <content:encoded><![CDATA[<div>Full <i>content</i></div>]]></content:encoded>
              <pubDate>Thu, 01 Oct 2026 08:30:00 EST</pubDate>
              <author>bob@example.com (Bob)</author>
            </item>
            <item><title>No link, guid url</title><guid>https://example.com/guid-3</guid></item>
          </channel>
        </rss>
        """;

    private const string Atom = """
        <?xml version="1.0" encoding="utf-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <title>Atom Blog</title>
          <entry>
            <title type="html">Entry &amp;amp; one</title>
            <link rel="self" href="https://example.org/self"/>
            <link rel="alternate" href="https://example.org/entry1"/>
            <id>tag:example.org,2026:1</id>
            <published>2026-10-02T09:00:00+02:00</published>
            <updated>2026-10-03T10:00:00Z</updated>
            <summary>Short summary</summary>
            <author><name>Cleo</name></author>
          </entry>
          <entry>
            <title>Entry two</title>
            <link href="https://example.org/entry2"/>
            <updated>2026-09-30T12:00:00Z</updated>
            <content type="html">&lt;p&gt;Body &lt;b&gt;two&lt;/b&gt;&lt;/p&gt;</content>
          </entry>
        </feed>
        """;

    private const string Rdf = """
        <?xml version="1.0"?>
        <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#" xmlns="http://purl.org/rss/1.0/" xmlns:dc="http://purl.org/dc/elements/1.1/">
          <channel><title>RDF Feed</title></channel>
          <item><title>Old style</title><link>https://example.net/old</link><description>Plain text</description><dc:date>2026-05-01T00:00:00Z</dc:date></item>
        </rdf:RDF>
        """;

    [Fact]
    public void Parses_rss_2()
    {
        var feed = RssParser.Parse(Rss, new Uri("https://example.com/feed.xml"));
        Assert.Equal("Example & Co News", feed.Title);
        Assert.Equal(3, feed.Items.Count);

        var first = feed.Items[0];
        Assert.Equal("First post", first.Title);
        Assert.Equal("https://example.com/1", first.Link);
        Assert.Equal("Hello world & friends.\nSecond paragraph.", first.Summary);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero), first.Published);
        Assert.Equal("Ann", first.Author);
        Assert.Equal("abc-1", first.Id);

        var second = feed.Items[1];
        Assert.Equal("https://example.com/relative/2", second.Link);
        Assert.Equal("Full content", second.Summary);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 8, 30, 0, TimeSpan.FromHours(-5)), second.Published);
        Assert.Contains("Bob", second.Author);

        Assert.Equal("https://example.com/guid-3", feed.Items[2].Link);
        Assert.Null(feed.Items[2].Published);
    }

    [Fact]
    public void Parses_atom()
    {
        var feed = RssParser.Parse(Atom);
        Assert.Equal("Atom Blog", feed.Title);
        Assert.Equal(2, feed.Items.Count);
        var one = feed.Items[0];
        Assert.Equal("Entry & one", one.Title);
        Assert.Equal("https://example.org/entry1", one.Link);
        Assert.Equal("Short summary", one.Summary);
        Assert.Equal("Cleo", one.Author);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.FromHours(2)), one.Published);   // published wins over updated
        var two = feed.Items[1];
        Assert.Equal("https://example.org/entry2", two.Link);
        Assert.Equal("Body two", two.Summary);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero), two.Published);
    }

    [Fact]
    public void Parses_rss_1_rdf()
    {
        var feed = RssParser.Parse(Rdf);
        Assert.Equal("RDF Feed", feed.Title);
        var item = Assert.Single(feed.Items);
        Assert.Equal("Old style", item.Title);
        Assert.Equal("https://example.net/old", item.Link);
        Assert.Equal("Plain text", item.Summary);
        Assert.Equal(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), item.Published);
    }

    [Fact]
    public void Tolerates_doctype_bom_and_stray_whitespace()
    {
        var feed = RssParser.Parse("\uFEFF\n  <!DOCTYPE rss [<!ENTITY foo \"bar\">]><rss version=\"2.0\"><channel><title>T</title><item><title>I</title></item></channel></rss>");
        Assert.Single(feed.Items);
    }

    [Fact]
    public void Rejects_things_that_are_not_feeds()
    {
        var html = Assert.Throws<ActionException>(() => RssParser.Parse("<html><body>hi</body></html>"));
        Assert.Contains("RSS or Atom", html.Message);
        Assert.Throws<ActionException>(() => RssParser.Parse("this is not xml"));
        Assert.Throws<ActionException>(() => RssParser.Parse("<rss><nochannel/></rss>"));
    }

    [Theory]
    [InlineData("Fri, 02 Oct 2026 09:00:00 +0200", 2026, 10, 2, 7, 0)]
    [InlineData("Fri, 02 Oct 2026 09:00:00 PST", 2026, 10, 2, 17, 0)]
    [InlineData("2026-10-02T09:00:00Z", 2026, 10, 2, 9, 0)]
    [InlineData("2026-10-02", 2026, 10, 2, 0, 0)]
    public void Dates_in_feeds(string text, int y, int m, int d, int h, int min) =>
        Assert.Equal(new DateTimeOffset(y, m, d, h, min, 0, TimeSpan.Zero), RssParser.ParseDate(text)!.Value.ToUniversalTime());

    [Fact]
    public void Bad_dates_are_null() { Assert.Null(RssParser.ParseDate("yesterday-ish")); Assert.Null(RssParser.ParseDate("")); }

    [Fact]
    public async Task Action_returns_dictionaries_and_honours_the_limit()
    {
        using var host = new TestHost();
        host.Services.Http = new StubHandler(_ => Resp(HttpStatusCode.OK, Rss, "application/rss+xml")).Client();
        var r = await Run(host, new GetRssItemsAction(), null, ("url", "https://example.com/feed.xml"));
        Assert.Equal(3, r.Count);
        var first = (DictionaryItem)r[0];
        Assert.Equal(new[] { "Title", "Link", "Published", "Summary", "Author" }, first.Keys);
        Assert.Equal("First post", first.Get("Title").AsText());
        Assert.IsType<UrlItem>(first.Get("Link")[0]);
        Assert.IsType<DateItem>(first.Get("Published")[0]);
        Assert.Equal("", ((DictionaryItem)r[2]).Get("Published").AsText());
        Assert.Equal(2, (await Run(host, new GetRssItemsAction(), null, ("url", "https://example.com/feed.xml"), ("limit", 2))).Count);

        host.Services.Http = new StubHandler(_ => Resp(HttpStatusCode.OK, "<html></html>", "text/html")).Client();
        await Fails(Run(host, new GetRssItemsAction(), null, ("url", "https://example.com/not-a-feed")));
    }
}
