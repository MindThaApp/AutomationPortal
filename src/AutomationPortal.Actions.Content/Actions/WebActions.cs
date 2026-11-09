using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AutomationPortal.Actions.Content.Utilities;
using AutomationPortal.Core;

namespace AutomationPortal.Actions.Content.Actions;

public sealed class GetContentsOfUrlAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "web.fetch", Title = "Get Contents of URL", Category = ActionCategories.Web, Icon = "GlobeArrowForward24",
        Keywords = new[] { "http", "api", "request", "get", "post", "download", "json", "fetch", "web", "rest", "webhook" },
        Summary = "{method} {url}",
        Description = "Sends a request to a web address and gives back what comes back: JSON becomes a dictionary or list, web pages and other text become text, and other files are saved so you can use them. You can send headers and a body.",
        InputKinds = new[] { ContentKind.Url }, OutputKind = ContentKind.Any, OutputName = "Contents of URL", NeedsInternet = true,
        ImportRiskFor = SendRisk,
        Parameters = new[]
        {
            ActionHelpers.UrlInput(),
            ParameterDescriptor.ChoiceOf("method", "Method", "GET", "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD"),
            new ParameterDescriptor { Key = "headers", Label = "Headers", Type = ParamType.Dictionary, Hint = "Extra information sent with the request, for example Authorization or Accept." },
            ParameterDescriptor.ChoiceOf("bodyType", "Request body", "none",
                new Choice("none", "None"), new Choice("json", "JSON"), new Choice("form", "Form"), new Choice("text", "Text"), new Choice("file", "File")),
            new ParameterDescriptor { Key = "json", Label = "JSON body", Type = ParamType.Dictionary, VisibleWhen = new VisibleWhen("bodyType", "json") },
            new ParameterDescriptor { Key = "form", Label = "Form fields", Type = ParamType.Dictionary, VisibleWhen = new VisibleWhen("bodyType", "form") },
            new ParameterDescriptor { Key = "bodyText", Label = "Text body", Type = ParamType.MultilineText, Multiline = true, VisibleWhen = new VisibleWhen("bodyType", "text") },
            new ParameterDescriptor { Key = "bodyFile", Label = "File to send", Type = ParamType.File, VisibleWhen = new VisibleWhen("bodyType", "file") },
            ParameterDescriptor.Number("timeout", "Give up after", 60, 1, 600, "seconds"),
        },
    };
    public override ActionDescriptor Descriptor => D;

    /// <summary>Sending a body (a file above all) or using any method other than GET or HEAD sends data off the PC.</summary>
    internal static ImportRisk SendRisk(ActionInstance a)
    {
        var method = a.Parameters.GetValueOrDefault("method") is ChoiceValue m ? m.Value : "GET";
        var body = a.Parameters.GetValueOrDefault("bodyType") is ChoiceValue b ? b.Value : "none";
        var sends = !string.Equals(body, "none", StringComparison.OrdinalIgnoreCase)
                    || !(string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) || string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase));
        return sends ? ImportRisk.SendsData : ImportRisk.None;
    }

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var url = await ActionHelpers.GetUrlAsync(ctx);
        var method = (await ctx.GetChoiceAsync("method", "GET")).ToUpperInvariant();
        var bodyType = await ctx.GetChoiceAsync("bodyType", "none");
        var timeout = TimeSpan.FromSeconds(Math.Clamp(await ctx.GetNumberAsync("timeout", 60), 1, 600));
        var headers = (await ctx.GetContentAsync("headers")).AsDictionary();

        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        HttpContent? body = null;
        FileStream? fileStream = null;
        try
        {
            switch (bodyType)
            {
                case "json":
                    {
                        var content = await ctx.GetContentAsync("json");
                        var json = content.Count == 1 && content[0] is TextItem t && ContentValue.TryParseJson(t.Value, out _) ? t.Value : content.ToJson();
                        body = new StringContent(json, Encoding.UTF8, "application/json");
                        break;
                    }
                case "form":
                    {
                        var dict = (await ctx.GetContentAsync("form")).AsDictionary();
                        var pairs = dict?.Entries.Select(e => new KeyValuePair<string, string>(e.Key, e.Value.AsText(","))) ?? Enumerable.Empty<KeyValuePair<string, string>>();
                        body = new FormUrlEncodedContent(pairs);
                        break;
                    }
                case "text":
                    body = new StringContent(await ctx.GetTextAsync("bodyText"), Encoding.UTF8, "text/plain");
                    break;
                case "file":
                    {
                        var path = await ctx.GetPathAsync("bodyFile");
                        if (path is null || !File.Exists(path)) throw new ActionException(path is null ? "Choose the file to send." : $"Couldn't find the file '{Path.GetFileName(path)}'. Check it still exists.");
                        fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        body = new StreamContent(fileStream);
                        body.Headers.ContentType = new MediaTypeHeaderValue(MimeFor(path));
                        break;
                    }
            }
            if (body is not null)
            {
                if (method is "GET" or "HEAD") throw new ActionException($"A {method} request can't have a body. Choose POST, PUT or PATCH, or set the body to None.");
                request.Content = body;
            }
            if (headers is not null)
            {
                foreach (var (name, value) in headers.Entries)
                {
                    var text = value.AsText(", ");
                    if (!request.Headers.TryAddWithoutValidation(name, text) && request.Content is not null)
                        request.Content.Headers.TryAddWithoutValidation(name, text);
                }
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
            cts.CancelAfter(timeout);
            try
            {
                return await HttpHelpers.GuardAsync(url, ctx.CancellationToken, async () =>
                {
                    using var response = await ctx.Services.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) throw new ActionException(HttpHelpers.StatusMessage(response.StatusCode, url));
                    return await ReadResponseAsync(ctx, response, url, method, cts.Token).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ctx.CancellationToken.IsCancellationRequested)
            {
                throw HttpHelpers.TimedOut(url);
            }
        }
        finally
        {
            body?.Dispose();
            fileStream?.Dispose();
        }
    }

    internal static async Task<ContentValue> ReadResponseAsync(ActionContext ctx, HttpResponseMessage response, Uri url, string method, CancellationToken ct)
    {
        if (method == "HEAD")
        {
            var entries = response.Headers.Concat(response.Content.Headers)
                .Select(h => new KeyValuePair<string, ContentValue>(h.Key, ContentValue.Text(string.Join(", ", h.Value))));
            return ContentValue.Dict(entries.Append(new("Status", ContentValue.Number((int)response.StatusCode))));
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
        var isJson = mediaType.Contains("json");
        var isText = mediaType.StartsWith("text/") || mediaType.Contains("xml") || mediaType.Contains("javascript") || mediaType == "application/x-www-form-urlencoded" || mediaType.Length == 0;
        if (!isJson && !isText)
        {
            var path = await HttpHelpers.SaveResponseAsync(ctx, response, url, ctx.TempFolder, null).ConfigureAwait(false);
            return ContentValue.Files(new[] { path });
        }

        var text = await HttpHelpers.ReadTextAsync(response.Content, ct).ConfigureAwait(false);
        if (isJson || mediaType is "text/plain" or "")
        {
            if (ContentValue.TryParseJson(text, out var parsed)) return parsed;
            if (isJson && text.Trim().Length > 0)
            {
                try { return ContentValue.FromJson(text); } catch (System.Text.Json.JsonException) { }
            }
        }
        return ContentValue.Text(text);
    }

    private static string MimeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => "application/json", ".txt" => "text/plain", ".csv" => "text/csv", ".html" or ".htm" => "text/html", ".xml" => "application/xml",
        ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".pdf" => "application/pdf", ".zip" => "application/zip",
        _ => "application/octet-stream",
    };
}

public sealed class DownloadFileAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "web.download", Title = "Download File", Category = ActionCategories.Web, Icon = "ArrowDownload24",
        Keywords = new[] { "save", "get file", "fetch", "pdf", "image", "attachment" },
        Summary = "Download {url}",
        Description = "Downloads a file from the web. Without a folder it is kept only while the shortcut runs; pick a folder to keep it.",
        InputKinds = new[] { ContentKind.Url }, OutputKind = ContentKind.File, OutputName = "Downloaded File", NeedsInternet = true,
        ImportRisk = ImportRisk.DownloadsFiles,
        Parameters = new[]
        {
            ActionHelpers.UrlInput("url", "File address"),
            ParameterDescriptor.FolderParam("folder", "Save in folder") with { Hint = "Leave empty to keep the file only during this run (use Save File later to keep it)." },
            ParameterDescriptor.Text("fileName", "Save as", null, "Keep the original name"),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var urls = await ctx.GetUrlsAsync("url");
        if (urls.Count == 0) throw new ActionException("There is no web address to download. Type or choose a link to a file.");
        var folder = await ctx.GetPathAsync("folder");
        var fileName = (await ctx.GetTextAsync("fileName")).Trim();
        var target = string.IsNullOrWhiteSpace(folder) ? ctx.TempFolder : folder;
        var files = new List<PathItem>();
        foreach (var url in urls)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            ctx.ReportProgress($"Downloading from {url.Host}");
            try { files.Add(await HttpHelpers.DownloadAsync(ctx, url, target, urls.Count == 1 && fileName.Length > 0 ? fileName : null)); }
            catch (UnauthorizedAccessException) { throw new ActionException($"Automation Portal isn't allowed to save into '{target}'. Choose another folder."); }
            catch (DirectoryNotFoundException) { throw new ActionException($"Couldn't find the folder '{target}'. Choose a folder that exists."); }
        }
        return new ContentValue(files);
    }
}

public sealed class GetWebPageTextAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "web.pageText", Title = "Get Web Page Text", Category = ActionCategories.Web, Icon = "DocumentText24",
        Keywords = new[] { "article", "read", "scrape", "html", "content", "title", "summarize", "page" },
        Summary = "Get {what} of the web page {url}",
        Description = "Downloads a web page and gives you the readable text, leaving out menus, adverts and scripts. You can also get just the page title. HTML text from an earlier action works too.",
        InputKinds = new[] { ContentKind.Url, ContentKind.Text }, OutputKind = ContentKind.Text, OutputName = "Page Text", NeedsInternet = true,
        Parameters = new[]
        {
            ActionHelpers.UrlInput("url", "Web page") with { Type = ParamType.Content },
            ParameterDescriptor.ChoiceOf("what", "Get", "text", new Choice("text", "the text"), new Choice("title", "the title"), new Choice("both", "the title and text")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var (html, _) = await PageLoader.LoadAsync(ctx, "url");
        var (title, text) = HtmlExtractor.ExtractText(html);
        return ContentValue.Text(await ctx.GetChoiceAsync("what", "text") switch
        {
            "title" => title ?? "",
            "both" => string.IsNullOrEmpty(title) ? text : title + "\n\n" + text,
            _ => text,
        });
    }
}

internal static class PageLoader
{
    /// <summary>The page HTML from a web address, or HTML given directly. BaseUrl is null for pasted HTML.</summary>
    public static async Task<(string Html, Uri? BaseUrl)> LoadAsync(ActionContext ctx, string key)
    {
        var content = await ctx.GetContentAsync(key);
        if (content.Count == 1 && content[0] is TextItem t && HtmlExtractor.LooksLikeHtml(t.Value)) return (t.Value, null);
        var url = await ActionHelpers.GetUrlAsync(ctx, key);
        ctx.ReportProgress($"Reading {url.Host}");
        var html = await HttpHelpers.GetStringAsync(ctx, url, "text/html,application/xhtml+xml;q=0.9,*/*;q=0.5");
        return (html, url);
    }
}

public sealed class GetLinksFromWebPageAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "web.pageLinks", Title = "Get Links from Web Page", Category = ActionCategories.Web, Icon = "Link24",
        Keywords = new[] { "urls", "anchors", "hrefs", "scrape", "page", "extract" },
        Summary = "Get {scope} links from {url}",
        Description = "Downloads a web page and lists the links on it, as full web addresses.",
        InputKinds = new[] { ContentKind.Url, ContentKind.Text }, OutputKind = ContentKind.Url, OutputName = "Page Links", NeedsInternet = true,
        Parameters = new[]
        {
            ActionHelpers.UrlInput("url", "Web page") with { Type = ParamType.Content },
            ParameterDescriptor.ChoiceOf("scope", "Which links", "all", new Choice("all", "all"), new Choice("sameSite", "same-website"), new Choice("otherSites", "other-website")),
            ParameterDescriptor.Text("containing", "Only links containing", null, "Part of the address or link text"),
            ParameterDescriptor.Bool("unique", "Skip duplicates", true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var (html, baseUrl) = await PageLoader.LoadAsync(ctx, "url");
        var scope = await ctx.GetChoiceAsync("scope", "all");
        var containing = (await ctx.GetTextAsync("containing")).Trim();
        var unique = await ctx.GetBoolAsync("unique", true);
        var links = Extract(html, baseUrl, scope, containing, unique);
        return new ContentValue(links.Select(l => (ContentItem)new UrlItem(l)));
    }

    internal static List<Uri> Extract(string html, Uri? baseUrl, string scope, string containing, bool unique)
    {
        var effectiveBase = baseUrl ?? new Uri("http://pasted-html.invalid/");
        var found = HtmlExtractor.ExtractLinks(html, effectiveBase)
            .Where(l => baseUrl is not null || l.Url.Host != effectiveBase.Host);
        var host = Strip(effectiveBase.Host);
        if (scope == "sameSite") found = found.Where(l => Strip(l.Url.Host) == host);
        else if (scope == "otherSites") found = found.Where(l => Strip(l.Url.Host) != host);
        if (containing.Length > 0)
            found = found.Where(l => l.Url.AbsoluteUri.Contains(containing, StringComparison.OrdinalIgnoreCase) || l.Text.Contains(containing, StringComparison.OrdinalIgnoreCase));
        var urls = found.Select(l => l.Url);
        if (unique) urls = urls.DistinctBy(u => u.AbsoluteUri);
        return urls.ToList();
    }

    private static string Strip(string host) => host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
}

public sealed class GetImagesFromWebPageAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "web.pageImages", Title = "Get Images from Web Page", Category = ActionCategories.Web, Icon = "ImageMultiple24",
        Keywords = new[] { "pictures", "photos", "scrape", "download", "page", "gallery" },
        Summary = "Get up to {limit} images from {url}",
        Description = "Finds the pictures on a web page and downloads them. Tiny tracking pixels are skipped. Turn off \"Download\" to get just their web addresses.",
        InputKinds = new[] { ContentKind.Url, ContentKind.Text }, OutputKind = ContentKind.Image, OutputName = "Page Images", NeedsInternet = true,
        Parameters = new[]
        {
            ActionHelpers.UrlInput("url", "Web page") with { Type = ParamType.Content },
            ParameterDescriptor.Number("limit", "At most", 10, 1, 200),
            ParameterDescriptor.Bool("download", "Download the images", true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var (html, baseUrl) = await PageLoader.LoadAsync(ctx, "url");
        var limit = (int)Math.Clamp(await ctx.GetNumberAsync("limit", 10), 1, 200);
        var download = await ctx.GetBoolAsync("download", true);
        var effectiveBase = baseUrl ?? new Uri("http://pasted-html.invalid/");
        var urls = HtmlExtractor.ExtractImageUrls(html, effectiveBase).Where(u => baseUrl is not null || u.Host != effectiveBase.Host).ToList();
        if (urls.Count == 0) throw new ActionException("Couldn't find any pictures on that page.");
        if (!download) return new ContentValue(urls.Take(limit).Select(u => (ContentItem)new UrlItem(u)));

        var images = new List<ContentItem>();
        var failures = 0;
        foreach (var url in urls)
        {
            if (images.Count >= limit) break;
            ctx.CancellationToken.ThrowIfCancellationRequested();
            ctx.ReportProgress($"Downloading image {images.Count + 1} of {Math.Min(limit, urls.Count)}");
            try
            {
                var file = await HttpHelpers.DownloadAsync(ctx, url, ctx.TempFolder, null, imagesOnly: true);
                images.Add(file is ImageItem ? file : new ImageItem(file.Path));
            }
            catch (ActionException ex) { failures++; ctx.Log($"Skipped an image: {ex.Message}"); }
        }
        if (images.Count == 0) throw new ActionException("Found pictures on the page but couldn't download any of them. Try again, or turn off \"Download the images\" to get their addresses.");
        return new ContentValue(images);
    }
}

public sealed class OpenUrlsAction : ShortcutAction
{
    internal static Action<Uri> Launcher { get; set; } = uri =>
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });

    private static readonly ActionDescriptor D = new()
    {
        Id = "web.open", Title = "Open URLs", Category = ActionCategories.Web, Icon = "Open24",
        Keywords = new[] { "browser", "website", "launch", "link", "visit", "mail" },
        Summary = "Open {urls}",
        Description = "Opens one or more web addresses in your default browser.",
        InputKinds = new[] { ContentKind.Url }, OutputKind = ContentKind.Url, OutputName = "Opened URLs",
        Parameters = new[]
        {
            ParameterDescriptor.Content("urls", "Web addresses", isInput: true, required: true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var urls = await ctx.GetUrlsAsync("urls");
        if (urls.Count == 0) throw new ActionException("There is no web address to open. Type or choose a link such as https://example.com.");
        foreach (var url in urls)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            if (url.Scheme is not ("http" or "https" or "mailto" or "ftp"))
                throw new ActionException($"Automation Portal only opens web and email links here, not '{url.Scheme}:' links.");
            try { Launcher(url); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                throw new ActionException($"Windows couldn't open {url.AbsoluteUri}. Check that a web browser is set up. ({ex.Message})", ex);
            }
        }
        return new ContentValue(urls.Select(u => (ContentItem)new UrlItem(u)));
    }
}

public sealed class SearchTheWebAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "web.search", Title = "Search the Web", Category = ActionCategories.Web, Icon = "Search24",
        Keywords = new[] { "google", "bing", "duckduckgo", "youtube", "wikipedia", "amazon", "maps", "find", "look up" },
        Summary = "Search {engine} for {query}",
        Description = "Searches Google, Bing, DuckDuckGo, YouTube, Wikipedia, Amazon or Maps for your words and opens the results in your browser.",
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Url, OutputName = "Search URL",
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("engine", "Search with", "google",
                new Choice("google", "Google"), new Choice("bing", "Bing"), new Choice("duckduckgo", "DuckDuckGo"), new Choice("youtube", "YouTube"),
                new Choice("wikipedia", "Wikipedia"), new Choice("amazon", "Amazon"), new Choice("maps", "Maps")),
            new ParameterDescriptor { Key = "query", Label = "Search for", Type = ParamType.Text, IsInput = true, Required = true },
            ParameterDescriptor.Bool("open", "Open the results in the browser", true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var query = (await ctx.GetTextAsync("query")).Trim();
        if (query.Length == 0) throw new ActionException("Type what you want to search for.");
        var engine = await ctx.GetChoiceAsync("engine", "google");
        var url = BuildUrl(engine, query);
        if (await ctx.GetBoolAsync("open", true)) OpenUrlsAction.Launcher(url);
        return ContentValue.Url(url);
    }

    public static Uri BuildUrl(string engine, string query)
    {
        var q = Uri.EscapeDataString(query);
        var lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        if (lang.Length != 2) lang = "en";
        return new Uri(engine switch
        {
            "bing" => $"https://www.bing.com/search?q={q}",
            "duckduckgo" => $"https://duckduckgo.com/?q={q}",
            "youtube" => $"https://www.youtube.com/results?search_query={q}",
            "wikipedia" => $"https://{lang}.wikipedia.org/w/index.php?search={q}",
            "amazon" => $"https://www.amazon.com/s?k={q}",
            "maps" => $"https://www.google.com/maps/search/?api=1&query={q}",
            _ => $"https://www.google.com/search?q={q}",
        });
    }
}

public sealed class GetPartOfUrlAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "web.urlPart", Title = "Get Part of URL", Category = ActionCategories.Web, Icon = "LinkSquare24",
        Keywords = new[] { "host", "domain", "path", "query", "parameter", "scheme", "port", "extension", "file name", "split" },
        Summary = "Get the {part} of {url}",
        Description = "Picks one part out of a web address, such as the website name, the path, the file name or the value of a query parameter.",
        InputKinds = new[] { ContentKind.Url }, OutputKind = ContentKind.Text, OutputName = "URL Part",
        Parameters = new[]
        {
            ParameterDescriptor.Content("url", "Web address", isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("part", "Part", "domain",
                new Choice("domain", "website (without www)"), new Choice("host", "host"), new Choice("scheme", "scheme (https)"),
                new Choice("path", "path"), new Choice("fileName", "file name"), new Choice("extension", "file extension"),
                new Choice("query", "query"), new Choice("queryParam", "value of a query parameter"), new Choice("fragment", "fragment (after #)"), new Choice("port", "port")),
            ParameterDescriptor.Text("name", "Parameter name", null, "e.g. id") with { VisibleWhen = new VisibleWhen("part", "queryParam") },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var urls = await ctx.GetUrlsAsync("url");
        if (urls.Count == 0) throw new ActionException("There is no web address to look at. Type or choose a link such as https://example.com/page.");
        var part = await ctx.GetChoiceAsync("part", "domain");
        var name = (await ctx.GetTextAsync("name")).Trim();
        if (part == "queryParam" && name.Length == 0) throw new ActionException("Enter the name of the query parameter, for example id.");
        return ContentValue.Texts(urls.Select(u => Part(u, part, name)));
    }

    internal static string Part(Uri u, string part, string name)
    {
        switch (part)
        {
            case "host": return u.Host;
            case "domain": return u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host[4..] : u.Host;
            case "scheme": return u.Scheme;
            case "path": return Uri.UnescapeDataString(u.AbsolutePath);
            case "fileName": return Uri.UnescapeDataString(Path.GetFileName(u.AbsolutePath.TrimEnd('/')));
            case "extension": return Path.GetExtension(u.AbsolutePath).TrimStart('.');
            case "query": return u.Query.TrimStart('?');
            case "fragment": return u.Fragment.TrimStart('#');
            case "port": return u.Port.ToString();
            case "queryParam":
                foreach (var pair in u.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = pair.IndexOf('=');
                    var key = WebUtility.UrlDecode(eq >= 0 ? pair[..eq] : pair);
                    if (key.Equals(name, StringComparison.OrdinalIgnoreCase)) return eq >= 0 ? WebUtility.UrlDecode(pair[(eq + 1)..]) : "";
                }
                return "";
            default: return u.AbsoluteUri;
        }
    }
}

public sealed class ExpandShortLinkAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "web.expand", Title = "Expand Short Link", Category = ActionCategories.Web, Icon = "LinkMultiple24",
        Keywords = new[] { "bit.ly", "tinyurl", "shortener", "redirect", "unshorten", "t.co", "final address" },
        Summary = "Expand the short link {url}",
        Description = "Follows a short link (like bit.ly) to find the real web address it leads to, without opening it in your browser.",
        InputKinds = new[] { ContentKind.Url }, OutputKind = ContentKind.Url, OutputName = "Expanded URL", NeedsInternet = true,
        Parameters = new[] { ParameterDescriptor.Content("url", "Short link", isInput: true, required: true) },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var urls = await ctx.GetUrlsAsync("url");
        if (urls.Count == 0) throw new ActionException("There is no link to expand. Type or choose a link such as https://bit.ly/abc.");
        var result = new List<ContentItem>();
        foreach (var url in urls)
            result.Add(new UrlItem(await LinkExpander.ExpandAsync(LinkExpander.NoRedirectClient, url, ctx.CancellationToken)));
        return new ContentValue(result);
    }
}

public sealed class RemoveTrackingFromLinkAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "web.cleanUrl", Title = "Remove Tracking from Link", Category = ActionCategories.Web, Icon = "ShieldCheckmark24",
        Keywords = new[] { "utm", "fbclid", "gclid", "clean", "privacy", "tracking", "share", "strip" },
        Summary = "Remove tracking from {url}",
        Description = "Cleans a link by removing the extra tracking bits (like utm_source, fbclid or gclid) that websites add, and keeps everything the page needs.",
        InputKinds = new[] { ContentKind.Url }, OutputKind = ContentKind.Url, OutputName = "Clean Link",
        Parameters = new[] { ParameterDescriptor.Content("url", "Link", isInput: true, required: true) },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var urls = await ctx.GetUrlsAsync("url");
        if (urls.Count == 0) throw new ActionException("There is no link to clean. Type or choose a link such as https://example.com/?utm_source=news.");
        return new ContentValue(urls.Select(u => (ContentItem)new UrlItem(LinkCleaner.Clean(u))));
    }
}

public sealed class GetRssItemsAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "web.rss", Title = "Get Items from RSS Feed", Category = ActionCategories.Web, Icon = "Rss24",
        Keywords = new[] { "feed", "atom", "news", "blog", "podcast", "headlines", "articles" },
        Summary = "Get up to {limit} items from the feed {url}",
        Description = "Reads an RSS or Atom feed and gives you its latest items. Each item has a Title, Link, Published date, Summary and Author.",
        InputKinds = new[] { ContentKind.Url }, OutputKind = ContentKind.Dictionary, OutputName = "Feed Items", NeedsInternet = true,
        Parameters = new[]
        {
            ActionHelpers.UrlInput("url", "Feed address"),
            ParameterDescriptor.Number("limit", "At most", 20, 1, 500),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var url = await ActionHelpers.GetUrlAsync(ctx);
        var limit = (int)Math.Clamp(await ctx.GetNumberAsync("limit", 20), 1, 500);
        var xml = await HttpHelpers.GetStringAsync(ctx, url, "application/rss+xml, application/atom+xml, application/xml, text/xml, */*;q=0.5");
        var feed = RssParser.Parse(xml, url);
        return ToContent(feed, limit);
    }

    internal static ContentValue ToContent(Feed feed, int limit) =>
        new(feed.Items.Take(limit).Select(i => (ContentItem)new DictionaryItem(new List<KeyValuePair<string, ContentValue>>
        {
            new("Title", ContentValue.Text(i.Title)),
            new("Link", Uri.TryCreate(i.Link, UriKind.Absolute, out var u) ? ContentValue.Url(u) : ContentValue.Text(i.Link)),
            new("Published", i.Published is { } p ? ContentValue.Date(p) : ContentValue.Text("")),
            new("Summary", ContentValue.Text(i.Summary)),
            new("Author", ContentValue.Text(i.Author)),
        }, i.Title)));
}

public sealed class GetPublicIpAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "web.publicIp", Title = "Get Public IP Address", Category = ActionCategories.Web, Icon = "GlobeLocation24",
        Keywords = new[] { "ip", "address", "network", "internet", "external", "my ip" },
        Summary = "Get my public IP address",
        Description = "Finds the IP address that websites see when you connect from this computer.",
        OutputKind = ContentKind.Text, OutputName = "Public IP Address", NeedsInternet = true,
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        Exception? last = null;
        foreach (var endpoint in new[] { "https://api.ipify.org", "https://api64.ipify.org" })
        {
            try
            {
                var text = (await HttpHelpers.GetStringAsync(ctx, new Uri(endpoint))).Trim();
                if (IPAddress.TryParse(text, out _)) return ContentValue.Text(text);
            }
            catch (ActionException ex) when (!ctx.CancellationToken.IsCancellationRequested) { last = ex; }
        }
        throw new ActionException("Couldn't find your public IP address. Check your internet connection and try again." + (last is null ? "" : $" ({last.Message})"));
    }
}

public sealed class CheckWebsiteIsUpAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "web.isUp", Title = "Check if Website Is Up", Category = ActionCategories.Web, Icon = "PlugConnected24",
        Keywords = new[] { "online", "down", "status", "ping", "monitor", "available", "working" },
        Summary = "Check if {url} is up",
        Description = "Finds out whether a website answers. The result is yes or no, which you can use in an If.",
        InputKinds = new[] { ContentKind.Url }, OutputKind = ContentKind.Boolean, OutputName = "Is Up", NeedsInternet = true,
        Parameters = new[]
        {
            ActionHelpers.UrlInput("url", "Website") with { Type = ParamType.Content },
            ParameterDescriptor.Number("timeout", "Wait up to", 10, 1, 120, "seconds"),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var url = await ActionHelpers.GetUrlAsync(ctx);
        var timeout = TimeSpan.FromSeconds(Math.Clamp(await ctx.GetNumberAsync("timeout", 10), 1, 120));
        var result = await SiteCheck.CheckAsync(ctx.Services.Http, url, timeout, ctx.CancellationToken);
        ctx.Log(result.Message);
        ctx.ReportProgress(result.Message);
        return ContentValue.Bool(result.IsUp);
    }
}
