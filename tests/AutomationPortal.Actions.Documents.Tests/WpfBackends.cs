using System.Runtime.CompilerServices;

namespace Flowmate.Actions.Documents.Tests;

internal static class WpfBackends
{
    [ModuleInitializer]
    internal static void Install() => Flowmate.Host.Wpf.WpfHost.InstallBackends();
}
