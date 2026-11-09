using System.Text;
using System.Text.Json;

namespace Flowmate.Core;

/// <summary>
/// The CLI's commands (list, run, import, export), shared by the running app (answering over the pipe) and the
/// in-process <c>fm</c>, so both behave and print the same.
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

    public Task<PipeResponse> ExecuteAsync(PipeRequest request, CancellationToken ct) => request.Kind switch
    {
        "ping" => Task.FromResult(PipeResponse.Done()),
        "list" => Task.FromResult(List(request.Json)),
        "run" => RunAsync(request, ct),
        "import" => Task.FromResult(Import(request)),
        "export" => Task.FromResult(Export(request)),
        _ => Task.FromResult(PipeResponse.Error(CliExit.Usage, $"Flow Mate doesn't know the command \"{request.Kind}\".")),
    };

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
        var hint = near.Count > 0 ? " Did you mean " + string.Join(", ", near) + "?" : " Run \"fm list\" to see your shortcuts.";
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
        if (string.IsNullOrWhiteSpace(r.Name)) return PipeResponse.Error(CliExit.Usage, "Say which shortcut to run, for example: fm run \"Good Morning\".");
        var shortcut = Find(r.Name);
        if (shortcut is null) return NotFound(r.Name);

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

    // ---- import / export ---------------------------------------------------------------------

    private PipeResponse Import(PipeRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Input)) return PipeResponse.Error(CliExit.Usage, "There was nothing to import.");
        try
        {
            var result = _library.ImportJson(r.Input);
            var note = result.RenamedFrom is null ? "" : $" You already had \"{result.RenamedFrom}\", so it was renamed.";
            var warn = result.Warnings.Count == 0 ? ""
                : " Heads up: it includes " + string.Join(", ", result.Warnings.Select(w => w.Title).Distinct()) + ".";
            var extras = new List<string>();
            if (result.PendingHotkey is { Length: > 0 } hk) extras.Add($"keyboard shortcut {hk}");
            if (result.PendingAlias is { Length: > 0 } al) extras.Add($"Quick Run name \"{al}\"");
            if (result.PendingShowInSendTo) extras.Add("Send to entry");
            if (extras.Count > 0) warn += " Left off for safety: " + string.Join(", ", extras) + ". Turn them on in Flow Mate if you trust this shortcut.";
            var output = r.Json ? JsonSerializer.Serialize(new { id = result.Shortcut.Id, name = result.Shortcut.Name }, JsonOut) : null;
            return PipeResponse.Done(output, $"Added \"{result.Shortcut.Name}\" to your shortcuts.{note}{warn}");
        }
        catch (LibraryException ex) { return PipeResponse.Error(CliExit.Problem, ex.Message); }
    }

    private PipeResponse Export(PipeRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return PipeResponse.Error(CliExit.Usage, "Say which shortcut to export, for example: fm export \"Good Morning\" file.flowmate.");
        var shortcut = Find(r.Name);
        if (shortcut is null) return NotFound(r.Name);
        try { return PipeResponse.Done(_library.Export(shortcut.Id)); }
        catch (LibraryException ex) { return PipeResponse.Error(CliExit.Problem, ex.Message); }
    }
}
