using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AutomationPortal.Core;

namespace AutomationPortal.Actions.Content.Utilities;

internal static class HttpHelpers
{
    /// <summary>A non-success status in plain words.</summary>
    public static string StatusMessage(HttpStatusCode status, Uri url)
    {
        var code = (int)status;
        var site = url.Host;
        return code switch
        {
            400 => Strings.Format("HttpHelpers_Message_0DidnTUnderstand", site),
            401 => Strings.Format("HttpHelpers_Message_0NeedsYouTo", site),
            403 => Strings.Format("HttpHelpers_Message_0SaysYouAren", site),
            404 => Strings.Format("HttpHelpers_Message_0SaysItCouldn", site),
            405 => Strings.Format("HttpHelpers_Message_0DoesnTAccept", site),
            408 => Strings.Format("HttpHelpers_Message_0TookTooLong", site),
            410 => Strings.Format("HttpHelpers_Message_ThatPageOn0", site),
            429 => Strings.Format("HttpHelpers_Message_0SaysYouRe", site),
            500 => Strings.Format("HttpHelpers_Message_0HadAProblem", site),
            502 => Strings.Format("HttpHelpers_Message_0SServerIsn", site),
            503 => Strings.Format("HttpHelpers_Message_0IsTemporarilyUnavailable", site),
            504 => Strings.Format("HttpHelpers_Message_0SServerTook", site),
            >= 500 => Strings.Format("HttpHelpers_Message_0HadAProblem2", site, code),
            >= 400 => Strings.Format("HttpHelpers_Message_0RepliedWithAn", site, code, status),
            _ => Strings.Format("HttpHelpers_Message_0RepliedWithAn2", site, code, status),
        };
    }

    public static ActionException Unreachable(Uri url, Exception ex)
    {
        var reason = ex.InnerException?.Message ?? ex.Message;
        return ex is HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError }
            ? new ActionException(Strings.Format("HttpHelpers_Error_CouldnTFind0", url.Host), ex)
            : new ActionException(Strings.Format("HttpHelpers_Error_CouldnTReach0", url.Host, reason), ex);
    }

    public static ActionException TimedOut(Uri url) =>
        new(Strings.Format("HttpHelpers_Error_TimedOut", url.Host));

    /// <summary>Runs a web request, turning network failures into friendly messages.</summary>
    public static async Task<T> GuardAsync<T>(Uri url, CancellationToken userToken, Func<Task<T>> work)
    {
        try { return await work().ConfigureAwait(false); }
        catch (HttpRequestException ex) { throw Unreachable(url, ex); }
        catch (TaskCanceledException) when (!userToken.IsCancellationRequested) { throw TimedOut(url); }
        catch (IOException ex) { throw Unreachable(url, ex); }
    }

    public static async Task<string> GetStringAsync(ActionContext ctx, Uri url, string? accept = null)
    {
        return await GuardAsync(url, ctx.CancellationToken, async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (accept is not null) request.Headers.TryAddWithoutValidation("Accept", accept);
            using var response = await ctx.Services.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ctx.CancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new ActionException(StatusMessage(response.StatusCode, url));
            return await ReadTextAsync(response.Content, ctx.CancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public static async Task<string> ReadTextAsync(HttpContent content, CancellationToken ct)
    {
        try { return await content.ReadAsStringAsync(ct).ConfigureAwait(false); }
        catch (InvalidOperationException)
        {
            // unknown charset name: fall back to UTF-8
            return Encoding.UTF8.GetString(await content.ReadAsByteArrayAsync(ct).ConfigureAwait(false));
        }
    }

    public static string FileNameFor(HttpResponseMessage response, Uri url)
    {
        var cd = response.Content.Headers.ContentDisposition;
        var name = cd?.FileNameStar ?? cd?.FileName?.Trim('"');
        if (string.IsNullOrWhiteSpace(name))
        {
            var last = Uri.UnescapeDataString(Path.GetFileName(url.AbsolutePath.TrimEnd('/')));
            name = string.IsNullOrWhiteSpace(last) ? "download" : last;
        }
        name = ActionContext.SanitizeFileName(name);
        if (name.Length == 0) name = "download";
        if (!Path.HasExtension(name) && response.Content.Headers.ContentType?.MediaType is { } mt)
            name += ExtensionFor(mt);
        return name;
    }

    public static string ExtensionFor(string mediaType) => mediaType.ToLowerInvariant() switch
    {
        "image/png" => ".png", "image/jpeg" => ".jpg", "image/gif" => ".gif", "image/webp" => ".webp", "image/bmp" => ".bmp",
        "image/svg+xml" => ".svg", "image/tiff" => ".tif", "image/x-icon" or "image/vnd.microsoft.icon" => ".ico",
        "application/pdf" => ".pdf", "application/zip" => ".zip", "text/html" => ".html", "text/plain" => ".txt", "text/csv" => ".csv",
        "application/json" => ".json", "application/xml" or "text/xml" => ".xml",
        "audio/mpeg" => ".mp3", "video/mp4" => ".mp4",
        _ => "",
    };

    /// <summary>Saves the response body into <paramref name="folder"/> and returns the new path.</summary>
    public static async Task<string> SaveResponseAsync(ActionContext ctx, HttpResponseMessage response, Uri url, string folder, string? fileName, bool uniqueName = true)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? FileNameFor(response, url) : ActionContext.SanitizeFileName(fileName);
        TestRootGuard.Check(folder);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        if (uniqueName)
        {
            var stem = Path.GetFileNameWithoutExtension(name); var ext = Path.GetExtension(name);
            for (var n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"{stem} ({n}){ext}");
        }
        var total = response.Content.Headers.ContentLength;
        long done = 0, lastReport = 0;
        await using var source = await response.Content.ReadAsStreamAsync(ctx.CancellationToken).ConfigureAwait(false);
        // CreateNew: never replaces an existing file, so the cleanup below only ever deletes a file this call created.
        var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        int read;
        try
        {
            while ((read = await source.ReadAsync(buffer, ctx.CancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ctx.CancellationToken).ConfigureAwait(false);
                done += read;
                if (done - lastReport > 512 * 1024)
                {
                    lastReport = done;
                    ctx.ReportProgress(total is > 0 ? Strings.Format("HttpHelpers_Progress_Downloaded0Of1", ContentFormat.Size(done), ContentFormat.Size(total.Value)) : Strings.Format("HttpHelpers_Progress_Downloaded0", ContentFormat.Size(done)));
                }
            }
            await target.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            await target.DisposeAsync();
            try { File.Delete(path); } catch { }
            throw;
        }
        MarkFromInternet(path, response.RequestMessage?.RequestUri ?? url);
        return path;
    }

    /// <summary>
    /// Adds the Mark of the Web (a Zone.Identifier stream saying "Internet zone" and where the file came from), the same mark a browser
    /// adds. Windows SmartScreen, Office Protected View and script hosts then treat the file as downloaded. Best effort: drives without
    /// alternate data streams (FAT32, exFAT, some network shares) simply don't get the mark.
    /// </summary>
    internal static void MarkFromInternet(string path, Uri source)
    {
        try { File.WriteAllText(path + ":Zone.Identifier", ZoneIdentifierFor(source), Encoding.ASCII); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { }
    }

    internal static string ZoneIdentifierFor(Uri source)
    {
        var host = source.IsAbsoluteUri && (source.Scheme == Uri.UriSchemeHttp || source.Scheme == Uri.UriSchemeHttps)
            // Drop any user name or password, and keep the line free of breaks so it can't add its own keys.
            ? source.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped).Replace("\r", "").Replace("\n", "")
            : null;
        return "[ZoneTransfer]\r\nZoneId=3\r\n" + (host is null ? "" : "HostUrl=" + host + "\r\n");
    }

    public static async Task<PathItem> DownloadAsync(ActionContext ctx, Uri url, string folder, string? fileName = null, bool imagesOnly = false)
    {
        return await GuardAsync(url, ctx.CancellationToken, async () =>
        {
            using var response = await ctx.Services.Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ctx.CancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new ActionException(StatusMessage(response.StatusCode, url));
            if (imagesOnly && response.Content.Headers.ContentType?.MediaType is { } mt && !mt.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                throw new ActionException(Strings.Format("HttpHelpers_Error_0IsnTA", url.AbsoluteUri));
            var path = await SaveResponseAsync(ctx, response, url, folder, fileName, uniqueName: true).ConfigureAwait(false);
            return ContentValue.Files(new[] { path })[0] as PathItem ?? new FileItem(path);
        }).ConfigureAwait(false);
    }

    private static readonly Regex MetaRefreshRx = new(@"<meta[^>]+http-equiv\s*=\s*[""']?refresh[""']?[^>]*content\s*=\s*[""']\s*\d+\s*;\s*url\s*=\s*['""]?([^'"">\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static Uri? FindMetaRefresh(string html, Uri baseUrl)
    {
        var m = MetaRefreshRx.Match(html.Length > 16384 ? html[..16384] : html);
        return m.Success && Uri.TryCreate(baseUrl, WebUtility.HtmlDecode(m.Groups[1].Value), out var u) ? u : null;
    }
}
