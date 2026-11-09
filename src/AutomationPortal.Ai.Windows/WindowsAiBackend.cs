namespace AutomationPortal.Ai.Windows;

/// <summary>Where the on-device model stands, in Automation Portal's words (mapped from the Windows App SDK's ready state).</summary>
public enum WindowsAiState
{
    Ready,
    /// <summary>Supported, but the model still has to be downloaded or set up (Windows does this).</summary>
    NotReady,
    /// <summary>This PC can't run it (no NPU / not a Copilot+ PC, or Windows is too old).</summary>
    NotSupported,
    DisabledByUser,
    /// <summary>The Windows AI components couldn't be loaded at all.</summary>
    Unavailable,
}

public enum RewriteTone { General, Casual, Concise, Formal }

/// <summary>
/// The on-device features the provider uses. <see cref="PhiSilicaBackend"/> is the real one (Windows App SDK, packaged app on a Copilot+ PC);
/// tests use a fake. Methods that return null mean "this feature can't do that one, use the language model instead".
/// </summary>
internal interface IWindowsAiBackend
{
    WindowsAiState GetReadyState();
    /// <summary>Asks Windows to download / set up the model. Null on success, otherwise a plain-English problem.</summary>
    Task<string?> EnsureReadyAsync(IProgress<double>? progress, CancellationToken ct);
    /// <summary>Phi Silica: one answer for <paramref name="prompt"/> under <paramref name="system"/> instructions.</summary>
    Task<string> GenerateAsync(string system, string prompt, CancellationToken ct);
    Task<string?> SummarizeAsync(string text, bool bullets, CancellationToken ct);
    Task<string?> RewriteAsync(string text, RewriteTone tone, CancellationToken ct);
    /// <summary>"short", "detailed" or "alt". Null when the on-device image description isn't available.</summary>
    Task<string?> DescribeImageAsync(byte[] image, string detail, CancellationToken ct);
}
