using System.Text.RegularExpressions;
using AutomationPortal.Actions.Documents;
using AutomationPortal.Core;

namespace AutomationPortal.Ai.Windows;

/// <summary>
/// "Windows AI (on this PC)": Phi Silica and Windows AI imaging through the Windows App SDK. Free, private, offline.
/// Only works in the Microsoft Store (MSIX) build on a Copilot+ PC; everywhere else <see cref="IsAvailableAsync"/> says why not,
/// and none of the Windows App SDK is touched (so the unpackaged build never loads it).
/// </summary>
public sealed class WindowsAiProvider : IAiProvider
{
    public static string UnpackagedReason => Strings.Get("WindowsAiProvider_Reason_Unpackaged");
    public static string NotSupportedReason => Strings.Get("WindowsAiProvider_Reason_NotSupported");
    public static string NotReadyReason => Strings.Get("WindowsAiProvider_Reason_NotReady");
    public static string DisabledReason => Strings.Get("WindowsAiProvider_Reason_Disabled");
    public const string ModelName = "Phi Silica";

    public static AiProviderInfo Info { get; } = new(
        AiProviderIds.WindowsAi, Strings.Get("WindowsAiProvider_DisplayName"), null, NeedsKey: false, NeedsEndpoint: false, UsesDeployment: false,
        SupportsImages: true, IsLocal: true, JsonMode: AiJsonMode.None,
        Help: Strings.Get("WindowsAiProvider_Help"));

    private readonly Func<bool> _isPackaged;
    private readonly Lazy<IWindowsAiBackend> _backend;

    public WindowsAiProvider() : this(() => PackageIdentity.IsPackaged, () => new PhiSilicaBackend()) { }

    internal WindowsAiProvider(Func<bool> isPackaged, Func<IWindowsAiBackend> backend)
    {
        _isPackaged = isPackaged;
        _backend = new Lazy<IWindowsAiBackend>(backend);
    }

    /// <summary>Adds "Windows AI (on this PC)" to the provider list (Settings › AI and every AI action's "Use" choice). Call once at startup.</summary>
    public static void Register() => AiProviderRegistry.Register(Info, _ => new WindowsAiProvider());

    public string Id => AiProviderIds.WindowsAi;

    // ---- availability -----------------------------------------------------------------------------

    public Task<AiAvailability> IsAvailableAsync(CancellationToken ct = default)
    {
        if (!_isPackaged()) return Task.FromResult(new AiAvailability(false, UnpackagedReason));
        return Task.Run(() => Describe(SafeState()), ct);
    }

    private WindowsAiState SafeState()
    {
        try { return _backend.Value.GetReadyState(); }
        catch (Exception) { return WindowsAiState.Unavailable; }   // missing runtime, class not registered, old Windows …
    }

    internal static AiAvailability Describe(WindowsAiState state) => state switch
    {
        WindowsAiState.Ready => AiAvailability.Yes,
        WindowsAiState.NotReady => new AiAvailability(false, NotReadyReason),
        WindowsAiState.DisabledByUser => new AiAvailability(false, DisabledReason),
        _ => new AiAvailability(false, NotSupportedReason),
    };

    /// <summary>True when the reason is one the user can fix by letting Windows finish setup (offer a "Get it ready" button).</summary>
    public static bool CanGetReady(AiAvailability a) => a.Reason == NotReadyReason;

    /// <summary>Asks Windows to download and set up the model. Returns null on success, or a plain-English problem.</summary>
    public async Task<string?> EnsureReadyAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!_isPackaged()) return UnpackagedReason + ".";
        try { return await Task.Run(() => _backend.Value.EnsureReadyAsync(progress, ct), ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return NotSupportedReason + "."; }
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) =>
        (await IsAvailableAsync(ct)).IsAvailable ? new[] { ModelName } : Array.Empty<string>();

    // ---- answering --------------------------------------------------------------------------------

    public async Task<string> CompleteAsync(string model, AiRequest request, CancellationToken ct)
    {
        var availability = await IsAvailableAsync(ct);
        if (!availability.IsAvailable) throw new ActionException(availability.Reason + ".");
        var backend = _backend.Value;
        try
        {
            return await Task.Run(() => Answer(backend, request, ct), ct);
        }
        catch (ActionException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { throw new ActionException(Strings.Get("WindowsAiProvider_Error_Problem"), ex); }
    }

    private static async Task<string> Answer(IWindowsAiBackend backend, AiRequest request, CancellationToken ct)
    {
        var images = request.Parts.OfType<AiPart.Image>().ToList();
        var text = string.Join("\n\n", request.Parts.OfType<AiPart.Text>().Select(t => t.Value));
        var intent = request.Intent ?? "";

        if (images.Count > 0) return await AnswerAboutImage(backend, request, images[0], text, intent, ct);

        if (intent.StartsWith("summarize:", StringComparison.Ordinal) && Unwrap(text) is { Length: > 0 } toSummarize
            && intent is "summarize:bullets" or "summarize:medium")
        {
            var summary = await backend.SummarizeAsync(toSummarize, bullets: intent == "summarize:bullets", ct);
            if (!string.IsNullOrWhiteSpace(summary)) return summary;
        }
        else if (intent.StartsWith("rewrite:", StringComparison.Ordinal) && Unwrap(text) is { Length: > 0 } toRewrite
            && ToneFor(intent) is { } tone)
        {
            var rewritten = await backend.RewriteAsync(toRewrite, tone, ct);
            if (!string.IsNullOrWhiteSpace(rewritten)) return rewritten;
        }

        var system = request.System;
        if (request.Shape != AiShape.Text)
        {
            system += "\nReply with only a single JSON object and nothing else.";
            if (request.SchemaJson is { Length: > 0 } schema) system += "\nThe JSON must follow this JSON schema: " + schema;
        }
        return await backend.GenerateAsync(system, text, ct);
    }

    private static async Task<string> AnswerAboutImage(IWindowsAiBackend backend, AiRequest request, AiPart.Image image, string instruction, string intent, CancellationToken ct)
    {
        if (intent == "describeImage:text")
            throw new ActionException(Strings.Get("WindowsAiProvider_Error_NoImageText"));
        var detail = intent switch { "describeImage:detailed" => "detailed", "describeImage:alt" => "alt", _ => intent.StartsWith("describeImage:") ? "short" : "detailed" };
        var description = await backend.DescribeImageAsync(image.Data, detail, ct);
        if (string.IsNullOrWhiteSpace(description))
            throw new ActionException(Strings.Get("WindowsAiProvider_Error_NoDescription"));
        if (intent.StartsWith("describeImage:", StringComparison.Ordinal)) return description;

        // A free-form question about the picture: answer it from the description.
        var prompt = $"Description of a picture:\n{description}\n\nUsing only that description, {instruction.Trim()}";
        return await backend.GenerateAsync(request.System, prompt, ct);
    }

    /// <summary>The text between the &lt;text&gt; tags the prompt builders wrap content in.</summary>
    internal static string Unwrap(string prompt)
    {
        var m = Regex.Match(prompt, @"<text>\r?\n(?<t>.*)\r?\n</text>", RegexOptions.Singleline);
        return m.Success ? m.Groups["t"].Value.Trim() : "";
    }

    internal static RewriteTone? ToneFor(string intent) => intent switch
    {
        "rewrite:professional" => RewriteTone.Formal,
        "rewrite:friendlier" => RewriteTone.Casual,
        "rewrite:shorter" => RewriteTone.Concise,
        _ => null,
    };
}
