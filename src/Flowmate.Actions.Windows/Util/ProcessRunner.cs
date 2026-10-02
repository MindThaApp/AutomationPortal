using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Flowmate.Actions.Windows;

internal sealed record ProcessResult(int ExitCode, string Output, string Error, bool TimedOut);

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

    public static async Task<ProcessResult> RunAsync(string file, string arguments, TimeSpan timeout, CancellationToken ct,
        Encoding? encoding = null, string? workingDirectory = null)
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
