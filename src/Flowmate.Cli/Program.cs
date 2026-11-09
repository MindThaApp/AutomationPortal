using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Flowmate.Core;

namespace Flowmate.Cli;

public static class Program
{
    private const string Usage = """
        fm - run your Flow Mate shortcuts from a terminal

        Usage
          fm list [--json]
          fm run "Name" [--input <text>] [--file <path>]... [--json] [--no-wait]
          fm import <file.flowmate>
          fm export "Name" <file.flowmate> [--force]
          fm --help | --version

        "Name" can be the shortcut's name, its Quick Run alias or its id. Use --input - to read the input text from standard input.
        fm talks to Flow Mate when it is running; otherwise it runs the shortcut itself and asks any questions in the terminal.
        Add --data-root <folder> to work on a separate copy of your data.
        fm export never replaces an existing file: it saves as "Name (2).flowmate" instead, unless you add --force.

        Exit codes: 0 done, 1 the shortcut failed, 2 wrong arguments, 3 shortcut not found, 4 cancelled or stopped, 5 file or import problem.
        More: docs/CLI.md
        """;

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try { return MainAsync(args).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { Console.Error.WriteLine("Cancelled."); return CliExit.Cancelled; }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Something went wrong. " + ex.Message);
            return CliExit.Problem;
        }
    }

    private static async Task<int> MainAsync(string[] args)
    {
        var dataRoot = InstanceNames.DataRootFrom(args);
        if (args.Length == 0) { Console.Error.WriteLine(Usage); return CliExit.Usage; }
        if (IsLaunchStyle(args[0])) return Launch(args, dataRoot);

        var verb = args[0].ToLowerInvariant();
        var rest = StripOption(args.Skip(1).ToList(), "--data-root", takesValue: true, out _);
        switch (verb)
        {
            case "-h": case "--help": case "help": Console.Out.WriteLine(Usage); return CliExit.Ok;
            case "-v": case "--version": case "version":
                Console.Out.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
                return CliExit.Ok;
            case "list": return await SendAsync(new PipeRequest { Kind = "list", Json = TakeFlag(rest, "--json") }, dataRoot, rest);
            case "run": return await RunAsync(rest, dataRoot);
            case "import": return await ImportAsync(rest, dataRoot);
            case "export": return await ExportAsync(rest, dataRoot);
            default:
                Console.Error.WriteLine($"fm doesn't know the command \"{args[0]}\".\n\n{Usage}");
                return CliExit.Usage;
        }
    }

    // ---- commands ----------------------------------------------------------------------------

    private static async Task<int> RunAsync(List<string> rest, string? dataRoot)
    {
        var json = TakeFlag(rest, "--json");
        var noWait = TakeFlag(rest, "--no-wait");
        TakeFlag(rest, "--wait");
        var files = new List<string>();
        while (TakeValue(rest, out var f, "--file", "--files", "-f")) files.Add(Path.GetFullPath(f));
        TakeValue(rest, out var input, "--input", "-i");
        if (input == "-") input = await Console.In.ReadToEndAsync();
        if (rest.Count != 1) { Console.Error.WriteLine("Say which shortcut to run, for example: fm run \"Good Morning\""); return CliExit.Usage; }

        return await SendAsync(new PipeRequest { Kind = "run", Name = rest[0], Input = input, Files = files.ToArray(), Json = json, Wait = !noWait }, dataRoot, rest);
    }

    private static async Task<int> ImportAsync(List<string> rest, string? dataRoot)
    {
        var json = TakeFlag(rest, "--json");
        if (rest.Count != 1) { Console.Error.WriteLine("Say which file to import, for example: fm import \"Good Morning.flowmate\""); return CliExit.Usage; }
        string text;
        try { text = await File.ReadAllTextAsync(Path.GetFullPath(rest[0])); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Couldn't read \"{rest[0]}\". Check the file exists.");
            return CliExit.Problem;
        }
        return await SendAsync(new PipeRequest { Kind = "import", Input = text, Json = json, Wait = true }, dataRoot, rest);
    }

    private static async Task<int> ExportAsync(List<string> rest, string? dataRoot)
    {
        var force = TakeFlag(rest, "--force");
        if (rest.Count != 2) { Console.Error.WriteLine("Say which shortcut and where to save it, for example: fm export \"Good Morning\" \"Good Morning.flowmate\""); return CliExit.Usage; }
        var target = Path.GetFullPath(rest[1]);
        var (response, _) = await ExchangeAsync(new PipeRequest { Kind = "export", Name = rest[0], Wait = true }, dataRoot);
        if (response.ExitCode != CliExit.Ok) return Report(response, null);
        try { target = await SaveExportAsync(target, response.Output ?? "", force); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Couldn't save \"{target}\". {ex.Message}");
            return CliExit.Problem;
        }
        Console.Error.WriteLine($"Saved \"{target}\".");
        return CliExit.Ok;
    }

    /// <summary>
    /// Writes an export. Without <paramref name="force"/> an existing file is never replaced: the first free name of
    /// "Name.flowmate", "Name (2).flowmate", "Name (3).flowmate"… is used. Returns the path that was written.
    /// </summary>
    internal static async Task<string> SaveExportAsync(string target, string text, bool force)
    {
        var bytes = new UTF8Encoding(false).GetBytes(text);
        if (force)
        {
            await File.WriteAllBytesAsync(target, bytes);
            return target;
        }
        var folder = Path.GetDirectoryName(target) ?? "";
        var stem = Path.GetFileNameWithoutExtension(target);
        var ext = Path.GetExtension(target);
        for (var n = 1; n < 10_000; n++)
        {
            var path = n == 1 ? target : Path.Combine(folder, $"{stem} ({n}){ext}");
            if (File.Exists(path) || Directory.Exists(path)) continue;
            try
            {
                // CreateNew fails instead of replacing a file that appeared in the meantime.
                await using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await fs.WriteAsync(bytes);
                return path;
            }
            catch (IOException) when (File.Exists(path)) { }
        }
        throw new IOException("There are too many files with that name already.");
    }

    // ---- talking to the app, or doing it ourselves -------------------------------------------

    private static async Task<int> SendAsync(PipeRequest request, string? dataRoot, List<string> leftover)
    {
        if (leftover.Count > 0 && leftover.Any(a => a.StartsWith('-')))
        {
            Console.Error.WriteLine($"fm doesn't know the option \"{leftover.First(a => a.StartsWith('-'))}\".\n\n{Usage}");
            return CliExit.Usage;
        }
        var (response, shown) = await ExchangeAsync(request, dataRoot);
        return Report(response, shown);
    }

    private static async Task<(PipeResponse Response, Func<string?> Shown)> ExchangeAsync(PipeRequest request, string? dataRoot)
    {
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cts.Cancel(); };
        Console.CancelKeyPress += onCancel;
        try
        {
            PipeResponse? response = null;
            try { response = await PipeClient.TrySendAsync(InstanceNames.For(dataRoot), request, TimeSpan.FromMilliseconds(400), cts.Token); }
            catch (OperationCanceledException) { return (PipeResponse.Error(CliExit.Cancelled, "Cancelled."), NoShown); }
            catch (IOException ex) { return (PipeResponse.Error(CliExit.Problem, "Flow Mate stopped answering. " + ex.Message), NoShown); }
            if (response is not null) return (response, NoShown);

            // Flow Mate isn't running: do it here. Nothing can carry on after we exit, so always wait for the result.
            request.Wait = true;
            using var host = new CliHost(dataRoot);
            Func<string?> shown = () => host.Interaction.LastShownText;
            try { return (await host.Commands.ExecuteAsync(request, cts.Token), shown); }
            catch (OperationCanceledException) { return (PipeResponse.Error(CliExit.Cancelled, "Cancelled."), shown); }
        }
        finally { Console.CancelKeyPress -= onCancel; }
    }

    private static readonly Func<string?> NoShown = () => null;

    private static int Report(PipeResponse response, Func<string?>? shown)
    {
        var output = response.Output;
        if (!string.IsNullOrEmpty(output) && !(shown?.Invoke() is { } already && already == output.TrimEnd()))
            Console.Out.WriteLine(output.TrimEnd('\r', '\n'));
        if (!string.IsNullOrEmpty(response.Message)) Console.Error.WriteLine(response.Message);
        return response.ExitCode;
    }

    // ---- "launch" style arguments, as used by Send To, Desktop links and Explorer verbs in the Store build ----

    private static bool IsLaunchStyle(string first) =>
        first.StartsWith("--run", StringComparison.OrdinalIgnoreCase) || first.StartsWith("--import", StringComparison.OrdinalIgnoreCase)
        || first.Equals("--background", StringComparison.OrdinalIgnoreCase) || first.StartsWith("flowmate://", StringComparison.OrdinalIgnoreCase)
        || first.EndsWith(".flowmate", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The packaged build points its Send To / Desktop / Start menu links at the fm.exe alias (a stable path). Those links
    /// pass Flowmate.exe style arguments: hand them to the running app, or start the app with them. No console, no output.
    /// </summary>
    private static int Launch(string[] args, string? dataRoot)
    {
        FreeConsole();
        var delivered = Forward(InstanceNames.For(dataRoot), args);
        if (delivered) return CliExit.Ok;

        var app = Path.Combine(AppContext.BaseDirectory, "Flowmate.exe");
        if (!File.Exists(app)) return CliExit.Problem;
        var start = new ProcessStartInfo(app) { UseShellExecute = false };
        foreach (var a in args) start.ArgumentList.Add(a);
        Process.Start(start);
        return CliExit.Ok;
    }

    private static bool Forward(string pipeName, string[] args)
    {
        try { return PipeClient.TrySendAsync(pipeName, new PipeRequest { Kind = "launch", Args = args }, TimeSpan.FromMilliseconds(500)).GetAwaiter().GetResult() is not null; }
        catch (IOException) { return false; }
    }

    // ---- tiny argument helpers ---------------------------------------------------------------

    private static bool TakeFlag(List<string> args, string name)
    {
        var i = args.FindIndex(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return false;
        args.RemoveAt(i);
        return true;
    }

    private static bool TakeValue(List<string> args, out string value, params string[] names)
    {
        var i = args.FindIndex(a => names.Contains(a, StringComparer.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= args.Count) { value = ""; return false; }
        value = args[i + 1];
        args.RemoveRange(i, 2);
        return true;
    }

    private static List<string> StripOption(List<string> args, string name, bool takesValue, out string? value)
    {
        value = null;
        var i = args.FindIndex(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0)
        {
            if (takesValue && i + 1 < args.Count) value = args[i + 1];
            args.RemoveRange(i, takesValue && i + 1 < args.Count ? 2 : 1);
        }
        return args;
    }
}
