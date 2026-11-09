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
            400 => $"{site} didn't understand the request (error 400). Check the address and anything you're sending.",
            401 => $"{site} needs you to sign in or send a key (error 401). Add it under Headers.",
            403 => $"{site} says you aren't allowed to see this (error 403).",
            404 => $"{site} says it couldn't find that page (error 404). Check the link is right.",
            405 => $"{site} doesn't accept this kind of request (error 405). Try a different method.",
            408 => $"{site} took too long to answer (error 408). Try again in a moment.",
            410 => $"That page on {site} has been removed (error 410).",
            429 => $"{site} says you're making too many requests (error 429). Wait a bit and try again.",
            500 => $"{site} had a problem on its side (error 500). Try again later.",
            502 => $"{site}'s server isn't answering properly (error 502). Try again later.",
            503 => $"{site} is temporarily unavailable (error 503). Try again later.",
            504 => $"{site}'s server took too long to answer (error 504). Try again later.",
            >= 500 => $"{site} had a problem on its side (error {code}). Try again later.",
            >= 400 => $"{site} replied with an error ({code} {status}).",
            _ => $"{site} replied with an unexpected answer ({code} {status}).",
        };
    }

    public static ActionException Unreachable(Uri url, Exception ex)
    {
        var reason = ex.InnerException?.Message ?? ex.Message;
        return ex is HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError }
            ? new ActionException($"Couldn't find {url.Host}. Check the address is spelled correctly and that you're online.", ex)
            : new ActionException($"Couldn't reach {url.Host}. Check your internet connection and the link. ({reason})", ex);
    }

    public static ActionException TimedOut(Uri url) =>
        new($"{url.Host} took too long to answer. Check your internet connection or try again later.");

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
                    ctx.ReportProgress(total is > 0 ? $"Downloaded {ContentFormat.Size(done)} of {ContentFormat.Size(total.Value)}" : $"Downloaded {ContentFormat.Size(done)}");
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
                throw new ActionException($"{url.AbsoluteUri} isn't a picture.");
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
