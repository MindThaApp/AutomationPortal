namespace AutomationPortal.Core;

public enum ParamType
{
    Text, MultilineText, Number, Boolean, Choice, Date, Time, DateTime, Duration, File, Folder, SaveFile,
    App, Hotkey, Color, Url, Content, List, Dictionary, Conditions, VariableName, Shortcut,
}

/// <summary>One option of a Choice parameter. <paramref name="Id"/> is stored in the shortcut; <paramref name="Label"/> is shown.</summary>
public sealed record Choice(string Id, string Label, string? Icon = null)
{
    public static implicit operator Choice(string idAndLabel) => new(idAndLabel, idAndLabel);
}

/// <summary>Show this parameter only when parameter <paramref name="Key"/> currently has one of <paramref name="Values"/> (choice ids / text).</summary>
public sealed record VisibleWhen(string Key, params string[] Values);

/// <summary>Computes dynamic choices (audio devices, installed apps, shortcuts …). Called by the editor and by Ask Each Time.</summary>
public delegate Task<IReadOnlyList<Choice>> ChoicesProvider(AutomationPortalServices services, CancellationToken ct);

/// <summary>Describes one action parameter. Use the static helpers and <c>with { … }</c> to refine.</summary>
public sealed record ParameterDescriptor
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required ParamType Type { get; init; }
    public ParamValue? Default { get; init; }
    public string? Placeholder { get; init; }
    public IReadOnlyList<Choice>? Choices { get; init; }
    public ChoicesProvider? ChoicesProvider { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }
    public double? Step { get; init; }
    /// <summary>"seconds", "%", "characters" …</summary>
    public string? Unit { get; init; }
    public VisibleWhen? VisibleWhen { get; init; }
    public bool AllowVariables { get; init; } = true;
    public bool AllowAskEachTime { get; init; } = true;
    public bool Multiline { get; init; }
    public bool Required { get; init; }
    /// <summary>When empty, this parameter receives the previous action's output (Shortcuts' implicit input).</summary>
    public bool IsInput { get; init; }
    public string? Hint { get; init; }

    // ---- helpers -----------------------------------------------------------------------------

    public static ParameterDescriptor Of(string key, string label, ParamType type, ParamValue? @default = null, string? placeholder = null) =>
        new() { Key = key, Label = label, Type = type, Default = @default, Placeholder = placeholder };

    public static ParameterDescriptor Text(string key, string label, string? @default = null, string? placeholder = null) =>
        new() { Key = key, Label = label, Type = ParamType.Text, Default = @default is null ? null : ParamValue.Text(@default), Placeholder = placeholder };

    public static ParameterDescriptor MultilineText(string key, string label, string? @default = null, string? placeholder = null) =>
        new() { Key = key, Label = label, Type = ParamType.MultilineText, Multiline = true, Default = @default is null ? null : ParamValue.Text(@default), Placeholder = placeholder };

    public static ParameterDescriptor Number(string key, string label, double? @default = null, double? min = null, double? max = null, string? unit = null) =>
        new() { Key = key, Label = label, Type = ParamType.Number, Default = @default is null ? null : ParamValue.Number(@default.Value), Min = min, Max = max, Unit = unit };

    public static ParameterDescriptor Bool(string key, string label, bool @default = false) =>
        new() { Key = key, Label = label, Type = ParamType.Boolean, Default = ParamValue.Bool(@default) };

    public static ParameterDescriptor ChoiceOf(string key, string label, string? defaultId, params Choice[] choices) =>
        new() { Key = key, Label = label, Type = ParamType.Choice, Choices = choices, Default = defaultId is null ? null : ParamValue.Choice(defaultId) };

    public static ParameterDescriptor DynamicChoice(string key, string label, ChoicesProvider provider, string? defaultId = null) =>
        new() { Key = key, Label = label, Type = ParamType.Choice, ChoicesProvider = provider, Default = defaultId is null ? null : ParamValue.Choice(defaultId) };

    /// <summary>A parameter that takes any content (variables welcome). Pass isInput: true to receive the previous action's output when empty.</summary>
    public static ParameterDescriptor Content(string key, string label, bool isInput = false, bool required = false) =>
        new() { Key = key, Label = label, Type = ParamType.Content, IsInput = isInput, Required = required };

    public static ParameterDescriptor Duration(string key, string label, double? defaultSeconds = null) =>
        new() { Key = key, Label = label, Type = ParamType.Duration, Default = defaultSeconds is null ? null : ParamValue.Duration(defaultSeconds.Value) };

    public static ParameterDescriptor FileParam(string key, string label, bool isInput = false) =>
        new() { Key = key, Label = label, Type = ParamType.File, IsInput = isInput };

    public static ParameterDescriptor FolderParam(string key, string label) =>
        new() { Key = key, Label = label, Type = ParamType.Folder };

    public static ParameterDescriptor ListParam(string key, string label) =>
        new() { Key = key, Label = label, Type = ParamType.List };

    public bool IsVisible(Func<string, string?> currentValueOf) =>
        VisibleWhen is null || VisibleWhen.Values.Contains(currentValueOf(VisibleWhen.Key) ?? "", StringComparer.OrdinalIgnoreCase);
}

/// <summary>A piece of a Summary sentence: plain text, or a {parameter} token.</summary>
public readonly record struct SummarySegment(string Text, string? ParamKey)
{
    public bool IsToken => ParamKey is not null;

    /// <summary>"Resize {images} to {width} wide" → ["Resize ", {images}, " to ", {width}, " wide"].</summary>
    public static IReadOnlyList<SummarySegment> Parse(string summary)
    {
        var list = new List<SummarySegment>();
        var i = 0;
        while (i < summary.Length)
        {
            var open = summary.IndexOf('{', i);
            if (open < 0) { list.Add(new(summary[i..], null)); break; }
            var close = summary.IndexOf('}', open);
            if (close < 0) { list.Add(new(summary[i..], null)); break; }
            if (open > i) list.Add(new(summary[i..open], null));
            var key = summary[(open + 1)..close];
            list.Add(new(key, key));
            i = close + 1;
        }
        return list;
    }
}

/// <summary>Plain-English metadata for an action (SPEC §3.6).</summary>
public sealed record ActionDescriptor
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Category { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
    /// <summary>WPF-UI SymbolRegular name, e.g. "Comment24".</summary>
    public string Icon { get; init; } = "Flash24";
    /// <summary>Sentence with {paramKey} tokens, e.g. "Resize {images} to {width} wide". Parameters not mentioned appear under "Show More".</summary>
    public string Summary { get; init; } = "";
    public string Description { get; init; } = "";
    public IReadOnlyList<ContentKind> InputKinds { get; init; } = Array.Empty<ContentKind>();
    public ContentKind OutputKind { get; init; } = ContentKind.Any;
    /// <summary>False for actions that produce no usable output (hidden from the variable picker).</summary>
    public bool HasOutput { get; init; } = true;
    /// <summary>Default name of the magic variable, e.g. "Resized Images".</summary>
    public string? OutputName { get; init; }
    public IReadOnlyList<ParameterDescriptor> Parameters { get; init; } = Array.Empty<ParameterDescriptor>();
    /// <summary>Hidden unless Settings › Show advanced actions.</summary>
    public bool IsAdvanced { get; init; }
    /// <summary>Shows a warning sign on import / when added.</summary>
    public bool IsDestructive { get; init; }
    /// <summary>The action is destructive only when this choice parameter has this value (e.g. ifExists = replace).</summary>
    public (string Key, string Value)? DestructiveWhenChoice { get; init; }
    /// <summary>The action is destructive only when this on/off parameter is on (e.g. Quit App's "force").</summary>
    public string? DestructiveWhenOn { get; init; }
    /// <summary>The output of this action is secret (generated passwords …) and must not be stored in run history.</summary>
    public bool SensitiveOutput { get; init; }
    public bool NeedsInternet { get; init; }
    /// <summary>What a step of this kind can do that someone importing a shared shortcut should hear about first (runs programs, sends data …).</summary>
    public ImportRisk ImportRisk { get; init; }
    /// <summary>Optional: works out the risk from the step's settings (e.g. only when a file is sent). When set, it replaces <see cref="ImportRisk"/>.</summary>
    public Func<ActionInstance, ImportRisk>? ImportRiskFor { get; init; }
    public FlowKind FlowKind { get; init; }

    public string EffectiveOutputName => OutputName ?? Title;
    /// <summary>Editor colour key for this category.</summary>
    public string Color => ActionCategories.ColorOf(Category);
    public IReadOnlyList<SummarySegment> SummarySegments => SummarySegment.Parse(Summary);
    public bool IsDestructiveFor(ActionInstance a) =>
        IsDestructive || (DestructiveWhenChoice is { } w && a.Parameters.TryGetValue(w.Key, out var v) && v is ChoiceValue c
            && string.Equals(c.Value, w.Value, StringComparison.OrdinalIgnoreCase))
        || (DestructiveWhenOn is { } on && a.Parameters.TryGetValue(on, out var b) && b is BoolValue { Value: true } or VarValue or AskValue);
    public ParameterDescriptor? FindParameter(string key) => Parameters.FirstOrDefault(p => p.Key == key);
    /// <summary>The import risk of this particular step.</summary>
    public ImportRisk ImportRiskOf(ActionInstance a) => ImportRiskFor?.Invoke(a) ?? ImportRisk;
}

/// <summary>Things a step can do that a person importing someone else's shortcut should be told about before keeping it.</summary>
[Flags]
public enum ImportRisk
{
    None = 0,
    /// <summary>Runs a command or program, or opens a file or app that could be a program.</summary>
    RunsPrograms = 1,
    /// <summary>Downloads files from the internet.</summary>
    DownloadsFiles = 2,
    /// <summary>Sends files or data off this PC (uploads, messages, e-mail).</summary>
    SendsData = 4,
}

/// <summary>Category names and their editor colours (SPEC §4).</summary>
public static class ActionCategories
{
    public const string Scripting = "Scripting";
    public const string Variables = "Variables";
    public const string ShowAndAsk = "Show & Ask";
    public const string Text = "Text";
    public const string Numbers = "Numbers";
    public const string Dates = "Dates";
    public const string ListsAndDictionaries = "Lists & Lookups";
    public const string Web = "Web";
    public const string WeatherAndLocation = "Weather & Location";
    public const string Spreadsheets = "Spreadsheets";
    public const string FilesAndFolders = "Files & Folders";
    public const string Images = "Images";
    public const string Pdf = "PDF";
    public const string NotesAndReminders = "Notes & Reminders";
    public const string EmailAndSharing = "Email & Sharing";
    public const string Ai = "AI";
    public const string DeviceAndSettings = "Device & Settings";
    public const string AppsAndWindows = "Apps & Windows";
    public const string KeyboardAndMouse = "Keyboard & Mouse";
    public const string Clipboard = "Clipboard";
    public const string Screen = "Screen";
    public const string SoundAndMedia = "Sound & Media";
    public const string Advanced = "Advanced";

    // Everyday jobs first, then the "Logic" group (building blocks), with Scripting last.
    private static readonly (string Name, string Color)[] Table =
    {
        (FilesAndFolders, "cyan"), (AppsAndWindows, "indigo"), (Text, "yellow"), (Web, "blue"), (Images, "purple"),
        (Pdf, "red"), (Clipboard, "teal"), (ShowAndAsk, "blue"), (SoundAndMedia, "pink"), (DeviceAndSettings, "gray"),
        (NotesAndReminders, "yellow"), (EmailAndSharing, "blue"), (Spreadsheets, "green"), (WeatherAndLocation, "cyan"),
        (KeyboardAndMouse, "graphite"), (Screen, "pink"), (Ai, "indigo"),
        (Variables, "orange"), (ListsAndDictionaries, "orange"), (Numbers, "slate"), (Dates, "red"),
        (Scripting, "gray"), (Advanced, "graphite"),
    };

    /// <summary>Building-block groups, shown under a "Logic" divider after the everyday ones.</summary>
    public static bool IsLogic(string category) =>
        category is Variables or ListsAndDictionaries or Numbers or Dates or Scripting or Advanced;

    /// <summary>All categories in display order.</summary>
    public static IReadOnlyList<string> All { get; } = Table.Select(t => t.Name).ToArray();

    public static string ColorOf(string category) =>
        Table.FirstOrDefault(t => t.Name == category).Color ?? "gray";
}

/// <summary>Plain-English metadata for a trigger (SPEC §3.10). Summary like "When {app} opens".</summary>
public sealed record TriggerDescriptor
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Category { get; init; }
    public string Icon { get; init; } = "Flash24";
    public string Summary { get; init; } = "";
    public string Description { get; init; } = "";
    public IReadOnlyList<ParameterDescriptor> Parameters { get; init; } = Array.Empty<ParameterDescriptor>();
    public ContentKind OutputKind { get; init; } = ContentKind.Any;
    /// <summary>What the shortcut receives as input, e.g. "The files that were added".</summary>
    public string? OutputDescription { get; init; }
    public IReadOnlyList<SummarySegment> SummarySegments => SummarySegment.Parse(Summary);
}

/// <summary>Plain-English metadata for an "Only if…" condition. Summary like "it's between {from} and {to}".</summary>
public sealed record ConditionDescriptor
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Category { get; init; } = "";
    public string Icon { get; init; } = "Flash24";
    public string Summary { get; init; } = "";
    public string Description { get; init; } = "";
    public IReadOnlyList<ParameterDescriptor> Parameters { get; init; } = Array.Empty<ParameterDescriptor>();
    public IReadOnlyList<SummarySegment> SummarySegments => SummarySegment.Parse(Summary);
}
