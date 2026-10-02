using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Flowmate.Core;

/// <summary>Process exit codes of <c>fm</c> (see docs/CLI.md).</summary>
public static class CliExit
{
    public const int Ok = 0;
    /// <summary>The shortcut ran but a step failed.</summary>
    public const int Failed = 1;
    /// <summary>Wrong or missing arguments.</summary>
    public const int Usage = 2;
    public const int NotFound = 3;
    /// <summary>The shortcut was cancelled or stopped before finishing.</summary>
    public const int Cancelled = 4;
    /// <summary>A file, import or export problem, or the app couldn't do it here.</summary>
    public const int Problem = 5;
}

/// <summary>One request on the Flowmate pipe. Kind: ping, launch, list, run, import, export.</summary>
public sealed class PipeRequest
{
    public string Kind { get; set; } = "";
    /// <summary>launch: the command line of the second Flowmate.exe.</summary>
    public string[] Args { get; set; } = Array.Empty<string>();
    public string? Name { get; set; }
    /// <summary>run: input text. import: the .flowmate file text.</summary>
    public string? Input { get; set; }
    public string[] Files { get; set; } = Array.Empty<string>();
    public bool Wait { get; set; }
    public bool Json { get; set; }
}

public sealed class PipeResponse
{
    public int ExitCode { get; set; }
    /// <summary>What the command prints to standard output.</summary>
    public string? Output { get; set; }
    /// <summary>What the command prints to standard error (a plain-English problem).</summary>
    public string? Message { get; set; }

    public static PipeResponse Done(string? output = null, string? message = null) => new() { ExitCode = CliExit.Ok, Output = output, Message = message };
    public static PipeResponse Error(int code, string message) => new() { ExitCode = code, Message = message };
}

internal static class PipeJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static readonly UTF8Encoding Utf8 = new(false);
}

/// <summary>Listens on the per-user Flowmate pipe. One request line in, one response line out. Closing the pipe early cancels the request.</summary>
public sealed class PipeServer : IDisposable
{
    private readonly string _name;
    private readonly Func<PipeRequest, CancellationToken, Task<PipeResponse>> _handler;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    public PipeServer(string name, Func<PipeRequest, CancellationToken, Task<PipeResponse>> handler)
    {
        _name = name;
        _handler = handler;
    }

    /// <summary>Creates the pipe right away (so a client that connects after this returns finds it) and starts accepting.</summary>
    public void Start()
    {
        var first = Create();
        _loop = Task.Run(() => AcceptLoopAsync(first));
    }

    private NamedPipeServerStream Create() =>
        new(_name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task AcceptLoopAsync(NamedPipeServerStream pipe)
    {
        while (!_stop.IsCancellationRequested)
        {
            try { await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { pipe.Dispose(); return; }
            catch (IOException) { pipe.Dispose(); pipe = Create(); continue; }

            var connected = pipe;
            try { pipe = Create(); }
            catch (IOException) { connected.Dispose(); return; }
            _ = Task.Run(() => ServeAsync(connected));
        }
        pipe.Dispose();
    }

    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        await using var owned = pipe;
        try
        {
            using var reader = new StreamReader(pipe, PipeJson.Utf8, false, 4096, leaveOpen: true);
            var line = await reader.ReadLineAsync(_stop.Token).ConfigureAwait(false);
            if (line is null) return;

            PipeRequest? request = null;
            try { request = JsonSerializer.Deserialize<PipeRequest>(line, PipeJson.Options); } catch (JsonException) { }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            using var watchStop = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                try { if (await pipe.ReadAsync(new byte[1], watchStop.Token).ConfigureAwait(false) == 0) cts.Cancel(); }
                catch { }
            });

            PipeResponse response;
            if (request is null) response = PipeResponse.Error(CliExit.Usage, "Flow Mate couldn't understand that request.");
            else
            {
                try { response = await _handler(request, cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { response = PipeResponse.Error(CliExit.Cancelled, "Cancelled."); }
                catch (Exception ex) { response = PipeResponse.Error(CliExit.Problem, "Something went wrong. " + ex.Message); }
            }
            watchStop.Cancel();

            if (cts.IsCancellationRequested && !_stop.IsCancellationRequested) return;
            var bytes = PipeJson.Utf8.GetBytes(JsonSerializer.Serialize(response, PipeJson.Options) + "\n");
            await pipe.WriteAsync(bytes, _stop.Token).ConfigureAwait(false);
            await pipe.FlushAsync(_stop.Token).ConfigureAwait(false);
            if (OperatingSystem.IsWindows()) { try { pipe.WaitForPipeDrain(); } catch (IOException) { } }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _loop?.Wait(500); } catch { }
        _stop.Dispose();
    }
}

public static class PipeClient
{
    /// <summary>Sends one request to the running app. Null when no app is listening (nothing to connect to within <paramref name="connectTimeout"/>).</summary>
    public static async Task<PipeResponse?> TrySendAsync(string pipeName, PipeRequest request, TimeSpan connectTimeout, CancellationToken ct = default)
    {
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try { await client.ConnectAsync(Math.Max(1, (int)connectTimeout.TotalMilliseconds), ct).ConfigureAwait(false); }
        catch (TimeoutException) { return null; }

        var bytes = PipeJson.Utf8.GetBytes(JsonSerializer.Serialize(request, PipeJson.Options) + "\n");
        await client.WriteAsync(bytes, ct).ConfigureAwait(false);
        await client.FlushAsync(ct).ConfigureAwait(false);

        using var reader = new StreamReader(client, PipeJson.Utf8, false, 4096, leaveOpen: true);
        var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        if (line is null) throw new IOException("Flow Mate closed the connection before it answered.");
        return JsonSerializer.Deserialize<PipeResponse>(line, PipeJson.Options) ?? throw new IOException("Flow Mate sent an answer that couldn't be read.");
    }
}
