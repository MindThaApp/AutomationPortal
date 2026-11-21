using System.Windows.Threading;

namespace AutomationPortal.App.Tests;

/// <summary>One long-lived UI thread for every test that needs the WPF Application, which belongs to the thread that made it.</summary>
internal static class SharedSta
{
    private static readonly Lazy<Dispatcher> Ui = new(() =>
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() => { dispatcher = Dispatcher.CurrentDispatcher; ready.Set(); Dispatcher.Run(); }) { IsBackground = true, Name = "Test UI thread" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    });

    public static void Run(Action body)
    {
        Exception? error = null;
        Ui.Value.Invoke(() => { try { body(); } catch (Exception e) { error = e; } });
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }
}
