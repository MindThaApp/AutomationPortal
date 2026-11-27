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
        Id = "web.fetch", Title = Strings.Get("WebFetch_Title"), Category = ActionCategories.Web, Icon = "GlobeArrowForward24",
        Keywords = new[] { "http", "api", "request", "get", "post", "download", "json", "fetch", "web", "rest", "webhook" },
        Summary = Strings.Get("WebFetch_Summary"),
        Description = Strings.Get("WebFetch_Description"),
        InputKinds = new[] { ContentKind.Url }, OutputKind = ContentKind.Any, OutputName = Strings.Get("WebFetch_OutputName"), NeedsInternet = true,
        ImportRiskFor = SendRisk,
        Parameters = new[]
        {
            ActionHelpers.UrlInput(),
            ParameterDescriptor.ChoiceOf("method", Strings.Get("WebFetch_Param_Method_Label"), "GET", "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD"),
            new ParameterDescriptor { Key = "headers", Label = Strings.Get("WebFetch_Param_Headers_Label"), Type = ParamType.Dictionary, Hint = Strings.Get("WebFetch_Param_Headers_Hint") },
            ParameterDescriptor.ChoiceOf("bodyType", Strings.Get("WebFetch_Param_BodyType_Label"), "none",
                new Choice("none", Strings.Get("WebFetch_Choice_None")), new Choice("json", Strings.Get("WebFetch_Choice_Json")), new Choice("form", Strings.Get("WebFetch_Choice_Form")), new Choice("text", Strings.Get("WebFetch_Choice_Text")), new Choice("file", Strings.Get("WebFetch_Choice_File"))),
            new ParameterDescriptor { Key = "json", Label = Strings.Get("WebFetch_Param_Json_Label"), Type = ParamType.Dictionary, VisibleWhen = new VisibleWhen("bodyType", "json") },
            new ParameterDescriptor { Key = "form", Label = Strings.Get("WebFetch_Param_Form_Label"), Type = ParamType.Dictionary, VisibleWhen = new VisibleWhen("bodyType", "form") },
            new ParameterDescriptor { Key = "bodyText", Label = Strings.Get("WebFetch_Param_BodyText_Label"), Type = ParamType.MultilineText, Multiline = true, VisibleWhen = new VisibleWhen("bodyType", "text") },
            new ParameterDescriptor { Key = "bodyFile", Label = Strings.Get("WebFetch_Param_BodyFile_Label"), Type = ParamType.File, VisibleWhen = new VisibleWhen("bodyType", "file") },
            ParameterDescriptor.Number("timeout", Strings.Get("WebFetch_Param_Timeout_Label"), 60, 1, 600, Strings.Get("WebFetch_Param_Timeout_Unit")),
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
                        if (path is null || !File.Exists(path)) throw new ActionException(path is null ? Strings.Get("WebFetch_Error_ChooseTheFileTo") : Strings.Format("WebFetch_Error_CouldnTFindThe", Path.GetFileName(path)));
                        fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        body = new StreamContent(fileStream);
                        body.Headers.ContentType = new MediaTypeHeaderValue(MimeFor(path));
                        break;
                    }
            }
            if (body is not null)
            {
                if (method is "GET" or "HEAD") throw new ActionException(Strings.Format("WebFetch_Error_A0RequestCan", method));
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
        Id = "web.download", Title = Strings.Get("WebDownload_Title"), Category = ActionCategories.Web, Icon = "ArrowDownload24",
        Keywords = new[] { "save", "get file", "fetch", "pdf", "image", "attachment" },
        Summary = Strings.Get("WebDownload_Summary"),
        Description = Strings.Get("WebDownload_Description"),
        InputKinds = new[] { ContentKind.Url }, OutputKind = ContentKind.File, OutputName = Strings.Get("WebDownload_OutputName"), NeedsInternet = true,
        ImportRisk = ImportRisk.DownloadsFiles,
        Parameters = new[]
        {
            ActionHelpers.UrlInput("url", Strings.Get("WebDownload_Param_Url_Label")),
            ParameterDescriptor.FolderParam("folder", Strings.Get("WebDownload_Param_Folder_Label")) with { Hint = Strings.Get("WebDownload_Param_Folder_Hint") },
            ParameterDescriptor.Text("fileName", Strings.Get("WebDownload_Param_FileName_Label"), null, Strings.Get("WebDownload_Param_FileName_Placeholder")),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var urls = await ctx.GetUrlsAsync("url");
        if (urls.Count == 0) throw new ActionException(Strings.Get("WebDownload_Error_ThereIsNoWeb"));
        var folder = await ctx.GetPathAsync("folder");
        var fileName = (await ctx.GetTextAsync("fileName")).Trim();
        var target = string.IsNullOrWhiteSpace(folder) ? ctx.TempFolder : folder;
        var files = new List<PathItem>();
        foreach (var url in urls)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            ctx.ReportProgress(Strings.Format("WebDownload_Progress_DownloadingFrom0", url.Host));
            try { files.Add(await HttpHelpers.DownloadAsync(ctx, url, target, urls.Count == 1 && fileName.Length > 0 ? fileName : null)); }
            catch (UnauthorizedAccessException) { throw new ActionException(Strings.Format("WebDownload_Error_AutomationPortalIsnT", target)); }
            catch (DirectoryNotFoundException) { throw new ActionException(Strings.Format("WebDownload_Error_CouldnTFindThe", target)); }
        }
        return new ContentValue(files);
    }
}

public sealed class GetWebPageTextAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "web.pageText", Title = Strings.Get("WebPageText_Title"), Category = ActionCategories.Web, Icon = "DocumentText24",
        Keywords = new[] { "article", "read", "scrape", "html", "content", "title", "summarize", "page" },
        Summary = Strings.Get("WebPageText_Summary"),
        Description = Strings.Get("WebPageText_Description"),
        InputKinds = new[] { ContentKind.Url, ContentKind.Text }, OutputKind = ContentKind.Text, OutputName = Strings.Get("WebPageText_OutputName"), NeedsInternet = true,
        Parameters = new[]
        {
            ActionHelpers.UrlInput("url", Strings.Get("WebPageText_Param_Url_Label")) with { Type = ParamType.Content },
            ParameterDescriptor.ChoiceOf("what", Strings.Get("WebPageText_Param_What_Label"), "text", new Choice("text", Strings.Get("WebPageText_Choice_Text")), new Choice("title", Strings.Get("WebPageText_Choice_Title")), new Choice("both", Strings.Get("WebPageText_Choice_Both"))),
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
        ctx.ReportProgress(Strings.Format("WebActions_Progress_Reading0", url.Host));
        var html = await HttpHelpers.GetStringAsync(ctx, url, "text/html,application/xhtml+xml;q=0.9,*/*;q=0.5");
        return (html, url);
    }
}

public sealed class GetLinksFromWebPageAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "web.pageLinks", Title = Strings.Get("WebPageLinks_Title"), Category = ActionCategories.Web, Icon = "Link24",
        Keywords = new[] { "urls", "anchors", "hrefs", "scrape", "page", "extract" },
        Summary = Strings.Get("WebPageLinks_Summary"),
        Description = Strings.Get("WebPageLinks_Description"),
        InputKinds = new[] { ContentKind.Url, ContentKind.Text }, OutputKind = ContentKind.Url, OutputName = Strings.Get("WebPageLinks_OutputName"), NeedsInternet = true,
        Parameters = new[]
        {
            ActionHelpers.UrlInput("url", Strings.Get("WebPageLinks_Param_Url_Label")) with { Type = ParamType.Content },
            ParameterDescriptor.ChoiceOf("scope", Strings.Get("WebPageLinks_Param_Scope_Label"), "all", new Choice("all", Strings.Get("WebPageLinks_Choice_All")), new Choice("sameSite", Strings.Get("WebPageLinks_Choice_SameSite")), new Choice("otherSites", Strings.Get("WebPageLinks_Choice_OtherSites"))),
            ParameterDescriptor.Text("containing", Strings.Get("WebPageLinks_Param_Containing_Label"), null, Strings.Get("WebPageLinks_Param_Containing_Placeholder")),
            ParameterDescriptor.Bool("unique", Strings.Get("WebPageLinks_Param_Unique_Label"), true),
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
        Id = "web.pageImages", Title = Strings.Get("WebPageImages_Title"), Category = ActionCategories.Web, Icon = "ImageMultiple24",
        Keywords = new[] { "pictures", "photos", "scrape", "download", "page", "gallery" },
        Summary = Strings.Get("WebPageImages_Summary"),
        Description = Strings.Get("WebPageImages_Description"),
        InputKinds = new[] { ContentKind.Url, ContentKind.Text }, OutputKind = ContentKind.Image, OutputName = Strings.Get("WebPageImages_OutputName"), NeedsInternet = true,
        Parameters = new[]
        {
            ActionHelpers.UrlInput("url", Strings.Get("WebPageImages_Param_Url_Label")) with { Type = ParamType.Content },
            ParameterDescriptor.Number("limit", Strings.Get("WebPageImages_Param_Limit_Label"), 10, 1, 200),
            ParameterDescriptor.Bool("download", Strings.Get("WebPageImages_Param_Download_Label"), true),
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
        if (urls.Count == 0) throw new ActionException(Strings.Get("WebPageImages_Error_CouldnTFindAny"));
        if (!download) return new ContentValue(urls.Take(limit).Select(u => (ContentItem)new UrlItem(u)));

        var images = new List<ContentItem>();
        var failures = 0;
        foreach (var url in urls)
        {
            if (images.Count >= limit) break;
            ctx.CancellationToken.ThrowIfCancellationRequested();
            ctx.ReportProgress(Strings.Format("WebPageImages_Progress_DownloadingImage0Of", images.Count + 1, Math.Min(limit, urls.Count)));
            try
            {
                var file = await HttpHelpers.DownloadAsync(ctx, url, ctx.TempFolder, null, imagesOnly: true);
                images.Add(file is ImageItem ? file : new ImageItem(file.Path));
            }
            catch (ActionException ex) { failures++; ctx.Log(Strings.Format("WebPageImages_Log_SkippedAnImage0", ex.Message)); }
        }
        if (images.Count == 0) throw new ActionException(Strings.Get("WebPageImages_Error_FoundPicturesOnThe"));
        return new ContentValue(images);
    }
}

public sealed class OpenUrlsAction : ShortcutAction
{
    internal static Action<Uri> Launcher { get; set; } = uri =>
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });

    private static readonly ActionDescriptor D = new()
    {
        Id = "web.open", Title = Strings.Get("WebOpen_Title"), Category = ActionCategories.Web, Icon = "Open24",
        Keywords = new[] { "browser", "website", "launch", "link", "visit", "mail" },
        Summary = Strings.Get("WebOpen_Summary"),
        Description = Strings.Get("WebOpen_Description"),
        InputKinds = new[] { ContentKind.Url }, OutputKind = ContentKind.Url, OutputName = Strings.Get("WebOpen_OutputName"),
        ImportRiskFor = OpenRisk,
        Parameters = new[]
        {
            ParameterDescriptor.Content("urls", Strings.Get("WebOpen_Param_Urls_Label"), isInput: true, required: true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    /// <summary>
    /// An address that comes from the input or a variable can carry anything the shortcut read (files, the clipboard) to a web site,
    /// so it counts as sending data. A typed-in address, or one asked for when it runs, doesn't.
    /// </summary>
    internal static ImportRisk OpenRisk(ActionInstance a) => a.Parameters.GetValueOrDefault("urls") switch
    {
        AskValue => ImportRisk.None,
        null => ImportRisk.SendsData,
        TextValue t when t.IsBlank => ImportRisk.SendsData,
        var v when v.VarRefs().Any() => ImportRisk.SendsData,
        _ => ImportRisk.None,
    };

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        TestRootGuard.BlockSystemChange("opening web pages");
        var urls = await ctx.GetUrlsAsync("urls");
        if (urls.Count == 0) throw new ActionException(Strings.Get("WebOpen_Error_ThereIsNoWeb"));
        foreach (var url in urls)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            if (url.Scheme is not ("http" or "https" or "mailto" or "ftp"))
                throw new ActionException(Strings.Format("WebOpen_Error_AutomationPortalOnlyOpens", url.Scheme));
            try { Launcher(url); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                throw new ActionException(Strings.Format("WebOpen_Error_WindowsCouldnTOpen", url.AbsoluteUri, ex.Message), ex);
            }
        }
        return new ContentValue(urls.Select(u => (ContentItem)new UrlItem(u)));
    }
}

public sealed class SearchTheWebAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "web.search", Title = Strings.Get("WebSearch_Title"), Category = ActionCategories.Web, Icon = "Search24",
        Keywords = new[] { "google", "bing", "duckduckgo", "youtube", "wikipedia", "amazon", "maps", "find", "look up" },
        Summary = Strings.Get("WebSearch_Summary"),
        Description = Strings.Get("WebSearch_Description"),
        InputKinds = new[] { ContentKind.Text }, OutputKind = ContentKind.Url, OutputName = Strings.Get("WebSearch_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.ChoiceOf("engine", Strings.Get("WebSearch_Param_Engine_Label"), "google",
                new Choice("google", Strings.Get("WebSearch_Choice_Google")), new Choice("bing", Strings.Get("WebSearch_Choice_Bing")), new Choice("duckduckgo", Strings.Get("WebSearch_Choice_Duckduckgo")), new Choice("youtube", Strings.Get("WebSearch_Choice_Youtube")),
                new Choice("wikipedia", Strings.Get("WebSearch_Choice_Wikipedia")), new Choice("amazon", Strings.Get("WebSearch_Choice_Amazon")), new Choice("maps", Strings.Get("WebSearch_Choice_Maps"))),
            new ParameterDescriptor { Key = "query", Label = Strings.Get("WebSearch_Param_Query_Label"), Type = ParamType.Text, IsInput = true, Required = true },
            ParameterDescriptor.Bool("open", Strings.Get("WebSearch_Param_Open_Label"), true),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var query = (await ctx.GetTextAsync("query")).Trim();
        if (query.Length == 0) throw new ActionException(Strings.Get("WebSearch_Error_TypeWhatYouWant"));
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
        Id = "web.urlPart", Title = Strings.Get("WebUrlPart_Title"), Category = ActionCategories.Web, Icon = "LinkSquare24",
        Keywords = new[] { "host", "domain", "path", "query", "parameter", "scheme", "port", "extension", "file name", "split" },
        Summary = Strings.Get("WebUrlPart_Summary"),
        Description = Strings.Get("WebUrlPart_Description"),
        InputKinds = new[] { ContentKind.Url }, OutputKind = ContentKind.Text, OutputName = Strings.Get("WebUrlPart_OutputName"),
        Parameters = new[]
        {
            ParameterDescriptor.Content("url", Strings.Get("WebUrlPart_Param_Url_Label"), isInput: true, required: true),
            ParameterDescriptor.ChoiceOf("part", Strings.Get("WebUrlPart_Param_Part_Label"), "domain",
                new Choice("domain", Strings.Get("WebUrlPart_Choice_Domain")), new Choice("host", Strings.Get("WebUrlPart_Choice_Host")), new Choice("scheme", Strings.Get("WebUrlPart_Choice_Scheme")),
                new Choice("path", Strings.Get("WebUrlPart_Choice_Path")), new Choice("fileName", Strings.Get("WebUrlPart_Choice_FileName")), new Choice("extension", Strings.Get("WebUrlPart_Choice_Extension")),
                new Choice("query", Strings.Get("WebUrlPart_Choice_Query")), new Choice("queryParam", Strings.Get("WebUrlPart_Choice_QueryParam")), new Choice("fragment", Strings.Get("WebUrlPart_Choice_Fragment")), new Choice("port", Strings.Get("WebUrlPart_Choice_Port"))),
            ParameterDescriptor.Text("name", Strings.Get("WebUrlPart_Param_Name_Label"), null, Strings.Get("WebUrlPart_Param_Name_Placeholder")) with { VisibleWhen = new VisibleWhen("part", "queryParam") },
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var urls = await ctx.GetUrlsAsync("url");
        if (urls.Count == 0) throw new ActionException(Strings.Get("WebUrlPart_Error_ThereIsNoWeb"));
        var part = await ctx.GetChoiceAsync("part", "domain");
        var name = (await ctx.GetTextAsync("name")).Trim();
        if (part == "queryParam" && name.Length == 0) throw new ActionException(Strings.Get("WebUrlPart_Error_EnterTheNameOf"));
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
        Id = "web.expand", Title = Strings.Get("WebExpand_Title"), Category = ActionCategories.Web, Icon = "LinkMultiple24",
        Keywords = new[] { "bit.ly", "tinyurl", "shortener", "redirect", "unshorten", "t.co", "final address" },
        Summary = Strings.Get("WebExpand_Summary"),
        Description = Strings.Get("WebExpand_Description"),
        InputKinds = new[] { ContentKind.Url }, OutputKind = ContentKind.Url, OutputName = Strings.Get("WebExpand_OutputName"), NeedsInternet = true,
        Parameters = new[] { ParameterDescriptor.Content("url", Strings.Get("WebExpand_Param_Url_Label"), isInput: true, required: true) },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var urls = await ctx.GetUrlsAsync("url");
        if (urls.Count == 0) throw new ActionException(Strings.Get("WebExpand_Error_ThereIsNoLink"));
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
        Id = "web.cleanUrl", Title = Strings.Get("WebCleanUrl_Title"), Category = ActionCategories.Web, Icon = "ShieldCheckmark24",
        Keywords = new[] { "utm", "fbclid", "gclid", "clean", "privacy", "tracking", "share", "strip" },
        Summary = Strings.Get("WebCleanUrl_Summary"),
        Description = Strings.Get("WebCleanUrl_Description"),
        InputKinds = new[] { ContentKind.Url }, OutputKind = ContentKind.Url, OutputName = Strings.Get("WebCleanUrl_OutputName"),
        Parameters = new[] { ParameterDescriptor.Content("url", Strings.Get("WebCleanUrl_Param_Url_Label"), isInput: true, required: true) },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var urls = await ctx.GetUrlsAsync("url");
        if (urls.Count == 0) throw new ActionException(Strings.Get("WebCleanUrl_Error_ThereIsNoLink"));
        return new ContentValue(urls.Select(u => (ContentItem)new UrlItem(LinkCleaner.Clean(u))));
    }
}

public sealed class GetRssItemsAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "web.rss", Title = Strings.Get("WebRss_Title"), Category = ActionCategories.Web, Icon = "Rss24",
        Keywords = new[] { "feed", "atom", "news", "blog", "podcast", "headlines", "articles" },
        Summary = Strings.Get("WebRss_Summary"),
        Description = Strings.Get("WebRss_Description"),
        InputKinds = new[] { ContentKind.Url }, OutputKind = ContentKind.Dictionary, OutputName = Strings.Get("WebRss_OutputName"), NeedsInternet = true,
        Parameters = new[]
        {
            ActionHelpers.UrlInput("url", Strings.Get("WebRss_Param_Url_Label")),
            ParameterDescriptor.Number("limit", Strings.Get("WebRss_Param_Limit_Label"), 20, 1, 500),
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
        Id = "web.publicIp", Title = Strings.Get("WebPublicIp_Title"), Category = ActionCategories.Web, Icon = "GlobeLocation24",
        Keywords = new[] { "ip", "address", "network", "internet", "external", "my ip" },
        Summary = Strings.Get("WebPublicIp_Summary"),
        Description = Strings.Get("WebPublicIp_Description"),
        OutputKind = ContentKind.Text, OutputName = Strings.Get("WebPublicIp_OutputName"), NeedsInternet = true,
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
        throw new ActionException(Strings.Get("WebPublicIp_Error_CouldnTFindYour") + (last is null ? "" : $" ({last.Message})"));
    }
}

public sealed class CheckWebsiteIsUpAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "web.isUp", Title = Strings.Get("WebIsUp_Title"), Category = ActionCategories.Web, Icon = "PlugConnected24",
        Keywords = new[] { "online", "down", "status", "ping", "monitor", "available", "working" },
        Summary = Strings.Get("WebIsUp_Summary"),
        Description = Strings.Get("WebIsUp_Description"),
        InputKinds = new[] { ContentKind.Url }, OutputKind = ContentKind.Boolean, OutputName = Strings.Get("WebIsUp_OutputName"), NeedsInternet = true,
        Parameters = new[]
        {
            ActionHelpers.UrlInput("url", Strings.Get("WebIsUp_Param_Url_Label")) with { Type = ParamType.Content },
            ParameterDescriptor.Number("timeout", Strings.Get("WebIsUp_Param_Timeout_Label"), 10, 1, 120, Strings.Get("WebIsUp_Param_Timeout_Unit")),
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
