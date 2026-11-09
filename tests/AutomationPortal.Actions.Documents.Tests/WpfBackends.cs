using System.Runtime.CompilerServices;

namespace AutomationPortal.Actions.Documents.Tests;

internal static class WpfBackends
{
    [ModuleInitializer]
    internal static void Install() => AutomationPortal.Host.Wpf.WpfHost.InstallBackends();
}
