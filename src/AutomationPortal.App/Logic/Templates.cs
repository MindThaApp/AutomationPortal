using System.Globalization;
using AutomationPortal.Core;

namespace AutomationPortal.App.Logic;

/// <summary>How often a template's shortcut runs by itself. <see cref="Off"/>: only when the user runs it.</summary>
public enum TemplateRepeat { Off, Daily, Weekdays, Weekly }

/// <summary>The "when" blank of a template sentence.</summary>
public sealed record TemplateSchedule(TemplateRepeat Repeat, TimeOnly Time)
{
    public static IReadOnlyList<(TemplateRepeat Repeat, string Label)> Choices { get; } = new[]
    {
        (TemplateRepeat.Off, "when I run it"),
        (TemplateRepeat.Daily, "every day"),
        (TemplateRepeat.Weekdays, "on weekdays"),
        (TemplateRepeat.Weekly, "every Monday"),
    };

    public static string LabelOf(TemplateRepeat r) => Choices.First(c => c.Repeat == r).Label;

    /// <summary>"every day at 08:00", or "when I run it".</summary>
    public string Describe() => Repeat == TemplateRepeat.Off ? LabelOf(Repeat) : $"{LabelOf(Repeat)} at {Time.ToString("HH:mm", CultureInfo.InvariantCulture)}";

    /// <summary>"08:00" or "8:30" → a time; null when it isn't one.</summary>
    public static TimeOnly? ParseTime(string text) =>
        TimeOnly.TryParseExact(text.Trim(), new[] { "H:mm", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    /// <summary>The time-of-day automation that runs <paramref name="shortcutId"/>, or null when it isn't scheduled.</summary>
    public Automation? ToAutomation(Guid shortcutId, bool runIfMissed)
    {
        if (Repeat == TemplateRepeat.Off) return null;
        var days = Repeat switch
        {
            TemplateRepeat.Weekdays => new[] { "mon", "tue", "wed", "thu", "fri" },
            TemplateRepeat.Weekly => new[] { "mon" },
            _ => new[] { "mon", "tue", "wed", "thu", "fri", "sat", "sun" },
        };
        return new Automation
        {
            Enabled = true,
            ShortcutId = shortcutId,
            Trigger = new TriggerInstance
            {
                TriggerId = "time.daily",
                Parameters = new Dictionary<string, ParamValue> { ["time"] = ParamValue.Time(Time), ["days"] = ParamValue.List(days) },
            },
            RunIfMissed = runIfMissed,
        };
    }
}

/// <summary>A fixed answer offered for a text blank, e.g. "type" for the subfolder pattern "{kind}".</summary>
public sealed record TemplateOption(string Value, string Label);

/// <summary>A piece of a template sentence: plain words, a blank (an import question of the shortcut) or the "when" blank.</summary>
public abstract record TemplatePart;
public sealed record TemplateWords(string Text) : TemplatePart;
public sealed record TemplateBlank(int Question) : TemplatePart;
public sealed record TemplateWhen : TemplatePart;

/// <summary>
/// One template of the Gallery's "Start here" row. The shortcut itself is the gallery file <see cref="File"/>; its import questions are
/// the blanks of <see cref="Sentence"/> ("{0}" is question 0, "{when}" the schedule).
/// </summary>
public sealed record TemplateInfo(
    string File, string Title, string Summary, string Icon, string Color, string Badge, bool ChangesFiles,
    IReadOnlyList<string> Story, string Sentence, TemplateSchedule? Schedule = null, bool RunIfMissed = false,
    IReadOnlyDictionary<int, IReadOnlyList<TemplateOption>>? Options = null, bool ComingSoon = false)
{
    public IReadOnlyList<TemplateOption>? OptionsFor(int question) =>
        Options is not null && Options.TryGetValue(question, out var o) ? o : null;
}

public static class TemplateCatalog
{
    public static IReadOnlyList<TemplateInfo> All { get; } = new[]
    {
        new TemplateInfo("template-tidy-downloads.autoportal", "Tidy Downloads",
            "Sorts files you downloaded a while ago into folders by type or date.", "FolderArrowRight24", "cyan", "Changes files", true,
            new[] { "Find files in the folder older than a few days", "Move them into folders by type or date", "Tell you how many moved, with Undo" },
            "Tidy {0} into folders by {1}, for files older than {2} days, {when}.",
            new TemplateSchedule(TemplateRepeat.Daily, new TimeOnly(8, 0)),
            Options: new Dictionary<int, IReadOnlyList<TemplateOption>>
            {
                [1] = new[] { new TemplateOption("{kind}", "type"), new TemplateOption("{year}-{month}", "month"), new TemplateOption("{year}", "year"), new TemplateOption("{ext}", "file extension") },
            }),
        new TemplateInfo("template-convert-pictures.autoportal", "Convert Pictures",
            "Saves the pictures you pick as JPEG, PNG, GIF, BMP or TIFF.", "ImageArrowBack24", "purple", "Makes copies", true,
            new[] { "Take the pictures you pick or send to it", "Save each one in the new file type", "Next to the originals, as name (2) if taken" },
            "Convert the pictures I pick to {0}, as new files next to the originals."),
        new TemplateInfo("template-work-setup.autoportal", "Set Up My Workspace",
            "Opens your work app, a folder and a web page in one go.", "Briefcase24", "blue", "No file changes", false,
            new[] { "Open the app you pick", "Open a folder in File Explorer", "Open a web page" },
            "Open {0}, the {1} folder and {2}, {when}.",
            new TemplateSchedule(TemplateRepeat.Off, new TimeOnly(9, 0))),
        new TemplateInfo("template-folder-backup.autoportal", "Back Up a Folder",
            "Copies a folder into a new dated folder, every day or every week.", "Shield24", "green", "Copy only", true,
            new[] { "At the time you pick, make a folder named by the date", "Copy the folder into it", "Never delete or replace anything in the backup" },
            "Copy {0} into a new dated folder in {1}, {when}.",
            new TemplateSchedule(TemplateRepeat.Daily, new TimeOnly(18, 0)), RunIfMissed: true),
        new TemplateInfo("", "Fill a Form from a Spreadsheet",
            "Types each row of a CSV file into a form in another app.", "Table24", "orange", "Coming soon", false,
            new[] { "You fill in the form once while it records", "It repeats row 1 step by step so you can check", "Then the other rows; it stops at the first row that doesn't fit" },
            "", ComingSoon: true),
    };

    /// <summary>The template whose gallery file is <paramref name="fileNameOrPath"/>, or null.</summary>
    public static TemplateInfo? ForFile(string fileNameOrPath)
    {
        var name = Path.GetFileName(fileNameOrPath);
        return name.Length == 0 ? null : All.FirstOrDefault(t => !t.ComingSoon && string.Equals(t.File, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Splits "Tidy {0} into folders by {1}, {when}." into words and blanks. Unknown braces stay as words.</summary>
    public static IReadOnlyList<TemplatePart> Parse(string sentence)
    {
        var parts = new List<TemplatePart>();
        var text = new System.Text.StringBuilder();
        for (var i = 0; i < sentence.Length; i++)
        {
            var close = sentence[i] == '{' ? sentence.IndexOf('}', i) : -1;
            if (close > i)
            {
                var token = sentence[(i + 1)..close];
                TemplatePart? blank = token == "when" ? new TemplateWhen()
                    : int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var q) ? new TemplateBlank(q) : null;
                if (blank is not null)
                {
                    if (text.Length > 0) { parts.Add(new TemplateWords(text.ToString())); text.Clear(); }
                    parts.Add(blank);
                    i = close;
                    continue;
                }
            }
            text.Append(sentence[i]);
        }
        if (text.Length > 0) parts.Add(new TemplateWords(text.ToString()));
        return parts;
    }

    /// <summary>
    /// A copy of the template shortcut with the answers filled in (one per import question; null keeps the default) and any extra
    /// values set on its steps, such as the folder for "A folder I choose".
    /// </summary>
    public static Shortcut Fill(Shortcut template, IReadOnlyList<ParamValue?> answers, IEnumerable<(string Uid, string Key, ParamValue Value)>? extra = null)
    {
        var copy = template.Clone();
        copy.ApplyImportAnswers(answers);
        foreach (var (uid, key, value) in extra ?? Array.Empty<(string, string, ParamValue)>())
            if (copy.FindAction(uid) is { } a) a.Parameters[key] = value;
        return copy;
    }

    /// <summary>Why "Add" can't be pressed yet (a folder blank still empty), or null when every blank is answered.</summary>
    public static string? Missing(Shortcut template, IReadOnlyList<ParamValue?> answers)
    {
        for (var i = 0; i < template.ImportQuestions.Count; i++)
        {
            var value = i < answers.Count ? answers[i] : null;
            value ??= template.ImportQuestions[i].DefaultValue;
            if (value is null || value is TextValue { IsBlank: true } || value is TextValue t && string.IsNullOrWhiteSpace(t.ToLiteralText()) && !t.VarRefs().Any())
                return $"Choose {template.ImportQuestions[i].Question.TrimEnd('?').ToLower(CultureInfo.CurrentCulture)} first.";
        }
        return null;
    }
}
