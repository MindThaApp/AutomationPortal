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
        (TemplateRepeat.Off, Strings.Get("Templates_WhenIRunIt")),
        (TemplateRepeat.Daily, Strings.Get("Templates_EveryDay")),
        (TemplateRepeat.Weekdays, Strings.Get("Templates_OnWeekdays")),
        (TemplateRepeat.Weekly, Strings.Get("Templates_EveryMonday")),
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
        new TemplateInfo("template-tidy-downloads.autoportal", Strings.Get("Templates_TidyDownloads"),
            Strings.Get("Templates_SortsFilesYouDownloaded"), "FolderArrowRight24", "cyan", Strings.Get("Templates_ChangesFiles"), true,
            new[] { Strings.Get("Templates_FindFilesInThe"), Strings.Get("Templates_MoveThemIntoFolders"), Strings.Get("Templates_TellYouHowMany") },
            Strings.Get("Templates_Tidy0IntoFolders"),
            new TemplateSchedule(TemplateRepeat.Daily, new TimeOnly(8, 0)),
            Options: new Dictionary<int, IReadOnlyList<TemplateOption>>
            {
                [1] = new[] { new TemplateOption("{kind}", "type"), new TemplateOption("{year}-{month}", "month"), new TemplateOption("{year}", "year"), new TemplateOption("{ext}", Strings.Get("Templates_FileExtension")) },
            }),
        new TemplateInfo("template-convert-pictures.autoportal", Strings.Get("Templates_ConvertPictures"),
            Strings.Get("Templates_SavesThePicturesYou"), "ImageArrowBack24", "purple", Strings.Get("Templates_MakesCopies"), true,
            new[] { Strings.Get("Templates_TakeThePicturesYou"), Strings.Get("Templates_SaveEachOneIn"), Strings.Get("Templates_NextToTheOriginals") },
            Strings.Get("Templates_ConvertThePicturesI")),
        new TemplateInfo("template-work-setup.autoportal", Strings.Get("Templates_SetUpMyWorkspace"),
            Strings.Get("Templates_OpensYourWorkApp"), "Briefcase24", "blue", Strings.Get("Templates_NoFileChanges"), false,
            new[] { Strings.Get("Templates_OpenTheAppYou"), Strings.Get("Templates_OpenAFolderIn"), Strings.Get("Templates_OpenAWebPage") },
            Strings.Get("Templates_Open0The1"),
            new TemplateSchedule(TemplateRepeat.Off, new TimeOnly(9, 0))),
        new TemplateInfo("template-folder-backup.autoportal", Strings.Get("Templates_BackUpAFolder"),
            Strings.Get("Templates_CopiesAFolderInto"), "Shield24", "green", Strings.Get("Templates_CopyOnly"), true,
            new[] { Strings.Get("Templates_AtTheTimeYou"), Strings.Get("Templates_CopyTheFolderInto"), Strings.Get("Templates_NeverDeleteOrReplace") },
            Strings.Get("Templates_Copy0IntoA"),
            new TemplateSchedule(TemplateRepeat.Daily, new TimeOnly(18, 0)), RunIfMissed: true),
        new TemplateInfo("", Strings.Get("Templates_FillAFormFrom"),
            Strings.Get("Templates_TypesEachRowOf"), "Table24", "orange", Strings.Get("Templates_ComingSoon"), false,
            new[] { Strings.Get("Templates_YouFillInThe"), Strings.Get("Templates_ItRepeatsRow1"), Strings.Get("Templates_ThenTheOtherRows") },
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
                return Strings.Format("Templates_Choose0First", template.ImportQuestions[i].Question.TrimEnd('?').ToLower(CultureInfo.CurrentCulture));
        }
        return null;
    }
}
