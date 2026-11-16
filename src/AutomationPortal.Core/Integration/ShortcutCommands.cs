using System.Text;
using System.Text.Json;

namespace AutomationPortal.Core;

/// <summary>
/// The CLI's commands (list, run, import, export), shared by the running app (answering over the pipe) and the
/// in-process <c>ap</c>, so both behave and print the same.
/// </summary>
public sealed class ShortcutCommands
{
    public delegate Task<RunResult?> RunHandler(Shortcut shortcut, ContentValue input, RunSource source, CancellationToken ct);

    private static readonly JsonSerializerOptions JsonOut = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly IShortcutLibrary _library;
    private readonly RunHandler _run;

    public ShortcutCommands(IShortcutLibrary library, RunHandler run)
    {
        _library = library;
        _run = run;
    }

    /// <summary>
    /// Set by the app when it has a window. Every pipe import is then shown to the user, who decides whether it is added, whatever the
    /// request's <see cref="PipeRequest.Source"/> says. Gets the .autoportal text and the request; returns the import, or null when the
    /// user said no. When not set (the in-process ap, run by the user at the terminal) imports are added straight away.
    /// </summary>
    public Func<string, PipeRequest, Task<ImportResult?>>? ReviewImport { get; init; }

    /// <summary>
    /// Set by the app: gets every accepted "event" request, which only feeds triggers. It must return quickly (never wait for an
    /// automation to run). When not set (the in-process ap) events are accepted and dropped.
    /// </summary>
    public Action<AppEvent>? PublishEvent { get; init; }

    /// <summary>
    /// Set by the app: asks the user before a run whose request has <see cref="PipeRequest.Ask"/> set. When not set (the in-process ap)
    /// such a run is refused, since nobody could be asked.
    /// </summary>
    public CallerConsent? Consent { get; init; }

    /// <summary>Whether a shortcut has steps that change files, for the caller prompt. The app knows which steps do.</summary>
    public Func<Shortcut, bool>? ChangesFiles { get; init; }

    /// <summary>
    /// Set by the app: opens the editor on a new, unsaved shortcut with the files to try it on, and completes once the editor is shown.
    /// When not set (the in-process ap) "new" is refused, since there is no editor.
    /// </summary>
    public Func<NewShortcutRequest, CancellationToken, Task>? OpenNewShortcut { get; init; }

    /// <summary>The pipe contract version reported by ping and capabilities. See "Pipe contract v1" in docs/CLI.md.</summary>
    public const int ContractVersion = 1;

    /// <summary>The kinds other apps may rely on. "launch" and "export" also work but are internal.</summary>
    public static IReadOnlyList<string> ContractKinds { get; } = new[] { "ping", "capabilities", "list", "run", "import", "event", "new" };

    public Task<PipeResponse> ExecuteAsync(PipeRequest request, CancellationToken ct) => request.Kind switch
    {
        "ping" or "capabilities" => Task.FromResult(PipeResponse.Done(Hello())),
        "list" => Task.FromResult(List(request.Json)),
        "run" => RunAsync(request, ct),
        "import" => ImportAsync(request, ct),
        "export" => Task.FromResult(Export(request)),
        "event" => Task.FromResult(Event(request)),
        "new" => NewAsync(request, ct),
        _ => Task.FromResult(PipeResponse.Error(CliExit.Usage, $"Automation Portal doesn't know the command \"{request.Kind}\".")),
    };

    // ---- ping / capabilities -----------------------------------------------------------------

    private string Hello()
    {
        // caller-prompt: ap.exe starts the app when it has to ask, so it is always there.
        var features = new List<string> { "list.json", "run.wait", "run.input", "run.files", "run.json", "import.json", "cli.files-from", "caller-prompt", "new-shortcut" };
        if (ReviewImport is not null) features.Add("import.review");
        return JsonSerializer.Serialize(new
        {
            app = "AutomationPortal", version = Version, contract = ContractVersion,
            edition = PackageIdentity.IsPackaged ? "store" : "unpackaged", kinds = ContractKinds, features,
        }, JsonOut);
    }

    private static readonly string Version = ReadVersion();

    private static string ReadVersion()
    {
        var assembly = typeof(ShortcutCommands).Assembly;
        var info = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(assembly)?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(info)) return assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        var plus = info.IndexOf('+');
        return plus < 0 ? info : info[..plus];   // drop the source revision the SDK appends
    }

    // ---- finding a shortcut ------------------------------------------------------------------

    /// <summary>Id, then exact name, then alias, then a name that starts with the text (only when exactly one does).</summary>
    public Shortcut? Find(string nameOrId)
    {
        if (Guid.TryParse(nameOrId, out var id) && _library.GetById(id) is { } byId) return byId;
        if (_library.FindByName(nameOrId) is { } byName) return byName;
        var all = _library.All;
        var alias = all.Where(s => string.Equals(s.Alias, nameOrId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (alias.Count == 1) return alias[0];
        var prefix = all.Where(s => s.Name.StartsWith(nameOrId, StringComparison.OrdinalIgnoreCase)).ToList();
        return prefix.Count == 1 ? prefix[0] : null;
    }

    private PipeResponse NotFound(string nameOrId)
    {
        var near = _library.All.Where(s => s.Name.Contains(nameOrId, StringComparison.OrdinalIgnoreCase)).Take(5).Select(s => $"\"{s.Name}\"").ToList();
        var hint = near.Count > 0 ? " Did you mean " + string.Join(", ", near) + "?" : " Run \"ap list\" to see your shortcuts.";
        return PipeResponse.Error(CliExit.NotFound, $"Couldn't find a shortcut called \"{nameOrId}\".{hint}");
    }

    // ---- list --------------------------------------------------------------------------------

    private PipeResponse List(bool json)
    {
        var folders = _library.Folders.ToDictionary(f => f.Id, f => f.Name);
        var items = _library.All.Select(s => new
        {
            id = s.Id, name = s.Name, folder = s.FolderId is { } f && folders.TryGetValue(f, out var n) ? n : null,
            alias = s.Alias, hotkey = s.Hotkey, pinned = s.Pinned, description = s.Description,
        }).ToList();

        if (json) return PipeResponse.Done(JsonSerializer.Serialize(items, JsonOut));
        if (items.Count == 0) return PipeResponse.Done("", "You don't have any shortcuts yet.");

        var width = Math.Min(40, items.Max(i => i.name.Length));
        var sb = new StringBuilder();
        foreach (var i in items)
            sb.AppendLine($"{Trim(i.name, width).PadRight(width)}  {i.id:D}{(i.alias is { Length: > 0 } a ? "  (" + a + ")" : "")}");
        return PipeResponse.Done(sb.ToString().TrimEnd());
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    // ---- run ---------------------------------------------------------------------------------

    private async Task<PipeResponse> RunAsync(PipeRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return PipeResponse.Error(CliExit.Usage, "Say which shortcut to run, for example: ap run \"Good Morning\".");
        var shortcut = Find(r.Name);
        if (shortcut is null) return NotFound(r.Name);

        if (r.Ask)
        {
            if (!Callers.IsValidId(r.Source)) return PipeResponse.Error(CliExit.Usage, "To ask first, say which app is asking. " + Callers.InvalidIdMessage);
            if (Consent is null) return PipeResponse.Error(CliExit.Problem, "Automation Portal needs to be open to ask you first. Open it and try again.");
            var prompt = new CallerPrompt(r.Source!, shortcut.Name, r.Files, !string.IsNullOrEmpty(r.Input))
            {
                ShortcutId = shortcut.Id, ChangesFiles = ChangesFiles?.Invoke(shortcut) == true,
            };
            var verdict = await Consent.RequestAsync(prompt, ct).ConfigureAwait(false);
            if (verdict != CallerVerdict.Allowed) return Consent.NotRun(verdict);
        }

        var input = r.Files.Length > 0
            ? (string.IsNullOrEmpty(r.Input) ? ContentValue.Files(r.Files) : ContentValue.Concat(ContentValue.Text(r.Input), ContentValue.Files(r.Files)))
            : string.IsNullOrEmpty(r.Input) ? ContentValue.Empty : ContentValue.Text(r.Input);

        if (!r.Wait)
        {
            _ = _run(shortcut, input, RunSource.Cli, CancellationToken.None).ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            return PipeResponse.Done(r.Json ? JsonSerializer.Serialize(new { shortcut = shortcut.Name, status = "started" }, JsonOut) : null, $"Started \"{shortcut.Name}\".");
        }

        var result = await _run(shortcut, input, RunSource.Cli, ct).ConfigureAwait(false);
        if (result is null) return PipeResponse.Error(CliExit.Failed, $"\"{shortcut.Name}\" is already running.");

        var text = result.Output.AsText();
        var code = result.Status switch { RunStatus.Succeeded => CliExit.Ok, RunStatus.Failed => CliExit.Failed, _ => CliExit.Cancelled };
        var message = result.Status switch
        {
            RunStatus.Failed => result.ErrorMessage ?? "The shortcut didn't finish.",
            RunStatus.Cancelled => "The shortcut was cancelled.",
            RunStatus.Stopped => "The shortcut stopped before it finished.",
            _ => null,
        };
        var output = r.Json
            ? JsonSerializer.Serialize(new
            {
                shortcut = result.ShortcutName, id = result.ShortcutId, status = result.Status.ToString().ToLowerInvariant(),
                output = text, error = result.ErrorMessage, durationMs = (long)result.Duration.TotalMilliseconds, runId = result.RunId,
            }, JsonOut)
            : text;
        return new PipeResponse { ExitCode = code, Output = output, Message = r.Json ? null : message };
    }

    // ---- new ---------------------------------------------------------------------------------

    /// <summary>
    /// Opens the editor on a new, unsaved shortcut with the files as its test input. Runs nothing, saves nothing and never opens or
    /// changes the files; files that aren't there are allowed and noted. Never asks first, since nothing can happen without the user.
    /// </summary>
    private async Task<PipeResponse> NewAsync(PipeRequest r, CancellationToken ct)
    {
        if (r.Files.Length == 0) return PipeResponse.Error(CliExit.Usage, "Say which files the new shortcut is for, for example: ap new --file \"C:\\Photos\\a.jpg\".");
        var (paths, problem) = LaunchArguments.CheckNewShortcutPaths(r.Files);
        if (problem is not null) return PipeResponse.Error(CliExit.Usage, problem);
        if (OpenNewShortcut is null) return PipeResponse.Error(CliExit.Problem, "Automation Portal needs to be open to create a shortcut. Open it and try again.");

        var missing = LaunchArguments.MissingNewShortcutFiles(paths);
        var caller = Callers.IsValidId(r.Source) && r.Source != "cli" ? r.Source : null;
        await OpenNewShortcut(new NewShortcutRequest(paths, missing, caller), ct).ConfigureAwait(false);

        var files = paths.Count == 1 ? "1 file" : $"{paths.Count} files";
        var note = missing.Count == 0 ? "" : missing.Count == 1 ? " 1 of them couldn't be found." : $" {missing.Count} of them couldn't be found.";
        var output = r.Json ? JsonSerializer.Serialize(new { status = "opened", files = paths.Count, missing = missing.Count }, JsonOut) : null;
        return PipeResponse.Done(output, $"Opened a new shortcut in Automation Portal to try on {files}. Nothing has run.{note}");
    }

    // ---- event -------------------------------------------------------------------------------

    /// <summary>Fire-and-forget whatever <see cref="PipeRequest.Wait"/> says: the reply never waits for an automation.</summary>
    private PipeResponse Event(PipeRequest r)
    {
        if (!AppEvents.TryParse(r, DateTimeOffset.Now, out var ev, out var problem)) return problem!;
        try { PublishEvent?.Invoke(ev!); }
        catch (Exception) { /* a listener must never turn an accepted event into an error for the sender */ }
        return PipeResponse.Done();
    }

    // ---- import / export ---------------------------------------------------------------------

    /// <summary>
    /// Imports never replace a shortcut: the library gives every import a new id and a free name. With <see cref="ReviewImport"/> set,
    /// nothing is added until the user agrees; without wait the reply comes straight away, while the review is still open.
    /// </summary>
    private async Task<PipeResponse> ImportAsync(PipeRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Input)) return PipeResponse.Error(CliExit.Usage, "There was nothing to import.");
        if (ReviewImport is null) return Import(r);

        Shortcut preview;
        try { preview = ShortcutPackage.Parse(r.Input); }
        catch (LibraryException ex) { return PipeResponse.Error(CliExit.Problem, ex.Message); }

        var review = ReviewImport(r.Input, r);
        if (!r.Wait)
        {
            _ = review.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            return PipeResponse.Done(r.Json ? JsonSerializer.Serialize(new { name = preview.Name, status = "review" }, JsonOut) : null,
                $"Automation Portal is showing \"{preview.Name}\" so you can decide whether to add it.");
        }

        ImportResult? result;
        try { result = await review.WaitAsync(ct).ConfigureAwait(false); }
        catch (LibraryException ex) { return PipeResponse.Error(CliExit.Problem, ex.Message); }
        if (result is null)
            return new PipeResponse
            {
                ExitCode = CliExit.Cancelled, Output = r.Json ? JsonSerializer.Serialize(new { name = preview.Name, status = "declined" }, JsonOut) : null,
                Message = $"\"{preview.Name}\" wasn't added.",
            };
        return Imported(result, r.Json);
    }

    private PipeResponse Import(PipeRequest r)
    {
        try { return Imported(_library.ImportJson(r.Input!), r.Json); }
        catch (LibraryException ex) { return PipeResponse.Error(CliExit.Problem, ex.Message); }
    }

    private static PipeResponse Imported(ImportResult result, bool json)
    {
        var note = result.RenamedFrom is null ? "" : $" You already had \"{result.RenamedFrom}\", so it was renamed.";
        var warn = result.Warnings.Count == 0 ? ""
            : " Heads up: it includes " + string.Join(", ", result.Warnings.Select(w => w.Title).Distinct()) + ".";
        var extras = new List<string>();
        if (result.PendingHotkey is { Length: > 0 } hk) extras.Add($"keyboard shortcut {hk}");
        if (result.PendingAlias is { Length: > 0 } al) extras.Add($"Quick Run name \"{al}\"");
        if (result.PendingShowInSendTo) extras.Add("Send to entry");
        if (extras.Count > 0) warn += " Left off for safety: " + string.Join(", ", extras) + ". Turn them on in Automation Portal if you trust this shortcut.";
        var output = json ? JsonSerializer.Serialize(new { id = result.Shortcut.Id, name = result.Shortcut.Name, status = "added" }, JsonOut) : null;
        return PipeResponse.Done(output, $"Added \"{result.Shortcut.Name}\" to your shortcuts.{note}{warn}");
    }

    private PipeResponse Export(PipeRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return PipeResponse.Error(CliExit.Usage, "Say which shortcut to export, for example: ap export \"Good Morning\" file.autoportal.");
        var shortcut = Find(r.Name);
        if (shortcut is null) return NotFound(r.Name);
        try { return PipeResponse.Done(_library.Export(shortcut.Id)); }
        catch (LibraryException ex) { return PipeResponse.Error(CliExit.Problem, ex.Message); }
    }
}

/// <summary>A "new" request for the app: the files to try the new shortcut on, those that couldn't be found, and the app that sent them (null for ap or a link).</summary>
public sealed record NewShortcutRequest(IReadOnlyList<string> Files, IReadOnlyList<string> Missing, string? CallerId);
