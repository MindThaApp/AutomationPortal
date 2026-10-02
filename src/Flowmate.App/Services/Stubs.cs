using Flowmate.App.Integration;
using Flowmate.Core;

namespace Flowmate.App.Services;

/// <summary>Placeholder until the shell integration (registry, shell links) arrives.</summary>
public sealed class StubShellIntegration : IShellIntegration
{
    public bool IsStartWithWindowsEnabled { get; private set; }

    public void SetStartWithWindows(bool enabled) => IsStartWithWindowsEnabled = enabled;

    public void AddToDesktop(Shortcut shortcut) =>
        throw new NotSupportedException("Adding shortcuts to the desktop arrives with the next update.");

    public void AddToStartMenu(Shortcut shortcut) =>
        throw new NotSupportedException("Adding shortcuts to the Start menu arrives with the next update.");
}
