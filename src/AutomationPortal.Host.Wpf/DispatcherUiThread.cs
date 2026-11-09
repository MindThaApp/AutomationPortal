using System.Windows.Threading;
using AutomationPortal.Core;

namespace AutomationPortal.Host.Wpf;

/// <summary>Runs work on the WPF dispatcher thread (the app's single STA thread).</summary>
public sealed class DispatcherUiThread : IUiThread
{
    private readonly Dispatcher _dispatcher;

    public DispatcherUiThread(Dispatcher dispatcher) { _dispatcher = dispatcher; }

    public bool IsOnUiThread => _dispatcher.CheckAccess();

    public Task InvokeAsync(Action action) => _dispatcher.InvokeAsync(action).Task;

    public Task<T> InvokeAsync<T>(Func<T> func) => _dispatcher.InvokeAsync(func).Task;

    public Task<T> InvokeTaskAsync<T>(Func<Task<T>> func) => _dispatcher.InvokeAsync(func).Task.Unwrap();
}
