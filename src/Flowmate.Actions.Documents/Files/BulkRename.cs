using System.Globalization;
using System.Text.RegularExpressions;

namespace Flowmate.Actions.Documents;

public sealed record BulkRenameOptions
{
    public string Pattern { get; init; } = "{name}.{ext}";
    public int Start { get; init; } = 1;
    public int Padding { get; init; }
    public string DateFormat { get; init; } = "yyyy-MM-dd";
    public string Find { get; init; } = "";
    public string ReplaceWith { get; init; } = "";
    public bool UseRegex { get; init; }
    public bool MatchCase { get; init; }
    public string CaseStyle { get; init; } = "none";
    public bool AddNumberOnCollision { get; init; } = true;
}

/// <summary>A planned rename. Target equals Source when nothing changes; Skipped means a collision kept the old name.</summary>
public sealed record BulkRenameStep(string Source, string Target, bool Skipped);

/// <summary>Plans and runs a bulk rename: names are all worked out first, nothing is overwritten, swaps work and a failure rolls back.</summary>
public static class BulkRenamer
{
    private static readonly Regex TokenRx = new(@"\{(name|ext|n|date|parent)\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static IReadOnlyList<BulkRenameStep> Plan(IReadOnlyList<string> files, BulkRenameOptions o)
    {
        var wanted = new List<(string Source, string Name)>();
        Regex? rx = null;
        if (o.Find.Length > 0)
        {
            try
            {
                rx = new Regex(o.UseRegex ? o.Find : Regex.Escape(o.Find),
                    o.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
            }
            catch (ArgumentException ex) { throw new ActionException($"The text to find isn't a valid pattern. ({ex.Message})", ex); }
        }

        for (var i = 0; i < files.Count; i++)
        {
            var path = files[i];
            var name = Expand(o, path, o.Start + i);
            if (rx is not null)
            {
                try { name = rx.Replace(name, o.UseRegex ? o.ReplaceWith : o.ReplaceWith.Replace("$", "$$")); }
                catch (RegexMatchTimeoutException) { throw new ActionException("The find and replace pattern took too long. Try a simpler one."); }
                catch (ArgumentException ex) { throw new ActionException($"The replacement text isn't valid. ({ex.Message})", ex); }
            }
            name = ApplyCase(name, o.CaseStyle);
            Validate(name, path);
            wanted.Add((path, name));
        }

        var held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < wanted.Count; i++)
            if (string.Equals(Path.GetFileName(wanted[i].Source), wanted[i].Name, StringComparison.Ordinal)) held.Add(wanted[i].Source);

        // In skip mode a skipped file keeps its name, which may be wanted by a file planned earlier, so plan again.
        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var steps = Assign(wanted, held, skipped, o.AddNumberOnCollision);
            var fresh = steps.Where(s => s.Skipped && !skipped.Contains(s.Source)).ToList();
            if (fresh.Count == 0) return steps;
            foreach (var s in fresh) { skipped.Add(s.Source); held.Add(s.Source); }
        }
    }

    private static List<BulkRenameStep> Assign(List<(string Source, string Name)> wanted, HashSet<string> held, HashSet<string> skipped, bool number)
    {
        var sources = new HashSet<string>(wanted.Select(w => w.Source), StringComparer.OrdinalIgnoreCase);
        var taken = new HashSet<string>(held, StringComparer.OrdinalIgnoreCase);
        var result = new BulkRenameStep?[wanted.Count];
        for (var i = 0; i < wanted.Count; i++)
            if (held.Contains(wanted[i].Source)) result[i] = new BulkRenameStep(wanted[i].Source, wanted[i].Source, skipped.Contains(wanted[i].Source));

        for (var i = 0; i < wanted.Count; i++)
        {
            if (result[i] is not null) continue;
            var (src, name) = wanted[i];
            var dir = Path.GetDirectoryName(src)!;
            var candidate = Path.Combine(dir, name);
            if (Blocked(candidate))
            {
                if (!number) { result[i] = new BulkRenameStep(src, src, true); continue; }
                var stem = Path.GetFileNameWithoutExtension(name);
                var ext = Path.GetExtension(name);
                for (var n = 2; Blocked(candidate); n++) candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
            }
            taken.Add(candidate);
            result[i] = new BulkRenameStep(src, candidate, false);
        }
        return result!.ToList();

        bool Blocked(string c) => taken.Contains(c) || ((File.Exists(c) || Directory.Exists(c)) && !sources.Contains(c));
    }

    public static void Execute(IReadOnlyList<BulkRenameStep> plan, CancellationToken ct = default)
    {
        var changes = plan.Where(s => !s.Skipped && !string.Equals(s.Source, s.Target, StringComparison.Ordinal)).ToList();
        if (changes.Count == 0) return;
        var sourceSet = new HashSet<string>(changes.Select(c => c.Source), StringComparer.OrdinalIgnoreCase);
        var viaTemp = changes.Any(c => sourceSet.Contains(c.Target));
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var done = new List<(string From, string To)>();
        try
        {
            if (!viaTemp)
            {
                foreach (var c in changes) { ct.ThrowIfCancellationRequested(); Move(c.Source, c.Target, done); }
                return;
            }
            var parked = new List<(string Temp, string Target)>();
            for (var i = 0; i < changes.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var temp = Path.Combine(Path.GetDirectoryName(changes[i].Source)!, $"~flowmate-{stamp}-{i}.tmp");
                Move(changes[i].Source, temp, done);
                parked.Add((temp, changes[i].Target));
            }
            foreach (var (temp, target) in parked) { ct.ThrowIfCancellationRequested(); Move(temp, target, done); }
        }
        catch
        {
            for (var i = done.Count - 1; i >= 0; i--)
            {
                try { File.Move(done[i].To, done[i].From); } catch { /* best effort rollback */ }
            }
            throw;
        }
    }

    private static void Move(string from, string to, List<(string, string)> done)
    {
        File.Move(from, to);
        done.Add((from, to));
    }

    private static string Expand(BulkRenameOptions o, string path, int n)
    {
        var ext = Path.GetExtension(path).TrimStart('.');
        var result = TokenRx.Replace(o.Pattern, m =>
        {
            switch (m.Groups[1].Value.ToLowerInvariant())
            {
                case "name": return Path.GetFileNameWithoutExtension(path);
                case "ext": return ext;
                case "n": return n.ToString("D" + Math.Clamp(o.Padding, 0, 20), CultureInfo.InvariantCulture);
                case "parent": return Path.GetFileName(Path.GetDirectoryName(path)) ?? "";
                default:
                    try { return File.GetLastWriteTime(path).ToString(o.DateFormat, CultureInfo.InvariantCulture); }
                    catch (FormatException) { throw new ActionException($"'{o.DateFormat}' isn't a valid date format. Try yyyy-MM-dd."); }
            }
        });
        return ext.Length == 0 && result.EndsWith('.') ? result[..^1] : result;
    }

    private static string ApplyCase(string name, string style)
    {
        var ext = Path.GetExtension(name);
        var stem = name[..^ext.Length];
        stem = style switch
        {
            "lower" => stem.ToLowerInvariant(),
            "upper" => stem.ToUpperInvariant(),
            "title" => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(stem.ToLowerInvariant()),
            _ => stem,
        };
        return stem + ext;
    }

    private static void Validate(string name, string source)
    {
        var shown = Path.GetFileName(source);
        if (string.IsNullOrWhiteSpace(name)) throw new ActionException($"The new name for '{shown}' would be empty.");
        var bad = name.FirstOrDefault(c => Path.GetInvalidFileNameChars().Contains(c));
        if (bad != default)
            throw new ActionException($"The new name '{name}' for '{shown}' contains a character Windows doesn't allow ({(char.IsControl(bad) ? "control character" : bad.ToString())}). File names can't contain \\ / : * ? \" < > |");
        if (name.EndsWith('.') || name.EndsWith(' '))
            throw new ActionException($"The new name '{name}' for '{shown}' can't end with a dot or a space.");
        var stem = name.Split('.')[0].TrimEnd();
        if (Reserved.Contains(stem))
            throw new ActionException($"'{stem}' is a reserved Windows name and can't be used as a file name (for '{shown}').");
        if (name.Length > 255) throw new ActionException($"The new name for '{shown}' is too long.");
    }
}

public sealed class BulkRenameAction : ShortcutAction
{
    private static readonly ActionDescriptor D = new()
    {
        Id = "file.renamePattern", Title = "Rename Files by Pattern", Category = ActionCategories.FilesAndFolders, Icon = "Rename24",
        Keywords = new[] { "bulk", "batch", "regex", "number", "counter", "case", "find", "replace" },
        Summary = "Rename {files} as {pattern}",
        Description = "Renames many files at once. The pattern can use {name} (name without type), {ext}, {n} (a counter), {date} (date modified) and {parent} (folder name). Optionally find and replace text, or change capital letters. Nothing is overwritten, and if a rename fails the ones already done are put back.",
        InputKinds = new[] { ContentKind.File }, OutputKind = ContentKind.File, OutputName = "Renamed Files",
        IsAdvanced = true,
        Parameters = new[]
        {
            ParameterDescriptor.FileParam("files", "Files", isInput: true) with { Required = true },
            ParameterDescriptor.Text("pattern", "New name", "{name}.{ext}", "e.g. {parent}-{n}.{ext}") with
            {
                Hint = "Tokens: {name} {ext} {n} {date} {parent}. Write the file type yourself, usually .{ext}.",
            },
            ParameterDescriptor.Number("startAt", "Start counting at", 1, 0, null),
            ParameterDescriptor.Number("padding", "Counter digits", 0, 0, 10) with { Hint = "3 turns 7 into 007." },
            ParameterDescriptor.Text("dateFormat", "Date format", "yyyy-MM-dd", "yyyy-MM-dd"),
            ParameterDescriptor.Text("find", "Replace this text", null, "text or pattern to find in the new name"),
            ParameterDescriptor.Text("replaceWith", "With", null, "leave empty to remove it"),
            ParameterDescriptor.Bool("useRegex", "Find is a regular expression", false),
            ParameterDescriptor.Bool("matchCase", "Match capital letters when replacing", false),
            ParameterDescriptor.ChoiceOf("caseStyle", "Capital letters", "none",
                new Choice("none", "Leave as they are"), new Choice("lower", "all lowercase"),
                new Choice("upper", "ALL UPPERCASE"), new Choice("title", "Every Word Capitalised")),
            ParameterDescriptor.ChoiceOf("onCollision", "If the name is already taken", "number",
                new Choice("number", "Add (2), (3) …"), new Choice("skip", "Leave that file as it is")),
            ParameterDescriptor.Bool("previewOnly", "Only show the new names (don't rename)", false),
        },
    };
    public override ActionDescriptor Descriptor => D;

    public override async Task<ContentValue> RunAsync(ActionContext ctx)
    {
        var files = await FileParams.RequireFilesAsync(ctx, "files", false);
        var pattern = await ctx.GetTextAsync("pattern", "{name}.{ext}");
        var date = await ctx.GetTextAsync("dateFormat", "yyyy-MM-dd");
        var options = new BulkRenameOptions
        {
            Pattern = string.IsNullOrWhiteSpace(pattern) ? "{name}.{ext}" : pattern,
            Start = (int)await ctx.GetNumberAsync("startAt", 1),
            Padding = (int)await ctx.GetNumberAsync("padding", 0),
            DateFormat = string.IsNullOrWhiteSpace(date) ? "yyyy-MM-dd" : date,
            Find = await ctx.GetTextAsync("find"),
            ReplaceWith = await ctx.GetTextAsync("replaceWith"),
            UseRegex = await ctx.GetBoolAsync("useRegex"),
            MatchCase = await ctx.GetBoolAsync("matchCase"),
            CaseStyle = await ctx.GetChoiceAsync("caseStyle", "none"),
            AddNumberOnCollision = await ctx.GetChoiceAsync("onCollision", "number") == "number",
        };
        var plan = BulkRenamer.Plan(files.Select(f => f.Path).ToList(), options);
        if (await ctx.GetBoolAsync("previewOnly"))
            return ContentValue.Texts(plan.Select(s => s.Skipped
                ? $"{Path.GetFileName(s.Source)} → (skipped, name taken)"
                : $"{Path.GetFileName(s.Source)} → {Path.GetFileName(s.Target)}"));

        try { BulkRenamer.Execute(plan, ctx.CancellationToken); }
        catch (IOException ex) { throw new ActionException($"Couldn't rename the files, so nothing was changed. One may be open in another program. ({ex.Message})", ex); }
        catch (UnauthorizedAccessException ex) { throw new ActionException("Flow Mate isn't allowed to rename these files. Nothing was changed.", ex); }
        foreach (var s in plan.Where(s => !s.Skipped && s.Source != s.Target))
            ctx.ReportProgress($"Renamed {Path.GetFileName(s.Source)} to {Path.GetFileName(s.Target)}");
        return ContentValue.Files(plan.Where(s => !s.Skipped).Select(s => s.Target));
    }
}
