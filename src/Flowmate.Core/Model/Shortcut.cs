namespace Flowmate.Core;

public enum WhenNoInput { ContinueWithNothing, AskForFiles, UseClipboard, StopAndSay }
public enum ErrorMode { Stop, Continue, Retry }
public enum FlowRole { Start, Middle, End }
public enum FlowKind { None, If, Menu, Repeat, RepeatEach }
public enum RunMode { Immediately, AskFirst }

/// <summary>Where a run came from. Shown in run history.</summary>
public enum RunSource { Editor, Library, Hotkey, Automation, Cli, SendTo, QuickRun, Link, Nested, Reminder }

/// <summary>A shortcut: an ordered flat list of actions plus metadata. Mutable POCO; the library hands out shared instances, so call <see cref="Clone"/> before editing.</summary>
public sealed class Shortcut
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Shortcut";
    public string? Description { get; set; }
    /// <summary>WPF-UI SymbolRegular name.</summary>
    public string Icon { get; set; } = "Flash24";
    /// <summary>Palette key: red, orange, yellow, green, mint, teal, cyan, blue, indigo, purple, pink, brown, gray, graphite, slate.</summary>
    public string Color { get; set; } = "blue";
    public Guid? FolderId { get; set; }
    public int SortOrder { get; set; }
    /// <summary>Short code typed in Quick Run ("gm" runs Good Morning).</summary>
    public string? Alias { get; set; }
    public bool Pinned { get; set; }
    /// <summary>e.g. "Ctrl+Alt+M".</summary>
    public string? Hotkey { get; set; }
    public bool ShowInQuickRun { get; set; } = true;
    public bool ShowInSendTo { get; set; }
    /// <summary>Empty = the shortcut takes no input.</summary>
    public List<ContentKind> AcceptsInput { get; set; } = new();
    public WhenNoInput WhenNoInput { get; set; } = WhenNoInput.ContinueWithNothing;
    public List<ActionInstance> Actions { get; set; } = new();
    public List<ImportQuestion> ImportQuestions { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.Now;
    public int SchemaVersion { get; set; } = 1;

    public Shortcut Clone() => FlowmateJson.Clone(this);

    /// <summary>Finds an action by uid.</summary>
    public ActionInstance? FindAction(string uid) => Actions.FirstOrDefault(a => a.Uid == uid);

    /// <summary>Writes import-question answers (same order as <see cref="ImportQuestions"/>; null = keep default) into the actions' parameters.</summary>
    public void ApplyImportAnswers(IReadOnlyList<ParamValue?> answers)
    {
        for (var i = 0; i < ImportQuestions.Count && i < answers.Count; i++)
        {
            var q = ImportQuestions[i];
            var value = answers[i];
            if (value is null) continue;
            var action = FindAction(q.ActionUid);
            if (action is not null) action.Parameters[q.ParameterKey] = value;
        }
    }
}

public sealed class ShortcutFolder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Folder";
    public string Icon { get; set; } = "Folder24";
    public int SortOrder { get; set; }
}

/// <summary>A question asked when a shortcut is imported from the gallery ("Which folder?") whose answer fills one action parameter.</summary>
public sealed class ImportQuestion
{
    public string ActionUid { get; set; } = "";
    public string ParameterKey { get; set; } = "";
    public string Question { get; set; } = "";
    public ParamValue? DefaultValue { get; set; }
}

public sealed class ErrorPolicy
{
    public ErrorMode Mode { get; set; } = ErrorMode.Stop;
    public int Retries { get; set; } = 2;
    public double RetryDelaySeconds { get; set; } = 1;
}

/// <summary>Marks an action as part of a control-flow group (If / Menu / Repeat).</summary>
public sealed class FlowMarker
{
    public string GroupId { get; set; } = "";
    public FlowRole Role { get; set; }
    /// <summary>Menu item text for a Middle part of Choose from Menu.</summary>
    public string? Label { get; set; }
}

public sealed class ActionInstance
{
    /// <summary>Short unique id; target of magic variables.</summary>
    public string Uid { get; set; } = NewUid();
    public string ActionId { get; set; } = "";
    public Dictionary<string, ParamValue> Parameters { get; set; } = new();
    /// <summary>User rename of the magic variable (default = descriptor OutputName).</summary>
    public string? OutputName { get; set; }
    public bool Disabled { get; set; }
    public ErrorPolicy OnError { get; set; } = new();
    public FlowMarker? Flow { get; set; }
    public bool UiCollapsed { get; set; }

    public static string NewUid() => Guid.NewGuid().ToString("N")[..10];

    /// <summary>Creates an action with a fresh uid and the given parameters.</summary>
    public static ActionInstance Create(string actionId, params (string Key, ParamValue Value)[] parameters)
    {
        var a = new ActionInstance { ActionId = actionId };
        foreach (var (k, v) in parameters) a.Parameters[k] = v;
        return a;
    }

    public ActionInstance WithUid(string uid) { Uid = uid; return this; }
    public ActionInstance WithParam(string key, ParamValue value) { Parameters[key] = value; return this; }
    public ActionInstance WithFlow(string groupId, FlowRole role, string? label = null) { Flow = new FlowMarker { GroupId = groupId, Role = role, Label = label }; return this; }
    public ActionInstance OnErrorContinue() { OnError = new ErrorPolicy { Mode = ErrorMode.Continue }; return this; }
    public ActionInstance OnErrorRetry(int retries = 2, double delaySeconds = 1) { OnError = new ErrorPolicy { Mode = ErrorMode.Retry, Retries = retries, RetryDelaySeconds = delaySeconds }; return this; }

    public ActionInstance Clone() => FlowmateJson.Clone(this);
}
