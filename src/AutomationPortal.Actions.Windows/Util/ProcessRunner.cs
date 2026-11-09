using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Flowmate.Actions.Windows;

internal sealed record ProcessResult(int ExitCode, string Output, string Error, bool TimedOut);

internal static class ConsoleText
{
    /// <summary>Decodes console output line by line: UTF-8 when the line is valid UTF-8 (Wi-Fi names), otherwise the console's code page.</summary>
    public static string Decode(byte[] bytes)
    {
        var utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
        var oem = ProcessRunner.OemEncoding();
        var sb = new StringBuilder();
        var start = 0;
        for (var i = 0; i <= bytes.Length; i++)
        {
            if (i < bytes.Length && bytes[i] != (byte)'\n') continue;
            var length = i - start + (i < bytes.Length ? 1 : 0);
            if (length > 0)
            {
                try { sb.Append(utf8.GetString(bytes, start, length)); }
                catch (DecoderFallbackException) { sb.Append(oem.GetString(bytes, start, length)); }
            }
            start = i + 1;
        }
        return sb.ToString();
    }
}

internal static class ProcessRunner
{
    /// <summary>The console code page ("OEM") that built-in tools like netsh and cmd write in.</summary>
    public static Encoding OemEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch { return Encoding.UTF8; }
    }

    /// <summary>
    /// Runs a program and returns its standard output as raw bytes (for tools like netsh that mix console-code-page labels
    /// with UTF-8 names). Decode with <see cref="ConsoleText.Decode"/>.
    /// </summary>
    public static async Task<(int ExitCode, byte[] Output)> RunRawAsync(string file, string arguments, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
        };
        using var process = new Process { StartInfo = psi };
        process.Start();
        process.StandardInput.Close();
        using var buffer = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(buffer, CancellationToken.None);
        _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout > TimeSpan.Zero) timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            await copy.ConfigureAwait(false);
            return (process.ExitCode, buffer.ToArray());
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            if (ct.IsCancellationRequested) throw;
            return (-1, Array.Empty<byte>());
        }
    }

    public static async Task<ProcessResult> RunAsync(string file, string arguments, TimeSpan timeout, CancellationToken ct,
        Encoding? encoding = null, string? workingDirectory = null, IReadOnlyDictionary<string, string>? environment = null)
    {
        encoding ??= OemEncoding();
        var psi = new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding,
        };
        if (!string.IsNullOrWhiteSpace(workingDirectory)) psi.WorkingDirectory = workingDirectory;
        if (environment is not null) foreach (var (k, v) in environment) psi.Environment[k] = v;

        using var process = new Process { StartInfo = psi };
        var output = new StringBuilder();
        var error = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (error) error.AppendLine(e.Data); };
        process.Start();
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout > TimeSpan.Zero) timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            process.WaitForExit(); // flush the async output readers
            return new ProcessResult(process.ExitCode, output.ToString().TrimEnd(), error.ToString().TrimEnd(), false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            if (ct.IsCancellationRequested) throw;
            return new ProcessResult(-1, output.ToString().TrimEnd(), error.ToString().TrimEnd(), true);
        }
    }
}
