using System.Collections.Concurrent;

namespace AutomationPortal.Core;

/// <summary>State of one shortcut run: named variables, step outputs, repeat/menu stacks, temp folder. Created by the runner (and by the test harness).</summary>
public sealed class RunScope
{
    internal static readonly ConcurrentDictionary<string, byte> ActiveTempFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private string? _tempFolder;

    public RunScope(AutomationPortalServices services, string shortcutName, ContentValue shortcutInput, RunOptions? options = null, Guid shortcutId = default)
    {
        Services = services;
        ShortcutName = shortcutName;
        ShortcutId = shortcutId;
        ShortcutInput = shortcutInput;
        Options = options ?? new RunOptions();
        RunId = Options.RunId ?? Guid.NewGuid().ToString("N")[..12];
    }

    public AutomationPortalServices Services { get; }
    public string ShortcutName { get; }
    public Guid ShortcutId { get; }
    public ContentValue ShortcutInput { get; }
    public RunOptions Options { get; }
    public string RunId { get; }
    public CancellationToken Token => Options.CancellationToken;
    public IRunObserver? Observer => Options.Observer;

    /// <summary>Named variables (case-insensitive).</summary>
    public ConcurrentDictionary<string, ContentValue> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Latest output of each executed action, by uid (magic variables).</summary>
    public ConcurrentDictionary<string, ContentValue> Outputs { get; } = new();
    /// <summary>Uids of the steps whose output is private (<see cref="ActionDescriptor.SensitiveOutput"/>).</summary>
    internal ConcurrentDictionary<string, byte> SensitiveOutputs { get; } = new();
    internal Stack<(ContentValue Item, int Index)> RepeatStack { get; } = new();
    internal Stack<string> MenuStack { get; } = new();

    /// <summary>Picks from a preview just before that this run uses instead of asking (set by the runner once it has checked them).</summary>
    internal PreviewPicks? ReplayPicks { get; set; }
    private readonly Dictionary<string, int> _pickAsks = new(StringComparer.Ordinal);

    /// <summary>
    /// The key of a file or folder ask by <paramref name="stepUid"/> for <paramref name="paramKey"/>: the same in a preview and in the
    /// real run that follows it, as long as the step asks for the same repeat items in the same order.
    /// </summary>
    internal string NextPickKey(string stepUid, string paramKey)
    {
        var context = string.Join('\u001e', RepeatStack.Select(r => r.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + ShortHash(r.Item.AsText())));
        var baseKey = PreviewPicks.KeyFor(stepUid, paramKey, context, 0);
        lock (_gate)
        {
            _pickAsks.TryGetValue(baseKey, out var n);
            _pickAsks[baseKey] = n + 1;
            return PreviewPicks.KeyFor(stepUid, paramKey, context, n);
        }
    }

    private static string ShortHash(string text) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))[..16];

    /// <summary>Per-run temp folder (created on first use, deleted when the run ends).</summary>
    public string TempFolder
    {
        get
        {
            if (Options.SharedTempFolder is { } shared) { Directory.CreateDirectory(shared); return shared; }
            lock (_gate)
            {
                if (_tempFolder is null)
                {
                    _tempFolder = Services.Paths.RunTempFolder(RunId);
                    Directory.CreateDirectory(_tempFolder);
                    ActiveTempFolders[_tempFolder] = 0;
                }
                return _tempFolder;
            }
        }
    }

    private RunFileJournal? _journal;

    /// <summary>The record of file changes for "Undo file changes" (the parent run's for nested runs).</summary>
    public RunFileJournal Journal
    {
        get
        {
            if (Options.SharedJournal is { } shared) return shared;
            lock (_gate) return _journal ??= new RunFileJournal(new FileJournal(Services.Paths), RunId, Services.Paths.TempRoot);
        }
    }

    /// <summary>Seals this run's own journal (not a shared one) if anything was recorded.</summary>
    internal void SealJournal()
    {
        RunFileJournal? j;
        lock (_gate) j = _journal;
        j?.Seal();
    }

    /// <summary>The temp folder if this run created one (null otherwise); does not create it.</summary>
    internal string? CreatedTempFolder { get { lock (_gate) return _tempFolder; } }

    public void SetVariable(string name, ContentValue value)
    {
        Variables[name] = value;
        Observer?.OnVariableSet(name, value);
    }

    public ContentValue GetVariable(string name) => Variables.TryGetValue(name, out var v) ? v : ContentValue.Empty;

    internal void CleanupTemp()
    {
        string? folder;
        lock (_gate) { folder = _tempFolder; _tempFolder = null; }
        if (folder is null) return;
        ActiveTempFolders.TryRemove(folder, out _);
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); } catch { /* files may still be locked; leftover temp is harmless */ }
    }

    /// <summary>Resolves a variable reference to its current value (with property, pick and coercion applied).</summary>
    public async Task<ContentValue> ResolveAsync(VarRef r)
    {
        var value = r.Kind switch
        {
            VarKind.Output => r.Uid is not null && Outputs.TryGetValue(r.Uid, out var o) ? o : ContentValue.Empty,
            VarKind.Named => r.Name is not null ? GetVariable(r.Name) : ContentValue.Empty,
            VarKind.Input => ShortcutInput,
            VarKind.Clipboard => Services.ClipboardOrNull is { } cb ? await cb.GetAsync().ConfigureAwait(false) : ContentValue.Empty,
            VarKind.CurrentDate => ContentValue.Date(DateTimeOffset.Now),
            VarKind.RepeatItem => RepeatStack.Count > 0 ? RepeatStack.Peek().Item : ContentValue.Empty,
            VarKind.RepeatIndex => RepeatStack.Count > 0 ? ContentValue.Number(RepeatStack.Peek().Index) : ContentValue.Empty,
            VarKind.MenuResult => MenuStack.Count > 0 ? ContentValue.Text(MenuStack.Peek()) : ContentValue.Empty,
            VarKind.Device => DeviceInfo(),
            _ => ContentValue.Empty,
        };
        if (r.Property is not null) value = value.Property(r.Property);
        if (r.Pick != VarPick.All) value = value.Pick(r.Pick);
        if (r.CoerceTo is { } kind) value = value.CoerceTo(kind);
        return value;
    }

    private static ContentValue DeviceInfo() => ContentValue.Dict(
        ("Name", ContentValue.Text(Environment.MachineName)),
        ("User", ContentValue.Text(Environment.UserName)),
        ("System", ContentValue.Text(System.Runtime.InteropServices.RuntimeInformation.OSDescription)));
}
