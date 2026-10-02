using Flowmate.Core;

namespace Flowmate.App.Integration;

/// <summary>
/// One running copy of Flow Mate per user (and data root). The first copy owns a named mutex and listens on the
/// <c>Flowmate-&lt;SID&gt;</c> pipe; later launches hand their command line to it and exit.
/// </summary>
public sealed class InstanceCoordinator : IDisposable
{
    private readonly Mutex? _mutex;
    private PipeServer? _server;

    public string Name { get; }
    public bool IsPrimary { get; }

    public InstanceCoordinator(string name)
    {
        Name = name;
        var mutex = new Mutex(true, @"Local\" + name, out var created);
        IsPrimary = created;
        if (created) _mutex = mutex; else mutex.Dispose();
    }

    public void StartServer(Func<PipeRequest, CancellationToken, Task<PipeResponse>> handler)
    {
        _server = new PipeServer(Name, handler);
        _server.Start();
    }

    /// <summary>Hands this launch's arguments to the running copy (waits up to 10 seconds for it to come up). False when nobody answered.</summary>
    public bool Forward(string[] args) => ForwardAsync(Name, args, TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();

    public static async Task<bool> ForwardAsync(string name, string[] args, TimeSpan patience)
    {
        var until = DateTime.UtcNow + patience;
        while (true)
        {
            try
            {
                var reply = await PipeClient.TrySendAsync(name, new PipeRequest { Kind = "launch", Args = args }, TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
                if (reply is not null) return true;
            }
            catch (IOException) { }
            if (DateTime.UtcNow >= until) return false;
            await Task.Delay(200).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _server?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex?.Dispose();
    }
}
