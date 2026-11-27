using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutomationPortal.Actions.Documents;

public static class AiProviderIds
{
    public const string Claude = "claude", OpenAi = "openai", Gemini = "gemini", DeepSeek = "deepseek", Kimi = "kimi",
        Azure = "azure", Ollama = "ollama", LmStudio = "lmstudio", Custom = "custom", WindowsAi = "windows-ai", PortalAiHost = "portal-ai-host";
}

public enum AiJsonMode { None, JsonObject, JsonSchema }

/// <summary>Static facts about one AI provider, for the Settings page and for building clients.</summary>
public sealed record AiProviderInfo(
    string Id, string Name, string? DefaultEndpoint, bool NeedsKey, bool NeedsEndpoint, bool UsesDeployment,
    bool SupportsImages, bool IsLocal, AiJsonMode JsonMode, string Help, bool OptionalKey = false);

public sealed record AiAvailability(bool IsAvailable, string? Reason = null)
{
    public static AiAvailability Yes { get; } = new(true);
}

public sealed record AiProviderContext(AiProviderInfo Info, AiProviderSettings Settings, string? Key, HttpClient? Http);

public interface IAiProvider
{
    string Id { get; }
    /// <summary>False (with a plain-English reason such as "Needs a Copilot+ PC") when the provider can't be used on this PC.</summary>
    Task<AiAvailability> IsAvailableAsync(CancellationToken ct = default);
    /// <summary>Sends the request and returns the reply text (JSON text for structured answers). Throws <see cref="ActionException"/> with plain words on failure.</summary>
    Task<string> CompleteAsync(string model, AiRequest request, CancellationToken ct);
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct);
}

/// <summary>
/// The providers Automation Portal knows about. Another assembly can add one at startup with <see cref="Register"/>
/// (for example "Windows AI (on this PC)"); it then shows up in the Settings list and in every AI action's "Use" choice.
/// </summary>
public static class AiProviderRegistry
{
    private static readonly object Gate = new();
    private static readonly List<(AiProviderInfo Info, Func<AiProviderContext, IAiProvider> Factory)> Entries = new();

    /// <summary>Tests set this so no provider talks to the network.</summary>
    public static Func<AiProviderContext, IAiProvider>? FactoryOverride { get; set; }

    static AiProviderRegistry()
    {
        Register(new(AiProviderIds.Claude, "Claude (Anthropic)", null, true, false, false, true, false, AiJsonMode.JsonSchema,
            Strings.Get("AiProviderRegistry_Help_GetAKeyAt")), c => new ClaudeProvider(c));
        Register(new(AiProviderIds.OpenAi, "ChatGPT (OpenAI)", "https://api.openai.com/v1", true, false, false, true, false, AiJsonMode.JsonSchema,
            Strings.Get("AiProviderRegistry_Help_GetAKeyAt2")), c => new OpenAiProvider(c));
        Register(new(AiProviderIds.Gemini, "Google Gemini", "https://generativelanguage.googleapis.com/v1beta/openai/", true, false, false, true, false, AiJsonMode.JsonSchema,
            Strings.Get("AiProviderRegistry_Help_GetAFreeKey")), c => new OpenAiProvider(c));
        Register(new(AiProviderIds.DeepSeek, "DeepSeek", "https://api.deepseek.com/v1", true, false, false, false, false, AiJsonMode.JsonObject,
            Strings.Get("AiProviderRegistry_Help_GetAKeyAt3")), c => new OpenAiProvider(c));
        Register(new(AiProviderIds.Kimi, "Kimi (Moonshot AI)", "https://api.moonshot.ai/v1", true, false, false, true, false, AiJsonMode.JsonObject,
            Strings.Get("AiProviderRegistry_Help_GetAKeyAt4")), c => new OpenAiProvider(c));
        Register(new(AiProviderIds.Azure, Strings.Get("AiProviderRegistry_Name_MicrosoftAzureOpenAICopilot"), null, true, true, true, true, false, AiJsonMode.JsonSchema,
            Strings.Get("AiProviderRegistry_Help_MicrosoftCopilotHasNo")),
            c => new OpenAiProvider(c));
        Register(new(AiProviderIds.Ollama, Strings.Get("AiProviderRegistry_Name_OllamaOnThisPC"), "http://localhost:11434/v1", false, false, false, true, true, AiJsonMode.JsonSchema,
            Strings.Get("AiProviderRegistry_Help_FreeAndPrivateInstall")), c => new OpenAiProvider(c));
        Register(new(AiProviderIds.LmStudio, Strings.Get("AiProviderRegistry_Name_LMStudioOnThis"), "http://localhost:1234/v1", false, false, false, true, true, AiJsonMode.JsonSchema,
            Strings.Get("AiProviderRegistry_Help_FreeAndPrivateStart")), c => new OpenAiProvider(c));
        Register(new(AiProviderIds.Custom, "Custom (OpenAI-compatible)", null, false, true, false, true, false, AiJsonMode.JsonObject,
            Strings.Get("AiProviderRegistry_Help_AnyServiceThatSpeaks"), OptionalKey: true), c => new OpenAiProvider(c));
        Register(PortalAiHostProvider.Info, c => new PortalAiHostProvider(c));
    }

    public static void Register(AiProviderInfo info, Func<AiProviderContext, IAiProvider> factory)
    {
        lock (Gate)
        {
            Entries.RemoveAll(e => e.Info.Id.Equals(info.Id, StringComparison.OrdinalIgnoreCase));
            Entries.Add((info, factory));
        }
    }

    public static IReadOnlyList<AiProviderInfo> All { get { lock (Gate) return Entries.Select(e => e.Info).ToList(); } }

    public static AiProviderInfo? Get(string id)
    {
        lock (Gate) return Entries.FirstOrDefault(e => e.Info.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).Info;
    }

    public static IAiProvider Create(AiProviderContext context)
    {
        if (FactoryOverride is { } o) return o(context);
        Func<AiProviderContext, IAiProvider> f;
        lock (Gate)
            f = Entries.FirstOrDefault(e => e.Info.Id.Equals(context.Info.Id, StringComparison.OrdinalIgnoreCase)).Factory
                ?? throw new ActionException(Strings.Format("AiProviderRegistry_Error_DonTKnowThe", context.Info.Id));
        return f(context);
    }
}

public sealed class AiProviderSettings
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    /// <summary>The default model for this provider (editable). For Claude: empty, or fast / smart / best, or a model id.</summary>
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("endpoint")] public string? Endpoint { get; set; }
    /// <summary>Azure OpenAI deployment name.</summary>
    [JsonPropertyName("deployment")] public string? Deployment { get; set; }
}

public sealed class AiProviderConfig
{
    [JsonPropertyName("defaultProvider")] public string DefaultProvider { get; set; } = AiProviderIds.Claude;
    /// <summary>True once the user has picked the default provider. Until then Portal AI Host is used when it is ready.</summary>
    [JsonPropertyName("defaultChosen")] public bool DefaultChosen { get; set; }
    [JsonPropertyName("providers")] public Dictionary<string, AiProviderSettings> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record AiTestResult(bool Ok, string Message);

/// <summary>
/// Provider settings in %APPDATA%\Automation Portal\ai-providers.json; API keys in the secret store as "ai.&lt;providerId&gt;.apiKey"
/// (the old "ai.apiKey" is still read as the Claude key).
/// </summary>
public sealed class AiProviderStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly Dictionary<string, (DateTime At, IReadOnlyList<string> Models)> ModelCache = new();

    private readonly string _path;
    private readonly ISecretStore _secrets;
    private readonly Func<HttpClient?> _http;
    private AiProviderConfig? _config;

    public AiProviderStore(string path, ISecretStore secrets, Func<HttpClient?>? http = null)
    {
        _path = path; _secrets = secrets; _http = http ?? (() => null);
    }

    public static AiProviderStore For(AutomationPortalServices services) =>
        new(Path.Combine(services.Paths.RoamingRoot, "ai-providers.json"), services.Secrets);

    public static string KeyName(string providerId) => $"ai.{providerId}.apiKey";

    public AiProviderConfig Config
    {
        get
        {
            if (_config is not null) return _config;
            try { _config = File.Exists(_path) ? JsonSerializer.Deserialize<AiProviderConfig>(File.ReadAllText(_path), Json) : null; }
            catch (Exception ex) when (ex is JsonException or IOException) { _config = null; }
            _config ??= new AiProviderConfig();
            _config.Providers = new Dictionary<string, AiProviderSettings>(_config.Providers ?? new(), StringComparer.OrdinalIgnoreCase);
            return _config;
        }
    }

    public void Update(Action<AiProviderConfig> change)
    {
        change(Config);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(Config, Json));
    }

    public string DefaultProviderId
    {
        get
        {
            var configured = AiProviderRegistry.Get(Config.DefaultProvider) is { } p ? p.Id : AiProviderIds.Claude;
            return !HasPickedDefault && HostIsReady() ? AiProviderIds.PortalAiHost : configured;
        }
    }

    /// <summary>The user chose a default provider (or the file already names one other than the built-in Claude).</summary>
    private bool HasPickedDefault => Config.DefaultChosen || !string.Equals(Config.DefaultProvider, AiProviderIds.Claude, StringComparison.OrdinalIgnoreCase);

    private bool HostIsReady() => PortalAiHostStatus.ReadyHint && HasHostToken;

    public bool HasHostToken => GetKey(AiProviderIds.PortalAiHost) is not null;

    /// <summary>The Portal AI Host access flow, using this store's secret store and HTTP client.</summary>
    public PortalAiHostSession Host => new(_http(), _secrets);

    /// <summary>The default provider, after a quick look at the host when it could be the unpicked default.</summary>
    public async Task<string> ResolveDefaultAsync(CancellationToken ct = default)
    {
        if (!HasPickedDefault && HasHostToken)
        {
            try { await Host.GetStateAsync(ct); }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested) { }
        }
        return DefaultProviderId;
    }

    public AiProviderSettings Get(string id)
    {
        if (Config.Providers.TryGetValue(id, out var s)) return s;
        var info = AiProviderRegistry.Get(id);
        return new AiProviderSettings
        {
            // Claude counts as on when it has a key (including the key saved by older versions).
            Enabled = id == AiProviderIds.Claude && !string.IsNullOrWhiteSpace(GetKey(id)),
            Endpoint = info?.DefaultEndpoint,
        };
    }

    public string? GetKey(string id)
    {
        var key = _secrets.Get(KeyName(id));
        if (string.IsNullOrWhiteSpace(key) && id == AiProviderIds.Claude) key = _secrets.Get("ai.apiKey");
        return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    }

    public void SetKey(string id, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) _secrets.Remove(KeyName(id));
        else _secrets.Set(KeyName(id), key.Trim());
    }

    public string EffectiveEndpoint(string id)
    {
        var s = Get(id);
        return !string.IsNullOrWhiteSpace(s.Endpoint) ? s.Endpoint!.Trim() : AiProviderRegistry.Get(id)?.DefaultEndpoint ?? "";
    }

    /// <summary>Why this provider can't be used yet (plain English), or null when it is ready.</summary>
    public string? SetupProblem(string id)
    {
        var info = AiProviderRegistry.Get(id) ?? throw new ActionException(Strings.Format("AiProviderRegistry_Error_DonTKnowThe", id));
        var s = Get(id);
        if (id == AiProviderIds.Claude)
            return GetKey(id) is null ? Strings.Get("AiProviderStore_Message_AddYourAIKey") : null;
        if (id == AiProviderIds.PortalAiHost)
            return GetKey(id) is null ? Strings.Get("AiProviderStore_Message_AllowAutomationPortalIn") : null;
        if (!s.Enabled) return Strings.Format("AiProviderStore_Message_TurnOnInSettings", info.Name);
        if (info.NeedsKey && GetKey(id) is null) return Strings.Format("AiProviderStore_Message_AddYourKeyIn", info.Name);
        if (info.NeedsEndpoint && string.IsNullOrWhiteSpace(EffectiveEndpoint(id))) return Strings.Format("AiProviderStore_Message_EnterTheAddressFor", info.Name);
        if (info.UsesDeployment && string.IsNullOrWhiteSpace(s.Deployment)) return Strings.Format("AiProviderStore_Message_EnterTheDeploymentName", info.Name);
        return null;
    }

    public bool IsConfigured(string id) => SetupProblem(id) is null;

    public IReadOnlyList<AiProviderInfo> ConfiguredProviders() => AiProviderRegistry.All.Where(p => IsConfigured(p.Id)).ToList();

    public AiProviderContext ContextFor(string id)
    {
        var info = AiProviderRegistry.Get(id) ?? throw new ActionException(Strings.Format("AiProviderRegistry_Error_DonTKnowThe", id));
        var s = Get(id);
        var copy = new AiProviderSettings { Enabled = s.Enabled, Model = s.Model, Deployment = s.Deployment, Endpoint = EffectiveEndpoint(id) };
        return new AiProviderContext(info, copy, GetKey(id), _http());
    }

    public IAiProvider Create(string id)
    {
        var provider = AiProviderRegistry.Create(ContextFor(id));
        if (provider is PortalAiHostProvider host) host.OnTokenRejected = () => SetKey(AiProviderIds.PortalAiHost, null);
        return provider;
    }

    /// <summary>The model ids the provider offers, from its /models list (cached for 10 minutes).</summary>
    public async Task<IReadOnlyList<string>> ListModelsAsync(string id, CancellationToken ct = default, bool refresh = false)
    {
        var ctx = ContextFor(id);
        var cacheKey = $"{id}|{ctx.Settings.Endpoint}|{ctx.Settings.Deployment}";
        lock (ModelCache)
            if (!refresh && ModelCache.TryGetValue(cacheKey, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromMinutes(10)) return hit.Models;
        var models = await Create(id).ListModelsAsync(ct);
        lock (ModelCache) ModelCache[cacheKey] = (DateTime.UtcNow, models);
        return models;
    }

    /// <summary>Checks that the provider can be reached with the saved settings (for the Settings "Test" button).</summary>
    public async Task<AiTestResult> TestConnectionAsync(string id, CancellationToken ct = default)
    {
        var info = AiProviderRegistry.Get(id);
        if (info is null) return new AiTestResult(false, Strings.Format("AiProviderRegistry_Error_DonTKnowThe", id));
        if (SetupProblem(id) is { } problem) return new AiTestResult(false, problem);
        try
        {
            var provider = Create(id);
            var availability = await provider.IsAvailableAsync(ct);
            if (!availability.IsAvailable) return new AiTestResult(false, availability.Reason ?? Strings.Format("AiProviderStore_Message_IsnTAvailableOn", info.Name));
            var models = await ListModelsAsync(id, ct, refresh: true);
            return new AiTestResult(true, models.Count > 0 ? Strings.Format("AiProviderStore_Message_ConnectedToModelAvailable", info.Name, models.Count, (models.Count == 1 ? "" : "s")) : Strings.Format("AiProviderStore_Message_ConnectedTo", info.Name));
        }
        catch (ActionException ex) { return new AiTestResult(false, ex.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new AiTestResult(false, AiErrors.Map(ex, info).Message); }
    }

    /// <summary>Picks a sensible chat model from a /models list (skips embedding, speech and image models).</summary>
    public static string? SuggestModel(IEnumerable<string> models) =>
        models.FirstOrDefault(m => !new[] { "embed", "whisper", "tts", "dall", "image", "moderation", "audio", "realtime", "transcribe", "rerank" }
            .Any(w => m.Contains(w, StringComparison.OrdinalIgnoreCase)));
}
