using System.Globalization;
using Flowmate.Core;

namespace Flowmate.Cli;

/// <summary>Questions and messages for shortcuts that run inside fm.exe. Prompts go to standard error so standard output stays the shortcut's result.</summary>
internal sealed class ConsoleInteraction : IUserInteraction
{
    private static readonly string NoUi = "This step needs to show something on your screen, so it only works while Flow Mate is open. Open Flow Mate and run the shortcut again.";

    /// <summary>The last text a "Show Result" step printed, so the final output isn't printed twice.</summary>
    public string? LastShownText { get; private set; }

    private static string ReadLine(string? prompt)
    {
        if (prompt is not null) Console.Error.Write(prompt);
        var line = Console.In.ReadLine();
        if (line is null) throw new ActionException("This shortcut asks a question, but there is nobody to answer it. Run it from a terminal, or open Flow Mate and run it there.");
        return line;
    }

    private static string Show(ContentValue v) => v.IsEmpty ? "(nothing)" : v.AsText();

    public Task<bool> ShowAlertAsync(string title, string message, bool showCancel, CancellationToken ct = default)
    {
        Console.Error.WriteLine(string.IsNullOrWhiteSpace(title) ? message : $"{title}: {message}");
        if (Console.IsInputRedirected) return Task.FromResult(true);
        var answer = ReadLine(showCancel ? "Press Enter to continue, or type n to cancel: " : "Press Enter to continue: ");
        if (showCancel && answer.Trim().StartsWith('n')) return Task.FromResult(false);
        return Task.FromResult(true);
    }

    public Task<ContentValue> AskForInputAsync(string prompt, InputKind kind, string? defaultValue, CancellationToken ct = default)
    {
        var line = ReadLine($"{prompt}{(string.IsNullOrEmpty(defaultValue) ? "" : $" [{defaultValue}]")}: ");
        if (line.Length == 0) line = defaultValue ?? "";
        return Task.FromResult(Convert(line, kind));
    }

    private static ContentValue Convert(string line, InputKind kind) => kind switch
    {
        InputKind.Number when double.TryParse(line, NumberStyles.Float, CultureInfo.CurrentCulture, out var d) || double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out d) => ContentValue.Number(d),
        InputKind.Date when DateTimeOffset.TryParse(line, out var date) => ContentValue.Date(date),
        InputKind.Url when Uri.TryCreate(line, UriKind.Absolute, out var u) => ContentValue.Url(u),
        _ => ContentValue.Text(line),
    };

    public Task<ContentValue> AskForFormAsync(string title, IReadOnlyList<FormField> fields, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(title)) Console.Error.WriteLine(title);
        var entries = new List<KeyValuePair<string, ContentValue>>();
        foreach (var f in fields)
        {
            ContentValue value;
            if (f.Kind == FormFieldKind.Choice && f.Options is { Count: > 0 } options)
            {
                var index = ChooseIndex(f.Label, options);
                value = ContentValue.Text(options[index]);
            }
            else
            {
                var line = ReadLine($"{f.Label}{(string.IsNullOrEmpty(f.Default) ? "" : $" [{f.Default}]")}: ");
                if (line.Length == 0) line = f.Default ?? "";
                value = f.Kind == FormFieldKind.Number ? Convert(line, InputKind.Number) : ContentValue.Text(line);
            }
            entries.Add(new(f.Label, value));
        }
        return Task.FromResult(ContentValue.Dict(entries));
    }

    private static int ChooseIndex(string prompt, IReadOnlyList<string> labels)
    {
        Console.Error.WriteLine(prompt);
        for (var i = 0; i < labels.Count; i++) Console.Error.WriteLine($"  {i + 1}) {labels[i]}");
        while (true)
        {
            var line = ReadLine($"Choose 1-{labels.Count}: ").Trim();
            if (line.Length == 0) throw new UserCancelledException();
            if (int.TryParse(line, out var n) && n >= 1 && n <= labels.Count) return n - 1;
            Console.Error.WriteLine("That isn't one of the choices.");
        }
    }

    public Task<ContentValue> ChooseFromListAsync(string prompt, IReadOnlyList<ContentItem> items, bool multiSelect, CancellationToken ct = default)
    {
        if (items.Count == 0) return Task.FromResult(ContentValue.Empty);
        if (!multiSelect) return Task.FromResult(ContentValue.From(items[ChooseIndex(prompt, items.Select(i => i.DisplayText).ToList())]));

        Console.Error.WriteLine(prompt);
        for (var i = 0; i < items.Count; i++) Console.Error.WriteLine($"  {i + 1}) {items[i].DisplayText}");
        while (true)
        {
            var line = ReadLine("Choose one or more (for example 1,3), or press Enter to cancel: ").Trim();
            if (line.Length == 0) throw new UserCancelledException();
            var picks = line.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(p => int.TryParse(p, out var n) ? n : -1).ToList();
            if (picks.All(n => n >= 1 && n <= items.Count)) return Task.FromResult(ContentValue.FromItems(picks.Select(n => items[n - 1])));
            Console.Error.WriteLine("Use the numbers shown, separated by commas.");
        }
    }

    public Task<int> ChooseFromMenuAsync(string prompt, IReadOnlyList<string> labels, CancellationToken ct = default) =>
        Task.FromResult(ChooseIndex(prompt, labels));

    public Task ShowResultAsync(string title, ContentValue value, CancellationToken ct = default)
    {
        LastShownText = Show(value);
        Console.Out.WriteLine(LastShownText);
        return Task.CompletedTask;
    }

    public Task PreviewAsync(ContentValue value, CancellationToken ct = default) => ShowResultAsync("", value, ct);

    private static IEnumerable<string> ParsePaths(string line) =>
        line.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim().Trim('"')).Where(p => p.Length > 0);

    public Task<ContentValue> PickFilesAsync(string prompt, bool multiple, IReadOnlyList<FileFilter>? filters = null, CancellationToken ct = default)
    {
        var line = ReadLine($"{prompt} - type the path{(multiple ? "s, separated by ;" : "")}: ");
        var paths = ParsePaths(line).Select(Path.GetFullPath).Take(multiple ? int.MaxValue : 1).ToList();
        if (paths.Count == 0) throw new UserCancelledException();
        return Task.FromResult(ContentValue.Files(paths));
    }

    public Task<ContentValue> PickFolderAsync(string prompt, CancellationToken ct = default)
    {
        var path = ParsePaths(ReadLine($"{prompt} - type the folder path: ")).FirstOrDefault();
        if (path is null) throw new UserCancelledException();
        return Task.FromResult(ContentValue.Folder(Path.GetFullPath(path)));
    }

    public Task<string> PickSaveFileAsync(string prompt, string? suggestedName = null, IReadOnlyList<FileFilter>? filters = null, CancellationToken ct = default)
    {
        var line = ReadLine($"{prompt} - type where to save{(string.IsNullOrEmpty(suggestedName) ? "" : $" [{suggestedName}]")}: ").Trim().Trim('"');
        if (line.Length == 0) line = suggestedName ?? "";
        if (line.Length == 0) throw new UserCancelledException();
        return Task.FromResult(Path.GetFullPath(line));
    }

    public Task<ScreenRect?> SelectScreenRegionAsync(CancellationToken ct = default) => throw new ActionException(NoUi);

    public Task<ScreenPoint?> SelectScreenPointAsync(CancellationToken ct = default) => throw new ActionException(NoUi);
}

internal sealed class ConsoleNotifications : INotificationService
{
    public void Show(string title, string body, string? imagePath = null, Action? openOnClick = null, bool silent = false) =>
        Console.Error.WriteLine($"[Flow Mate] {title}{(string.IsNullOrWhiteSpace(body) ? "" : ": " + body)}");

    public Task<NotificationResponse> ShowInteractiveAsync(string title, string body, IReadOnlyList<string> buttons, bool allowReply, string? imagePath = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        Show(title, body);
        if (Console.IsInputRedirected) return Task.FromResult(new NotificationResponse(null, null, true));
        string? label = null, reply = null;
        if (buttons.Count > 0)
        {
            Console.Error.WriteLine("  " + string.Join("   ", buttons.Select((b, i) => $"{i + 1}) {b}")));
            var pick = Console.In.ReadLine()?.Trim();
            if (int.TryParse(pick, out var n) && n >= 1 && n <= buttons.Count) label = buttons[n - 1];
        }
        if (allowReply)
        {
            Console.Error.Write("Reply: ");
            reply = Console.In.ReadLine();
        }
        return Task.FromResult(new NotificationResponse(label, string.IsNullOrEmpty(reply) ? null : reply, label is null && string.IsNullOrEmpty(reply)));
    }

    public Task<bool> AskToRunAsync(string shortcutName, string reason, CancellationToken ct = default)
    {
        Console.Error.Write($"{reason} Run \"{shortcutName}\" now? [y/N] ");
        return Task.FromResult(!Console.IsInputRedirected && (Console.In.ReadLine()?.Trim().StartsWith('y') ?? false));
    }
}
