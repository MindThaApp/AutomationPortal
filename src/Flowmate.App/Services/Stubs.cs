using Flowmate.App.Integration;
using Flowmate.Core;

namespace Flowmate.App.Services;

/// <summary>INTEGRATION(E): replace with the registry / shell-link implementation.</summary>
public sealed class StubShellIntegration : IShellIntegration
{
    public bool IsStartWithWindowsEnabled { get; private set; }

    public void SetStartWithWindows(bool enabled) => IsStartWithWindowsEnabled = enabled;

    public void AddToDesktop(Shortcut shortcut) =>
        throw new NotSupportedException("Adding shortcuts to the desktop arrives with the next update.");
}

/// <summary>INTEGRATION(E): replace with a real call to the AI provider.</summary>
public sealed class StubAiKeyTester : IAiKeyTester
{
    public Task<AiKeyTestResult> TestAsync(string apiKey, AiModelTier model, CancellationToken ct = default) =>
        Task.FromResult(string.IsNullOrWhiteSpace(apiKey)
            ? new AiKeyTestResult(false, "Paste your key first.")
            : new AiKeyTestResult(false, "Checking keys online isn't available yet. Your key is saved and will be used when you run AI actions."));
}
