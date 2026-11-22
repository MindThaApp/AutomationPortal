using System.Text.Json;

namespace AutomationPortal.Core;

public enum PickKind { OpenFile, OpenFiles, Folder, SaveFile }

/// <param name="Owner">The window the dialog belongs to (a WPF Window), or null.</param>
public sealed record PickRequest(PickKind Kind, string Title, string? Filter = null, string? InitialDirectory = null, string? SuggestedName = null, object? Owner = null);

/// <summary>Every file, folder and save picker goes through this. Returns the chosen paths, or null when cancelled.</summary>
public interface IPicker
{
    IReadOnlyList<string>? Pick(PickRequest request);
}

/// <summary>
/// The picker used under a custom data root: no dialog is shown. Answers come from <c>&lt;root&gt;\picker-answers.json</c>,
/// a JSON array used as a queue (each item a path, or an array of paths for several files). An answer outside the root,
/// or an empty queue, is a cancel. Each request is logged to <c>&lt;root&gt;\picker-requests.log</c>.
/// </summary>
public sealed class ScriptedPicker : IPicker
{
    private readonly string _root;
    private readonly object _gate = new();

    public ScriptedPicker(string root) => _root = Path.GetFullPath(root).TrimEnd('\\', '/');

    public string AnswersFile => Path.Combine(_root, "picker-answers.json");
    public string LogFile => Path.Combine(_root, "picker-requests.log");

    public IReadOnlyList<string>? Pick(PickRequest request)
    {
        lock (_gate)
        {
            var (answer, left) = Dequeue();
            string[]? paths = answer;
            string outcome;
            if (paths is null || paths.Length == 0) { paths = null; outcome = "cancel (no answer queued)"; }
            else
            {
                paths = paths.Select(p => Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(_root, p))).ToArray();
                if (!paths.All(IsInside)) { outcome = "cancel (outside the test root): " + string.Join(" | ", paths); paths = null; }
                else if (request.Kind != PickKind.OpenFiles && paths.Length > 1) { paths = new[] { paths[0] }; outcome = "answer: " + paths[0]; }
                else outcome = "answer: " + string.Join(" | ", paths);
            }
            WriteLog($"{DateTime.Now:s} {request.Kind} \"{request.Title}\" -> {outcome} ({left} left)");
            return paths;
        }
    }

    private bool IsInside(string full) =>
        full.Equals(_root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private (string[]? Answer, int Left) Dequeue()
    {
        try
        {
            if (!File.Exists(AnswersFile)) return (null, 0);
            var items = JsonSerializer.Deserialize<List<JsonElement>>(File.ReadAllText(AnswersFile)) ?? new();
            if (items.Count == 0) return (null, 0);
            var head = items[0];
            items.RemoveAt(0);
            AtomicFile.WriteAllText(AnswersFile, JsonSerializer.Serialize(items));
            return head.ValueKind switch
            {
                JsonValueKind.String => (new[] { head.GetString()! }, items.Count),
                JsonValueKind.Array => (head.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToArray(), items.Count),
                _ => (null, items.Count),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return (null, 0); }
    }

    private void WriteLog(string line)
    {
        try { File.AppendAllText(LogFile, line + Environment.NewLine); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>Where pickers come from. Under a custom data root it is always the <see cref="ScriptedPicker"/>.</summary>
public static class PickerHost
{
    /// <summary>The real dialogs, set once by the app. Only used on a normal launch.</summary>
    public static IPicker? System { get; set; }

    public static IPicker Current =>
        UserFolders.Root is { } root ? new ScriptedPicker(root)
        : System ?? throw new InvalidOperationException("No picker has been set up.");
}
