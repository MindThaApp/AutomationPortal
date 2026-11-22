using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace AutomationPortal.Core;

/// <summary>
/// How shortcuts are described to other apps (pipe contract 1.1): a stable tool name, a JSON Schema for what they take, and a
/// dry-run description of what they would do that never runs a step.
/// </summary>
public static class ShortcutTools
{
    public const int MaxToolNameLength = 64;

    /// <summary>
    /// A tool name for every shortcut: lower-case a-z, 0-9 and _, at most 64 characters, unique. The oldest shortcut keeps the plain
    /// name; a later one whose name gives the same tool name gets part of its id added, so names don't move when shortcuts are added.
    /// </summary>
    public static IReadOnlyDictionary<Guid, string> ToolNames(IEnumerable<Shortcut> shortcuts)
    {
        var names = new Dictionary<Guid, string>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in shortcuts.OrderBy(s => s.CreatedAt).ThenBy(s => s.Id))
        {
            var stem = BaseToolName(s.Name);
            var hex = s.Id.ToString("N");
            string? name = null;
            foreach (var n in new[] { 0, 8, 12, 32 })
            {
                var candidate = n == 0 ? stem : Cut(stem, MaxToolNameLength - n - 1) + "_" + hex[..n];
                if (used.Add(candidate)) { name = candidate; break; }
            }
            for (var i = 2; name is null; i++)
            {
                var candidate = Cut(stem, MaxToolNameLength - 34 - i.ToString(CultureInfo.InvariantCulture).Length) + "_" + hex + "_" + i.ToString(CultureInfo.InvariantCulture);
                if (used.Add(candidate)) name = candidate;
            }
            names[s.Id] = name;
        }
        return names;
    }

    /// <summary>"Tidy Downloads!" becomes "tidy_downloads"; accents are dropped, other characters become _, empty becomes "shortcut".</summary>
    public static string BaseToolName(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in (name ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            var lower = char.ToLowerInvariant(c);
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(lower);
            else if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
        }
        var text = Cut(sb.ToString(), MaxToolNameLength);
        return text.Length == 0 ? "shortcut" : text;
    }

    private static string Cut(string s, int max) => (s.Length <= max ? s : s[..max]).Trim('_') is { Length: > 0 } t ? t : "shortcut";

    /// <summary>
    /// A JSON Schema object for what the shortcut takes: <c>text</c> (a string), <c>files</c> (full paths) or <c>folder</c> (one full
    /// path), from the kinds it accepts as input. A shortcut that takes no input gets an object with no properties.
    /// </summary>
    public static JsonObject InputSchema(Shortcut shortcut)
    {
        var kinds = shortcut.AcceptsInput;
        var any = kinds.Contains(ContentKind.Any);
        var text = any || kinds.Any(k => k is ContentKind.Text or ContentKind.Number or ContentKind.Boolean or ContentKind.Date or ContentKind.Url
            or ContentKind.Dictionary or ContentKind.Location);
        var files = any || kinds.Any(k => k is ContentKind.File or ContentKind.Image);
        var folder = any || kinds.Contains(ContentKind.Folder);
        var whenEmpty = shortcut.WhenNoInput switch
        {
            WhenNoInput.AskForFiles => " Leave it out and Automation Portal asks you to pick them.",
            WhenNoInput.UseClipboard => " Leave it out and the shortcut uses what is on the clipboard.",
            _ => "",
        };

        var properties = new JsonObject();
        if (text)
            properties["text"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Text handed to the shortcut as its input" + (kinds.Contains(ContentKind.Url) && !any ? " (for example a link)." : ".") + (files || folder ? "" : whenEmpty),
            };
        if (files)
            properties["files"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" },
                ["description"] = $"Full paths of the {(folder ? "files or folders" : kinds.Contains(ContentKind.Image) && !kinds.Contains(ContentKind.File) && !any ? "pictures" : "files")} to work on." + whenEmpty,
            };
        else if (folder)
            properties["folder"] = new JsonObject { ["type"] = "string", ["description"] = "Full path of the folder to work on." + whenEmpty };

        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (shortcut.WhenNoInput == WhenNoInput.StopAndSay && properties.Count > 0)
        {
            if (properties.Count == 1) schema["required"] = new JsonArray(properties.Single().Key);
            else schema["minProperties"] = 1;
        }
        schema["additionalProperties"] = false;
        if (AsksUser(shortcut)) schema["description"] = "While it runs, the shortcut also asks you questions in Automation Portal.";
        return schema;
    }

    /// <summary>True when a step asks the person something while the shortcut runs.</summary>
    public static bool AsksUser(Shortcut shortcut) =>
        ShortcutEffects.CountedSteps(shortcut).Any(x => x.ActionId.StartsWith("ui.ask", StringComparison.OrdinalIgnoreCase)
                                                        || x.ActionId.StartsWith("ui.choose", StringComparison.OrdinalIgnoreCase)
                                                        || x.ActionId.Equals(FlowIds.Menu, StringComparison.OrdinalIgnoreCase));

    /// <summary>One sentence on what the shortcut does to files, for tool descriptions and the request sheet.</summary>
    public static string EffectSentence(ShortcutEffect effect) => effect switch
    {
        ShortcutEffect.MakesCopies => "It saves new files and leaves the originals as they are.",
        ShortcutEffect.ChangesFiles => "It changes files on this PC.",
        _ => "It doesn't change files.",
    };

    /// <summary>The tool's description: the shortcut's own description (or name), what it does to files, and that you are asked first.</summary>
    public static string ToolDescription(Shortcut shortcut, ShortcutEffect effect, bool asksFirst)
    {
        var own = string.IsNullOrWhiteSpace(shortcut.Description) ? $"Runs the Automation Portal shortcut \"{shortcut.Name}\"." : shortcut.Description.Trim();
        if (!own.EndsWith('.') && !own.EndsWith('!') && !own.EndsWith('?')) own += ".";
        var text = own + " " + EffectSentence(effect);
        if (asksFirst) text += " Automation Portal asks you before it runs.";
        return text;
    }

    /// <summary>
    /// What the shortcut would do with this input, without running any step: each step with what it does (reads, changes files, makes
    /// copies, controls other apps, or nothing to files). <paramref name="describeStep"/> gives a step's sentence; the step's title is used without it.
    /// </summary>
    public static ShortcutDryRun DryRun(Shortcut shortcut, string? text, IReadOnlyList<string> files, ActionRegistry? actions, Func<ActionInstance, string?>? describeStep = null)
    {
        var steps = new List<DryRunStep>();
        var n = 0;
        foreach (var x in ShortcutEffects.CountedSteps(shortcut))
        {
            if (x.ActionId.Equals(FlowIds.Comment, StringComparison.OrdinalIgnoreCase)) continue;
            var effect = ShortcutEffects.Of(x, actions);
            var title = actions?.Describe(x.ActionId)?.Title ?? x.ActionId;
            string? sentence = null;
            try { sentence = describeStep?.Invoke(x); }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
            steps.Add(new DryRunStep(++n, title, string.IsNullOrWhiteSpace(sentence) ? title : sentence.Trim(), effect));
        }
        var shortcutEffect = ShortcutEffects.Of(shortcut, actions);
        return new ShortcutDryRun(shortcut.Name, shortcutEffect, text, files, steps, AsksUser(shortcut));
    }
}

/// <summary>One step of a <see cref="ShortcutDryRun"/>.</summary>
public sealed record DryRunStep(int Number, string Title, string Text, StepEffect Effect)
{
    public string EffectText => Effect switch
    {
        StepEffect.Reads => "only reads",
        StepEffect.NoFileChanges => "doesn't change files",
        StepEffect.MakesCopies => "saves new files",
        StepEffect.ControlsApps => "clicks and types in other apps",
        StepEffect.Flow => "decides what happens next",
        _ => "changes files",
    };
}

/// <summary>What a shortcut would do, worked out from its steps alone: nothing was run, read or opened.</summary>
public sealed record ShortcutDryRun(string Shortcut, ShortcutEffect Effect, string? Text, IReadOnlyList<string> Files, IReadOnlyList<DryRunStep> Steps, bool AsksUser)
{
    /// <summary>Plain text for the terminal and the request sheet.</summary>
    public string Describe(int maxSteps = 30)
    {
        var sb = new StringBuilder();
        var on = Files.Count switch { 0 => string.IsNullOrEmpty(Text) ? "" : " with the text you gave", 1 => " on 1 item", var c => $" on {c} items" };
        sb.Append($"\"{Shortcut}\" would run{on}. {ShortcutTools.EffectSentence(Effect)}");
        if (Steps.Count == 0) sb.Append(" It has no steps.");
        foreach (var s in Steps.Take(maxSteps)) sb.Append($"\n{s.Number}. {s.Text} ({s.EffectText})");
        if (Steps.Count > maxSteps) sb.Append($"\n…and {Steps.Count - maxSteps} more steps");
        if (AsksUser) sb.Append("\nIt asks you questions while it runs.");
        sb.Append("\nNothing has run: this comes from the shortcut's steps, not from looking at the files.");
        return sb.ToString();
    }
}
