using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AutomationPortal.Core;

namespace AutomationPortal.Cli;

public static class Program
{
    private const string Usage = """
        ap - run your Automation Portal shortcuts from a terminal

        Usage
          ap list [--json]
          ap run "Name" [--input <text>] [--file <path>]... [--files-from <list.txt|->] [--from <caller>] [--json] [--no-wait]
          ap preview "Name" [--input <text>] [--file <path>]... [--files-from <list.txt|->] [--json]
          ap tools
          ap new --file <path> [--file <path>]... [--files-from <list.txt|->] [--from <caller>] [--json]
          ap import <file.autoportal>...
          ap export "Name" <file.autoportal> [--force]
          ap capabilities [--json]
          ap --help | --version

        "Name" can be the shortcut's name, its Quick Run alias or its id. Use --input - to read the input text from standard input.
        ap talks to Automation Portal when it is running; otherwise it runs the shortcut itself and asks any questions in the terminal.
        Add --data-root <folder> to work on a separate copy of your data.
        ap export never replaces an existing file: it saves as "Name (2).autoportal" instead, unless you add --force.
        --from <caller> is for other apps: Automation Portal asks you before it runs the shortcut (caller: a-z, 0-9 and -, up to 32).
        ap new opens a new, unsaved shortcut in Automation Portal with those files to try it on. It never runs anything.
        ap preview says what a shortcut would do, step by step, without running anything. ap tools prints every shortcut as a JSON tool definition.

        Exit codes: 0 done, 1 the shortcut failed, 2 wrong arguments, 3 shortcut not found, 4 cancelled or stopped, 5 file or import problem,
        6 not run (you chose Don't run, or nobody answered in time).
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
            case "capabilities": return await CapabilitiesAsync(rest, dataRoot);
            case "run": return await RunAsync(rest, dataRoot);
            case "preview": return await PreviewAsync(rest, dataRoot);
            case "tools":
                TakeFlag(rest, "--json");   // always JSON
                return await SendAsync(new PipeRequest { Kind = "tools", Json = true }, dataRoot, rest);
            case "new": return await NewAsync(rest, dataRoot);
            case "import": return await ImportAsync(rest, dataRoot);
            case "export": return await ExportAsync(rest, dataRoot);
            default:
                Console.Error.WriteLine($"ap doesn't know the command \"{args[0]}\".\n\n{Usage}");
                return CliExit.Usage;
        }
    }

    // ---- commands ----------------------------------------------------------------------------

    internal static async Task<int> RunAsync(List<string> rest, string? dataRoot)
    {
        var (request, error) = await ParseRunAsync(rest, Console.In, Directory.GetCurrentDirectory());
        if (request is null) { Console.Error.WriteLine(error); return CliExit.Usage; }
        return await SendAsync(request, dataRoot, rest);
    }

    /// <summary>
    /// The request for <c>ap run</c>. Takes the options it knows out of <paramref name="rest"/>; what is left over is checked by the caller.
    /// <c>--from &lt;caller&gt;</c> names the app that wants the run and asks the user first.
    /// </summary>
    internal static async Task<(PipeRequest? Request, string? Error)> ParseRunAsync(List<string> rest, TextReader stdin, string currentDirectory)
    {
        var json = TakeFlag(rest, "--json");
        var noWait = TakeFlag(rest, "--no-wait");
        TakeFlag(rest, "--wait");
        TakeValue(rest, out var input, "--input", "-i");
        var (files, from, problem) = await TakeFilesAndCallerAsync(rest, stdin, currentDirectory, stdinTaken: input == "-");
        if (problem is not null) return (null, problem);
        if (input == "-") input = await stdin.ReadToEndAsync();
        if (rest.Count != 1) return (null, "Say which shortcut to run, for example: ap run \"Good Morning\"");

        return (new PipeRequest
        {
            Kind = "run", Name = rest[0], Input = input, Files = files.ToArray(), Json = json, Wait = !noWait,
            Source = from, Ask = from is not null,
        }, null);
    }

    internal static async Task<int> PreviewAsync(List<string> rest, string? dataRoot)
    {
        var (request, error) = await ParsePreviewAsync(rest, Console.In, Directory.GetCurrentDirectory());
        if (request is null) { Console.Error.WriteLine(error); return CliExit.Usage; }
        return await SendAsync(request, dataRoot, rest);
    }

    /// <summary>The request for <c>ap preview</c>: what the shortcut would do with this input. Nothing runs and nobody is asked.</summary>
    internal static async Task<(PipeRequest? Request, string? Error)> ParsePreviewAsync(List<string> rest, TextReader stdin, string currentDirectory)
    {
        var json = TakeFlag(rest, "--json");
        TakeValue(rest, out var input, "--input", "-i");
        var (files, from, problem) = await TakeFilesAndCallerAsync(rest, stdin, currentDirectory, stdinTaken: input == "-");
        if (problem is not null) return (null, problem);
        if (input == "-") input = await stdin.ReadToEndAsync();
        if (rest.Count != 1) return (null, "Say which shortcut to preview, for example: ap preview \"Good Morning\"");
        return (new PipeRequest { Kind = "preview", Name = rest[0], Input = input, Files = files.ToArray(), Json = json, Wait = true, Source = from }, null);
    }

    internal static async Task<int> NewAsync(List<string> rest, string? dataRoot)
    {
        var (request, error) = await ParseNewAsync(rest, Console.In, Directory.GetCurrentDirectory());
        if (request is null) { Console.Error.WriteLine(error); return CliExit.Usage; }
        return await SendAsync(request, dataRoot, rest);
    }

    /// <summary>
    /// The request for <c>ap new</c>: opens a new, unsaved shortcut in the editor with the files to try it on. Runs nothing.
    /// <c>--from</c> only names the app that sent the files; nothing is asked.
    /// </summary>
    internal static async Task<(PipeRequest? Request, string? Error)> ParseNewAsync(List<string> rest, TextReader stdin, string currentDirectory)
    {
        var json = TakeFlag(rest, "--json");
        var (files, from, problem) = await TakeFilesAndCallerAsync(rest, stdin, currentDirectory, stdinTaken: false);
        if (problem is not null) return (null, problem);
        if (rest.Count > 0) return (null, $"ap new doesn't take \"{rest[0]}\". Name the files with --file or --files-from, for example: ap new --file \"C:\\Photos\\a.jpg\"");
        if (files.Count == 0) return (null, "Say which files the new shortcut is for, for example: ap new --file \"C:\\Photos\\a.jpg\"");
        return (new PipeRequest { Kind = "new", Files = files.ToArray(), Json = json, Wait = true, Source = from }, null);
    }

    /// <summary>--file (repeatable), --files-from and --from, shared by run and new.</summary>
    private static async Task<(List<string> Files, string? From, string? Problem)> TakeFilesAndCallerAsync(List<string> rest, TextReader stdin, string currentDirectory, bool stdinTaken)
    {
        var files = new List<string>();
        while (TakeValue(rest, out var f, "--file", "--files", "-f")) files.Add(Path.GetFullPath(f, currentDirectory));
        string? from = null;
        if (rest.Any(a => a.Equals("--from", StringComparison.OrdinalIgnoreCase)))
        {
            if (!TakeValue(rest, out var caller, "--from")) return (files, null, "--from needs the name of the app that is asking, for example: --from filesportal");
            if (!Callers.IsValidId(caller)) return (files, null, Callers.InvalidIdMessage);
            from = caller;
        }
        var hasListSource = TakeValue(rest, out var listSource, "--files-from");
        if (hasListSource && listSource == "-" && stdinTaken)
            return (files, from, "Standard input can only be used once: don't combine --input - with --files-from -.");
        if (hasListSource)
        {
            var (listed, error) = await FileList.ReadAsync(listSource, stdin, currentDirectory);
            if (listed is null) return (files, from, error);
            files.AddRange(listed);
        }
        return (files, from, null);
    }

    // The running app answers over the pipe; otherwise the in-process commands build the same hello.
    private static async Task<int> CapabilitiesAsync(List<string> rest, string? dataRoot)
    {
        var json = TakeFlag(rest, "--json");
        if (rest.Count > 0) { Console.Error.WriteLine($"ap doesn't know the option \"{rest[0]}\".\n\n{Usage}"); return CliExit.Usage; }
        var (response, _) = await ExchangeAsync(new PipeRequest { Kind = "capabilities", Wait = true }, dataRoot);
        if (response.ExitCode != CliExit.Ok || json || string.IsNullOrWhiteSpace(response.Output)) return Report(response, null);
        Console.Out.WriteLine(Summarize(response.Output));
        return CliExit.Ok;
    }

    internal static string Summarize(string helloJson)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(helloJson);
        var root = doc.RootElement;
        string Text(string name) => root.TryGetProperty(name, out var v) ? v.ToString() : "?";
        string List(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Array
            ? string.Join(", ", v.EnumerateArray().Select(e => e.ToString())) : "";
        return $"Automation Portal {Text("version")} ({Text("edition")}), pipe contract {Text("contract")}\nCommands: {List("kinds")}\nFeatures: {List("features")}";
    }

    private static async Task<int> ImportAsync(List<string> rest, string? dataRoot)
    {
        var json = TakeFlag(rest, "--json");
        if (rest.Count == 0) { Console.Error.WriteLine("Say which file to import, for example: ap import \"Good Morning.autoportal\""); return CliExit.Usage; }
        if (rest.FirstOrDefault(a => a.StartsWith('-')) is { } option)
        {
            Console.Error.WriteLine($"ap doesn't know the option \"{option}\".\n\n{Usage}");
            return CliExit.Usage;
        }
        var texts = new List<string>();
        foreach (var file in rest)
        {
            try { texts.Add(await File.ReadAllTextAsync(Path.GetFullPath(file))); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Couldn't read \"{file}\". Check the file exists.");
                return CliExit.Problem;
            }
        }

        // Automation Portal open: it reviews and limits the import itself (the plan's shortcut limit included).
        var running = await PipeClient.TrySendAsync(InstanceNames.For(dataRoot), new PipeRequest { Kind = "ping", Wait = true }, TimeSpan.FromMilliseconds(400)) is not null;
        if (running)
        {
            var first = 0;
            foreach (var text in texts)
            {
                var (response, shown) = await ExchangeAsync(new PipeRequest { Kind = "import", Input = text, Json = json, Wait = true }, dataRoot);
                var code = Report(response, shown);
                if (code != 0 && first == 0) first = code;
            }
            return first;
        }

        // Closed: ap.exe writes to the library itself, so it applies the plan's limit from the last plan the app saw.
        using var host = new CliHost(dataRoot);
        var tier = ImportGate.TierFor(PackageIdentity.IsPackaged, ImportGate.ReadOwned(host.BasePaths));
        return await ImportGate.ImportUpToAsync(texts, tier, host.ShortcutCount, async text =>
        {
            var response = await host.Commands.ExecuteAsync(new PipeRequest { Kind = "import", Input = text, Json = json, Wait = true, Source = "cli" }, CancellationToken.None);
            return Report(response, () => host.Interaction.LastShownText);
        }, Console.Error, CliExit.Problem);
    }

    private static async Task<int> ExportAsync(List<string> rest, string? dataRoot)
    {
        var force = TakeFlag(rest, "--force");
        if (rest.Count != 2) { Console.Error.WriteLine("Say which shortcut and where to save it, for example: ap export \"Good Morning\" \"Good Morning.autoportal\""); return CliExit.Usage; }
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
    /// "Name.autoportal", "Name (2).autoportal", "Name (3).autoportal"… is used. Returns the path that was written.
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
            Console.Error.WriteLine($"ap doesn't know the option \"{leftover.First(a => a.StartsWith('-'))}\".\n\n{Usage}");
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
        request.Source ??= "cli";
        try
        {
            PipeResponse? response = null;
            try { response = await PipeClient.TrySendAsync(InstanceNames.For(dataRoot), request, TimeSpan.FromMilliseconds(400), cts.Token); }
            catch (OperationCanceledException) { return (PipeResponse.Error(CliExit.Cancelled, "Cancelled."), NoShown); }
            catch (IOException ex) { return (PipeResponse.Error(CliExit.Problem, "Automation Portal stopped answering. " + ex.Message), NoShown); }
            if (response is not null) return (response, NoShown);

            // Only the app can ask the user (or show the editor): start it in the background and hand it the request.
            if (NeedsApp(request))
            {
                try { return (await StartAppAndSendAsync(request, dataRoot, cts.Token), NoShown); }
                catch (OperationCanceledException) { return (PipeResponse.Error(CliExit.Cancelled, "Cancelled."), NoShown); }
                catch (IOException ex) { return (PipeResponse.Error(CliExit.Problem, "Automation Portal stopped answering. " + ex.Message), NoShown); }
            }

            // Automation Portal isn't running: do it here. Nothing can carry on after we exit, so always wait for the result.
            request.Wait = true;
            using var host = new CliHost(dataRoot);
            Func<string?> shown = () => host.Interaction.LastShownText;
            try { return (await host.Commands.ExecuteAsync(request, cts.Token), shown); }
            catch (OperationCanceledException) { return (PipeResponse.Error(CliExit.Cancelled, "Cancelled."), shown); }
        }
        finally { Console.CancelKeyPress -= onCancel; }
    }

    private static readonly Func<string?> NoShown = () => null;

    /// <summary>Requests only the running app can answer: a run that asks the user first, and a new shortcut (it needs the editor).</summary>
    internal static bool NeedsApp(PipeRequest request) => (request.Ask && request.Kind == "run") || request.Kind == "new";

    private static readonly TimeSpan AppStartTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Starts AutomationPortal.exe (from beside ap.exe) in the background and sends the request once its pipe is up.
    /// The answer can take a while: the app may be waiting for the user (up to 60 seconds), and the pipe client has no read deadline.
    /// </summary>
    private static async Task<PipeResponse> StartAppAndSendAsync(PipeRequest request, string? dataRoot, CancellationToken ct)
    {
        var app = Path.Combine(AppContext.BaseDirectory, "AutomationPortal.exe");
        if (!File.Exists(app)) return PipeResponse.Error(CliExit.Problem, "Automation Portal needs to be open for this. Open it and try again.");
        var start = new ProcessStartInfo(app) { UseShellExecute = false };
        start.ArgumentList.Add("--background");
        if (!string.IsNullOrWhiteSpace(dataRoot)) { start.ArgumentList.Add("--data-root"); start.ArgumentList.Add(dataRoot); }
        Process.Start(start)?.Dispose();
        return await PipeClient.TrySendAsync(InstanceNames.For(dataRoot), request, AppStartTimeout, ct)
               ?? PipeResponse.Error(CliExit.Problem, "Automation Portal didn't start in time. Open it and try again.");
    }

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
        || first.Equals("--background", StringComparison.OrdinalIgnoreCase) || LaunchArguments.IsLink(first)
        || first.Equals(LaunchArguments.LinkSwitch, StringComparison.OrdinalIgnoreCase)
        || ShortcutPackage.HasPackageExtension(first);

    /// <summary>
    /// The packaged build points its Send To / Desktop / Start menu links at the ap.exe alias (a stable path). Those links
    /// pass AutomationPortal.exe style arguments: hand them to the running app, or start the app with them. No console, no output.
    /// </summary>
    private static int Launch(string[] args, string? dataRoot)
    {
        FreeConsole();
        var delivered = Forward(InstanceNames.For(dataRoot), args);
        if (delivered) return CliExit.Ok;

        var app = Path.Combine(AppContext.BaseDirectory, "AutomationPortal.exe");
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
