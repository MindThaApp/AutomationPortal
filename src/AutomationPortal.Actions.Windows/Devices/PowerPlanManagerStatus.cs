using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Flowmate.Actions.Windows.Devices;

/// <summary>Talks to Power Plan Manager's read-only status pipe. Tests use a fake so the real pipe is never touched.</summary>
public interface IPpmStatusClient
{
    /// <summary>Sends one request line and returns the JSON reply, or null when Power Plan Manager isn't running (no pipe).
    /// Throws <see cref="TimeoutException"/> when it doesn't answer, and <see cref="ActionException"/> when the pipe's server isn't Power Plan Manager.</summary>
    Task<string?> RequestAsync(string request, CancellationToken ct);
}

public sealed record PpmStatus(string? PlanName, string? PlanGuid, string? Preset, int? ChargeLimit, string? ChargingState,
    bool KeepAwake, bool AllowExternalLinks, string? Edition);

internal static class PpmPipe
{
    public static IPpmStatusClient Client { get; set; } = new NamedPipeStatusClient();

    public const string NotRunning = "Power Plan Manager isn't running. Open it and try again.";
    public const string LinksOff = "Turn on 'Allow links from other apps' in Power Plan Manager (Settings › General) so Flow Mate can control it.";

    public static async Task<string> RequireAsync(string request, CancellationToken ct)
    {
        try { return await Client.RequestAsync(request, ct).ConfigureAwait(false) ?? throw new ActionException(NotRunning); }
        catch (TimeoutException) { throw new ActionException("Power Plan Manager didn't answer in time. Try again in a moment."); }
    }

    public static async Task<PpmStatus> GetStatusAsync(CancellationToken ct) => ParseStatus(await RequireAsync("GET STATUS", ct).ConfigureAwait(false));

    public static async Task<IReadOnlyList<string>> GetPresetsAsync(CancellationToken ct) => ParsePresets(await RequireAsync("GET PRESETS", ct).ConfigureAwait(false));

    /// <summary>Before sending a link: refuses when the app answers and has links turned off. No pipe or no answer means send anyway.</summary>
    public static async Task EnsureLinksAllowedAsync(CancellationToken ct)
    {
        string? json;
        try { json = await Client.RequestAsync("GET STATUS", ct).ConfigureAwait(false); }
        catch (TimeoutException) { return; }
        if (json is null) return;
        if (!ParseStatus(json).AllowExternalLinks) throw new ActionException(LinksOff);
    }

    public static PpmStatus ParseStatus(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            string? plan = null, guid = null;
            if (r.TryGetProperty("plan", out var p) && p.ValueKind == JsonValueKind.Object)
            {
                plan = Str(p, "name"); guid = Str(p, "guid");
            }
            int? limit = r.TryGetProperty("chargeLimit", out var l) && l.ValueKind == JsonValueKind.Number && l.TryGetInt32(out var li) ? li : null;
            return new PpmStatus(plan, guid, Str(r, "preset"), limit, Str(r, "chargingState"),
                Bool(r, "keepAwake"), Bool(r, "allowExternalLinks"), Str(r, "edition"));
        }
        catch (JsonException) { throw new ActionException("Power Plan Manager sent a reply Flow Mate couldn't read."); }
    }

    public static IReadOnlyList<string> ParsePresets(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("presets", out var a) || a.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            return a.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : Str(e, "name"))
                .Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList();
        }
        catch (JsonException) { throw new ActionException("Power Plan Manager sent a reply Flow Mate couldn't read."); }
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    // Package names of the Store app and its Lite edition. A package family name is "<Name>_<PublisherId>".
    private static readonly System.Text.RegularExpressions.Regex PackageName =
        new(@"^34458MindtheApp\.PowerPlanManager([.\-]?Lite)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// The pipe's server must be the packaged Power Plan Manager (full or Lite edition, including its tray helper): either its process
    /// belongs to that package family, or its program file sits in that package's folder under Program Files\WindowsApps, which only
    /// Windows can write to. A program that merely has "PowerPlanManager" somewhere in its path is not enough.
    /// </summary>
    public static bool IsTrustedServer(string? exePath, string? packageFamily, string? windowsAppsFolder = null)
    {
        if (packageFamily is { Length: > 0 })
        {
            var us = packageFamily.LastIndexOf('_');
            if (us > 0 && us < packageFamily.Length - 1 && PackageName.IsMatch(packageFamily[..us])) return true;
        }
        if (exePath is not { Length: > 0 }) return false;
        windowsAppsFolder ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        string full, root;
        try { full = Path.GetFullPath(exePath); root = Path.GetFullPath(windowsAppsFolder).TrimEnd('\\') + "\\"; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
        var rest = full[root.Length..];
        var slash = rest.IndexOf('\\');
        if (slash <= 0) return false;
        // Package folder: <Name>_<Version>_<Arch>_<ResourceId>_<PublisherId>
        var folder = rest[..slash];
        var nameEnd = folder.IndexOf('_');
        return nameEnd > 0 && PackageName.IsMatch(folder[..nameEnd]);
    }
}

internal sealed class NamedPipeStatusClient : IPpmStatusClient
{
    [DllImport("kernel32.dll")] private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint pid);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageNameW(IntPtr h, uint flags, StringBuilder name, ref uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackageFamilyName(IntPtr h, ref uint length, StringBuilder? name);

    public async Task<string?> RequestAsync(string request, CancellationToken ct)
    {
        // Identification only: whoever owns the pipe can see who is asking, but can never act as this user.
        using var pipe = new NamedPipeClientStream(".", "PowerPlanManager.Status", PipeDirection.InOut, PipeOptions.Asynchronous,
            System.Security.Principal.TokenImpersonationLevel.Identification);
        try { await pipe.ConnectAsync(500, ct).ConfigureAwait(false); }
        catch (TimeoutException) { return null; }
        catch (IOException) { return null; }

        if (!ServerIsPowerPlanManager(pipe))
            throw new ActionException("Something other than Power Plan Manager is answering on its status channel, so Flow Mate won't talk to it.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var bytes = Encoding.UTF8.GetBytes(request + "\n");
            await pipe.WriteAsync(bytes, cts.Token).ConfigureAwait(false);
            await pipe.FlushAsync(cts.Token).ConfigureAwait(false);
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
            return await reader.ReadLineAsync(cts.Token).ConfigureAwait(false) ?? throw new TimeoutException();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException(); }
    }

    private static bool ServerIsPowerPlanManager(NamedPipeClientStream pipe)
    {
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid)) return false;
        var h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return false;
        try
        {
            string? path = null, family = null;
            var name = new StringBuilder(1024);
            var size = (uint)name.Capacity;
            if (QueryFullProcessImageNameW(h, 0, name, ref size)) path = name.ToString();
            uint len = 0;
            if (GetPackageFamilyName(h, ref len, null) == 122 /* ERROR_INSUFFICIENT_BUFFER */ && len > 0)
            {
                var pfn = new StringBuilder((int)len);
                if (GetPackageFamilyName(h, ref len, pfn) == 0) family = pfn.ToString();
            }
            return PpmPipe.IsTrustedServer(path, family);
        }
        finally { CloseHandle(h); }
    }
}

public sealed class GetPpmStatusAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "ppm.status", Title = "Get Power Plan Manager Status", Category = ActionCategories.DeviceAndSettings, Icon = "Flash24",
        Keywords = new[] { "power plan manager", "ppm", "charge limit", "preset", "charging" },
        Summary = "Get the Power Plan Manager status",
        Description = "Reads the current plan, preset, charge limit, charging state and Keep Awake from Power Plan Manager, which must be running. Pick a field from the result, such as Plan, Preset, Charge Limit or Charging State. Fields are empty when 'Allow links from other apps' is off.",
        OutputKind = ContentKind.Any, OutputName = "Power Plan Manager Status",
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var s = await PpmPipe.GetStatusAsync(ctx.CancellationToken).ConfigureAwait(false);
        static ContentValue T(string? v) => v is null ? ContentValue.Empty : ContentValue.Text(v);
        return ContentValue.Dict(
            ("Plan", T(s.PlanName)),
            ("Plan ID", T(s.PlanGuid)),
            ("Preset", T(s.Preset)),
            ("Charge Limit", s.ChargeLimit is { } l ? ContentValue.Number(l) : ContentValue.Empty),
            ("Charging State", T(s.ChargingState)),
            ("Keep Awake", ContentValue.Bool(s.KeepAwake)),
            ("Allow Links", ContentValue.Bool(s.AllowExternalLinks)),
            ("Edition", T(s.Edition)));
    }
}

public sealed class GetPpmPresetsAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "ppm.presets", Title = "Get Power Plan Manager Presets", Category = ActionCategories.DeviceAndSettings, Icon = "Flash24",
        Keywords = new[] { "power plan manager", "ppm", "presets" },
        Summary = "Get the Power Plan Manager presets",
        Description = "Gives back the names of the presets in Power Plan Manager, which must be running. The list is empty on the Lite edition.",
        OutputKind = ContentKind.Text, OutputName = "Presets",
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx) =>
        ContentValue.Texts(await PpmPipe.GetPresetsAsync(ctx.CancellationToken).ConfigureAwait(false));
}
