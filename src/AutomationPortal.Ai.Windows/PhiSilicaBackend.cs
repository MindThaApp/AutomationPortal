using AutomationPortal.Core;
using Microsoft.Graphics.Imaging;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.ContentSafety;
using Microsoft.Windows.AI.Imaging;
using Microsoft.Windows.AI.Text;
using WinGraphics = global::Windows.Graphics.Imaging;
using WinStreams = global::Windows.Storage.Streams;

namespace AutomationPortal.Ai.Windows;

/// <summary>
/// The real on-device backend: Phi Silica (<see cref="LanguageModel"/>), <see cref="TextSummarizer"/>, <see cref="TextRewriter"/> and
/// <see cref="ImageDescriptionGenerator"/> from the Windows App SDK. Only ever created when the app has package identity.
/// Not exercised by the automated tests (they need a Copilot+ PC); the provider's gating and routing are tested with a fake backend.
/// </summary>
internal sealed class PhiSilicaBackend : IWindowsAiBackend
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LanguageModel? _model;

    public WindowsAiState GetReadyState()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100)) return WindowsAiState.NotSupported;
        return Map(LanguageModel.GetReadyState());
    }

    private static WindowsAiState Map(AIFeatureReadyState state) => state switch
    {
        AIFeatureReadyState.Ready => WindowsAiState.Ready,
        AIFeatureReadyState.NotReady => WindowsAiState.NotReady,
        AIFeatureReadyState.DisabledByUser => WindowsAiState.DisabledByUser,
        _ => WindowsAiState.NotSupported,
    };

    public async Task<string?> EnsureReadyAsync(IProgress<double>? progress, CancellationToken ct)
    {
        var op = LanguageModel.EnsureReadyAsync();
        if (progress is not null) op.Progress = (_, p) => progress.Report(p);
        using var reg = ct.Register(op.Cancel);
        var result = await op;
        if (result.Status == AIFeatureReadyResultState.Success) return null;
        return string.IsNullOrWhiteSpace(result.ErrorDisplayText) ? Strings.Get("PhiSilicaBackend_Error_SetupFailed") : result.ErrorDisplayText;
    }

    private async Task<LanguageModel> ModelAsync()
    {
        await _gate.WaitAsync();
        try { return _model ??= await LanguageModel.CreateAsync(); }
        finally { _gate.Release(); }
    }

    public async Task<string> GenerateAsync(string system, string prompt, CancellationToken ct)
    {
        var model = await ModelAsync();
        using var context = model.CreateContext(system);
        var op = model.GenerateResponseAsync(context, prompt, new LanguageModelOptions());
        using var reg = ct.Register(op.Cancel);
        var result = await op;
        if (result.Status != LanguageModelResponseStatus.Complete) throw new ActionException(Explain(result.Status));
        return result.Text;
    }

    public async Task<string?> SummarizeAsync(string text, bool bullets, CancellationToken ct)
    {
        var model = await ModelAsync();
        var summarizer = new TextSummarizer(model);
        var op = bullets ? summarizer.SummarizeAsync(text) : summarizer.SummarizeParagraphAsync(text);
        using var reg = ct.Register(op.Cancel);
        var result = await op;
        return result.Status == LanguageModelResponseStatus.Complete ? result.Text : null;
    }

    public async Task<string?> RewriteAsync(string text, RewriteTone tone, CancellationToken ct)
    {
        var model = await ModelAsync();
        var rewriter = new TextRewriter(model);
        var op = rewriter.RewriteAsync(text, tone switch
        {
            RewriteTone.Casual => TextRewriteTone.Casual,
            RewriteTone.Concise => TextRewriteTone.Concise,
            RewriteTone.Formal => TextRewriteTone.Formal,
            _ => TextRewriteTone.General,
        });
        using var reg = ct.Register(op.Cancel);
        var result = await op;
        return result.Status == LanguageModelResponseStatus.Complete ? result.Text : null;
    }

    public async Task<string?> DescribeImageAsync(byte[] image, string detail, CancellationToken ct)
    {
        if (ImageDescriptionGenerator.GetReadyState() != AIFeatureReadyState.Ready) return null;
        using var stream = new WinStreams.InMemoryRandomAccessStream();
        using (var writer = new WinStreams.DataWriter(stream))
        {
            writer.WriteBytes(image);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        var decoder = await WinGraphics.BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(WinGraphics.BitmapPixelFormat.Bgra8, WinGraphics.BitmapAlphaMode.Premultiplied);
        var buffer = ImageBuffer.CreateForSoftwareBitmap(bitmap);
        var generator = await ImageDescriptionGenerator.CreateAsync();
        try
        {
            var kind = detail switch
            {
                "detailed" => ImageDescriptionKind.DetailedDescription,
                "alt" => ImageDescriptionKind.AccessibleDescription,
                _ => ImageDescriptionKind.BriefDescription,
            };
            var op = generator.DescribeAsync(buffer, kind, new ContentFilterOptions());
            using var reg = ct.Register(op.Cancel);
            var result = await op;
            return result.Status == ImageDescriptionResultStatus.Complete && !string.IsNullOrWhiteSpace(result.Description) ? result.Description : null;
        }
        finally { generator.Dispose(); }
    }

    private static string Explain(LanguageModelResponseStatus status) => status switch
    {
        LanguageModelResponseStatus.PromptLargerThanContext => Strings.Get("PhiSilicaBackend_Error_TooLong"),
        LanguageModelResponseStatus.PromptBlockedByContentModeration or LanguageModelResponseStatus.ResponseBlockedByContentModeration =>
            Strings.Get("PhiSilicaBackend_Error_Moderation"),
        LanguageModelResponseStatus.BlockedByPolicy => Strings.Get("PhiSilicaBackend_Error_Policy"),
        LanguageModelResponseStatus.UnsupportedLanguage or LanguageModelResponseStatus.LanguageMismatch => Strings.Get("PhiSilicaBackend_Error_Language"),
        _ => Strings.Get("PhiSilicaBackend_Error_Generic"),
    };
}
