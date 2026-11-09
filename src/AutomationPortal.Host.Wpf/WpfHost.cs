using System.Windows.Threading;
using Flowmate.Actions.Documents;
using Flowmate.Core;
using Flowmate.Triggers;

namespace Flowmate.Host.Wpf;

/// <summary>Plugs Flowmate's WPF implementations into an engine: UI thread, clipboard, picture editing and the copy trigger's clipboard.</summary>
public static class WpfHost
{
    /// <summary>Process-wide backends the shared libraries call without a <see cref="FlowmateServices"/> at hand. Safe to call more than once.</summary>
    public static void InstallBackends()
    {
        if (!ImageEngine.HasBackend) ImageEngine.Backend = new WpfImageBackend();
        TriggerClipboard.Backend ??= new WpfTriggerClipboard();
    }

    /// <summary>Uses <paramref name="uiThread"/> for <c>UiThread</c> and the WPF clipboard for <c>Clipboard</c>, and installs the backends.</summary>
    public static FlowmateEngineBuilder UseWpf(this FlowmateEngineBuilder builder, IUiThread uiThread)
    {
        InstallBackends();
        return builder.ConfigureServices(s =>
        {
            s.UiThread = uiThread;
            s.Clipboard = new ClipboardService(uiThread, s.Paths);
        });
    }

    public static FlowmateEngineBuilder UseWpf(this FlowmateEngineBuilder builder, Dispatcher dispatcher) =>
        builder.UseWpf(new DispatcherUiThread(dispatcher));

    /// <summary>
    /// A background STA thread running a dispatcher, for hosts with no UI thread of their own (the command line).
    /// Clipboard and imaging work need one. Stop it with <see cref="Dispatcher.InvokeShutdown"/>.
    /// </summary>
    public static Dispatcher StartStaDispatcher(string name = "Flowmate STA")
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        }) { IsBackground = true, Name = name };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }
}
