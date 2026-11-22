using System.Net;
using System.Text;
using System.Text.Json;

namespace AutomationPortal.Actions.Documents;

public sealed record PortalAiHostEndpoint(string BaseUrl, string? InstanceId, string? HostVersion);

public sealed record PortalAiHostReply(int Status, string Body)
{
    public bool Ok => Status is >= 200 and < 300;

    /// <summary>The <c>error.code</c> of an error answer, or null.</summary>
    public string? ErrorCode => ErrorField("code");

    public string? ErrorField(string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(Body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var e)
                && e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var c) && c.ValueKind == JsonValueKind.String) return c.GetString();
        }
        catch (JsonException) { }
        return null;
    }
}

internal static class PortalAiHostApi
{
    public static readonly Lazy<HttpClient> SharedHttp = new(() =>
        new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan });

    public static async Task<PortalAiHostReply> SendAsync(HttpClient http, HttpMethod method, string url, string? token, string? json,
        TimeSpan timeout, CancellationToken ct, string? priority = null)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        using var request = new HttpRequestMessage(method, url);
        if (token is not null) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (priority is not null) request.Headers.TryAddWithoutValidation("X-Portal-Priority", priority);
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        try
        {
            using var response = await http.SendAsync(request, cts.Token);
            var body = await response.Content.ReadAsStringAsync(cts.Token);
            return new PortalAiHostReply((int)response.StatusCode, body);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new HttpRequestException("Portal AI Host didn't answer in time.");
        }
    }
}

/// <summary>
/// Finds a running Portal AI Host: the development override, then the discovery file in the host's package folder,
/// then the default port. Every candidate is checked with <c>GET /host/ping</c>. Read-only.
/// </summary>
public sealed class PortalAiHostDiscovery
{
    private readonly HttpClient _http;
    private readonly Func<string, string?> _env;
    private readonly string? _localAppData;
    private readonly Func<Task<bool>> _installedProbe;

    public PortalAiHostDiscovery(HttpClient? http = null, Func<string, string?>? env = null, string? localAppData = null, Func<Task<bool>>? installedProbe = null)
    {
        _http = http ?? PortalAiHostApi.SharedHttp.Value;
        _env = env ?? Environment.GetEnvironmentVariable;
        _localAppData = localAppData ?? UserFolders.LocalAppData();
        _installedProbe = installedProbe ?? DefaultInstalledProbe;
    }

    internal HttpClient Http => _http;

    public async Task<PortalAiHostEndpoint?> DiscoverAsync(CancellationToken ct = default)
    {
        if (_env(PortalAiHostConstants.UrlEnvironmentVariable) is { } over && NormalizeLoopback(over) is { } overUrl
            && await PingAsync(overUrl, null, ct) is { } viaEnv)
            return viaEnv;

        foreach (var file in DiscoveryFiles())
        {
            if (ReadDiscoveryFile(file) is not { } info || NormalizeLoopback(info.BaseUrl) is not { } url) continue;
            if (await PingAsync(url, info.InstanceId, ct) is { } viaFile) return viaFile;
        }

        // A test data root only trusts the env override and its own copy of the discovery file.
        if (UserFolders.IsSandboxed) return null;
        return await PingAsync($"http://127.0.0.1:{PortalAiHostConstants.DefaultPort}", null, ct);
    }

    public Task<bool> IsInstalledAsync() => _installedProbe();

    private async Task<bool> DefaultInstalledProbe()
    {
        if (DiscoveryFiles().Any()) return true;
        if (UserFolders.IsSandboxed) return false;
        try
        {
            var support = await Windows.System.Launcher.QueryUriSupportAsync(new Uri(PortalAiHostConstants.StartUri), Windows.System.LaunchQuerySupportType.Uri);
            return support == Windows.System.LaunchQuerySupportStatus.Available;
        }
        catch (Exception) { return false; }
    }

    private IEnumerable<string> DiscoveryFiles()
    {
        var packages = Path.Combine(_localAppData ?? "", "Packages");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pfn in PortalAiHostConstants.PackageFamilyNames)
        {
            var file = DiscoveryPath(Path.Combine(packages, pfn));
            if (File.Exists(file) && seen.Add(file)) yield return file;
        }
        foreach (var dir in PackageDirectories(packages))
        {
            var file = DiscoveryPath(dir);
            if (File.Exists(file) && seen.Add(file)) yield return file;
        }
    }

    private static List<string> PackageDirectories(string packages)
    {
        try { return Directory.Exists(packages) ? Directory.EnumerateDirectories(packages).ToList() : new List<string>(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new List<string>(); }
    }

    private static string DiscoveryPath(string packageDir) => Path.Combine(packageDir, "LocalState", "discovery", "host.json");

    internal static PortalAiHostEndpoint? ReadDiscoveryFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            return ParseDiscovery(doc.RootElement);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    internal static PortalAiHostEndpoint? ParseDiscovery(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (Str(root, "service") is { } service && !service.Equals(PortalAiHostConstants.ServiceName, StringComparison.Ordinal)) return null;
        var url = Str(root, "base_url");
        if (url is null && root.TryGetProperty("port", out var port) && port.TryGetInt32(out var p)) url = $"http://127.0.0.1:{p}";
        return url is null ? null : new PortalAiHostEndpoint(url, Str(root, "instance_id"), Str(root, "host_version"));
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Only plain http to this PC is ever used, so the access token can't be sent anywhere else.</summary>
    internal static string? NormalizeLoopback(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp) return null;
        var ok = uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip));
        return ok ? $"http://{uri.Authority}" : null;
    }

    private async Task<PortalAiHostEndpoint?> PingAsync(string baseUrl, string? expectedInstance, CancellationToken ct)
    {
        try
        {
            var reply = await PortalAiHostApi.SendAsync(_http, HttpMethod.Get, baseUrl + "/host/ping", null, null, TimeSpan.FromSeconds(2), ct);
            if (!reply.Ok) return null;
            using var doc = JsonDocument.Parse(reply.Body);
            var root = doc.RootElement;
            if (Str(root, "service") != PortalAiHostConstants.ServiceName) return null;
            var instance = Str(root, "instance_id");
            if (expectedInstance is not null && !string.Equals(expectedInstance, instance, StringComparison.Ordinal)) return null;
            return new PortalAiHostEndpoint(baseUrl, instance, Str(root, "host_version"));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException) { return null; }
    }
}
