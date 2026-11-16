using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Logic;
using AutomationPortal.App.Services;
using AutomationPortal.Core;
using Microsoft.Win32;

namespace AutomationPortal.App.Dialogs;

/// <summary>
/// The set-up sheet of a Gallery template: one sentence with blanks, a live preview of what it would do right now, and Add.
/// Adding imports a filled-in copy of the template shortcut, plus a time-of-day automation when a schedule was picked.
/// Nothing runs for real here; the preview only reads.
/// </summary>
public sealed class TemplateSheet
{
    private readonly AppHost _host;
    private readonly GalleryItem _item;
    private readonly TemplateInfo _info;
    private readonly Shortcut _template;
    private readonly ParamValue?[] _answers;
    private readonly Dictionary<(string Uid, string Key), ParamValue> _extra = new();
    private readonly List<(int Question, Dictionary<string, ParamValue> Values, string Key)> _forms = new();
    private readonly StackPanel _preview = new() { Margin = new Thickness(0, 14, 0, 0) };
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private TemplateRepeat _repeat;
    private string _time;
    private int _previewVersion;
    private FlowDialog? _dialog;

    private TemplateSheet(AppHost host, GalleryItem item, TemplateInfo info)
    {
        _host = host;
        _item = item;
        _info = info;
        _template = item.Shortcut;
        _answers = new ParamValue?[_template.ImportQuestions.Count];
        _repeat = info.Schedule?.Repeat ?? TemplateRepeat.Off;
        _time = (info.Schedule?.Time ?? new TimeOnly(8, 0)).ToString("HH:mm", CultureInfo.InvariantCulture);
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = RefreshPreviewAsync(); };
    }

    /// <summary>Shows the sheet. Returns the name of the added shortcut, or null when nothing was added.</summary>
    public static Task<string?> ShowAsync(AppHost host, Window? owner, GalleryItem item, TemplateInfo info) =>
        new TemplateSheet(host, item, info).ShowCoreAsync(owner);

    private bool AsksForFiles => _template.WhenNoInput == WhenNoInput.AskForFiles;

    private async Task<string?> ShowCoreAsync(Window? owner)
    {
        var body = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.Children.Add(Ui.Chip(_info.Icon, _info.Color, 44, 22));
        var titles = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Ui.Text(_info.Title, size: 18, weight: FontWeights.SemiBold));
        titles.Children.Add(Ui.Text(_info.Summary, secondary: true, size: 13));
        Grid.SetColumn(titles, 1);
        head.Children.Add(titles);
        body.Children.Add(head);

        var sentence = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        AutomationProperties.SetName(sentence, "Set up " + _info.Title);
        foreach (var part in TemplateCatalog.Parse(_info.Sentence))
        {
            switch (part)
            {
                case TemplateWords t:
                    foreach (var word in Words(t.Text)) sentence.Children.Add(Word(word));
                    break;
                case TemplateBlank b when b.Question < _template.ImportQuestions.Count:
                    sentence.Children.Add(BlankFor(b.Question));
                    break;
                case TemplateWhen:
                    sentence.Children.Add(WhenBlank());
                    break;
            }
        }
        body.Children.Add(sentence);
        body.Children.Add(_preview);
        var note = Ui.Text("You can change any of this later in the editor.", secondary: true, size: 13);
        note.Margin = new Thickness(0, 14, 0, 0);
        body.Children.Add(note);

        var scheduled = _info.Schedule is not null;
        var dlg = _dialog = new FlowDialog("Set up " + _info.Title, body, PrimaryText(), "Cancel", width: 600);
        var addUnscheduled = false;
        if (scheduled)
            dlg.AddLeftButton("Add, don't schedule yet", () => { if (Missing() is null) { addUnscheduled = true; dlg.Accept(); } });
        if (_info.ChangesFiles)
            dlg.AddLeftButton("Show what it would do", () => _ = RefreshPreviewAsync(force: true));
        dlg.Validate = () => Missing() is null;
        UpdateState();
        if (!AsksForFiles) _ = RefreshPreviewAsync();
        else _preview.Children.Add(Ui.Text("It works on the pictures you pick or send to it. Choose \"Show what it would do\" to pick some and see the new files it would make. Nothing is changed.", secondary: true, size: 13));

        if (!await dlg.ShowTopmostAsync(default, owner)) return null;
        _debounce.Stop();
        return await AddAsync(owner, schedule: !addUnscheduled);
    }

    private string PrimaryText() => _repeat != TemplateRepeat.Off ? "Add and turn on" : "Add";

    private static IEnumerable<string> Words(string text)
    {
        // Each word is its own piece so the sentence wraps like text; punctuation stays with the word before it.
        foreach (var w in text.Split(' ', StringSplitOptions.RemoveEmptyEntries)) yield return w;
    }

    private static TextBlock Word(string text) => new()
    {
        Text = text, FontSize = 16, Margin = new Thickness(0, 6, 6, 6), VerticalAlignment = VerticalAlignment.Center,
    };

    // ---- blanks -------------------------------------------------------------------------------

    private FrameworkElement BlankFor(int index)
    {
        var q = _template.ImportQuestions[index];
        var action = _template.FindAction(q.ActionUid);
        var param = action is null ? null : _host.Actions.Describe(action.ActionId)?.FindParameter(q.ParameterKey);
        var current = q.DefaultValue;

        if (_info.OptionsFor(index) is { } options)
        {
            var literal = current?.ToLiteralText() ?? "";
            return Combo(q.Question, options.Select(o => (o.Value, o.Label)).ToList(), literal, v => Answer(index, ParamValue.Text(v)));
        }

        switch (param?.Type)
        {
            case ParamType.Choice when param.Choices is { Count: > 0 } choices:
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                var chosenButton = new Wpf.Ui.Controls.Button { Content = "Choose…", Margin = new Thickness(0, 4, 6, 4), Visibility = Visibility.Collapsed };
                AutomationProperties.SetName(chosenButton, "Choose the folder");
                var hasChosen = action!.ActionId == "file.getFolder" && choices.Any(c => c.Id == "chosen");
                var combo = Combo(q.Question, choices.Select(c => (c.Id, c.Label)).ToList(), (current as ChoiceValue)?.Value ?? param.Default?.ToLiteralText() ?? "", v =>
                {
                    Answer(index, ParamValue.Choice(v));
                    chosenButton.Visibility = hasChosen && v == "chosen" ? Visibility.Visible : Visibility.Collapsed;
                });
                chosenButton.Click += (_, _) =>
                {
                    if (PickFolder() is not { } path) return;
                    _extra[(action.Uid, "chosen")] = ParamValue.Text(path);
                    chosenButton.Content = FolderLabel(path);
                    Changed();
                };
                panel.Children.Add(combo);
                panel.Children.Add(chosenButton);
                return panel;
            }
            case ParamType.Folder:
            {
                var start = current?.ToLiteralText() ?? "";
                var button = new Wpf.Ui.Controls.Button { Content = start.Length > 0 ? FolderLabel(start) : "choose a folder", Margin = new Thickness(0, 4, 6, 4) };
                AutomationProperties.SetName(button, q.Question);
                button.Click += (_, _) =>
                {
                    if (PickFolder() is not { } path) return;
                    button.Content = FolderLabel(path);
                    Answer(index, ParamValue.Text(path));
                };
                return button;
            }
            case ParamType.Number:
            {
                var box = new Wpf.Ui.Controls.TextBox { Text = current is NumberValue n ? n.Value.ToString(CultureInfo.CurrentCulture) : "", Width = 64, Margin = new Thickness(0, 4, 6, 4) };
                AutomationProperties.SetName(box, q.Question);
                var minimum = Logic.TemplateRules.MinimumFor(action?.ActionId, q.ParameterKey, param?.Min);
                box.TextChanged += (_, _) =>
                {
                    // Below the minimum (for example 0 days) the earlier answer stays.
                    if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var v) && Logic.TemplateRules.Accept(v, minimum) is { } ok)
                        Answer(index, ParamValue.Number(ok));
                };
                return box;
            }
            case ParamType.Text or ParamType.Url or ParamType.Content or null:
            {
                var box = new Wpf.Ui.Controls.TextBox { Text = current?.ToLiteralText() ?? "", MinWidth = 200, Margin = new Thickness(0, 4, 6, 4) };
                AutomationProperties.SetName(box, q.Question);
                box.TextChanged += (_, _) => Answer(index, ParamValue.Text(box.Text));
                return box;
            }
            default:
            {
                // Apps and anything else: the editor's own field for that kind of value.
                var values = new Dictionary<string, ParamValue>();
                if (current is not null) values[param.Key] = current;
                var form = _host.ParameterForms.Create(new[] { param with { Label = q.Question, Hint = null } }, values, _host.Services);
                form.Changed += (_, _) => Changed();
                _forms.Add((index, values, param.Key));
                form.Element.MinWidth = 220;
                form.Element.Margin = new Thickness(0, 0, 6, 0);
                return form.Element;
            }
        }
    }

    private FrameworkElement WhenBlank()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var time = new Wpf.Ui.Controls.TextBox { Text = _time, Width = 72, Margin = new Thickness(0, 4, 6, 4), Visibility = _repeat == TemplateRepeat.Off ? Visibility.Collapsed : Visibility.Visible };
        AutomationProperties.SetName(time, "At what time");
        var at = Word("at");
        at.Visibility = time.Visibility;
        var combo = Combo("When it runs", TemplateSchedule.Choices.Select(c => (c.Repeat.ToString(), c.Label)).ToList(), _repeat.ToString(), v =>
        {
            _repeat = Enum.Parse<TemplateRepeat>(v);
            time.Visibility = at.Visibility = _repeat == TemplateRepeat.Off ? Visibility.Collapsed : Visibility.Visible;
            UpdateState();
        });
        time.TextChanged += (_, _) => { _time = time.Text; UpdateState(); };
        panel.Children.Add(combo);
        panel.Children.Add(at);
        panel.Children.Add(time);
        return panel;
    }

    private ComboBox Combo(string name, IReadOnlyList<(string Value, string Label)> items, string selected, Action<string> onChange)
    {
        var combo = new ComboBox { Margin = new Thickness(0, 4, 6, 4), MinWidth = 120 };
        AutomationProperties.SetName(combo, name);
        foreach (var (value, label) in items) combo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == selected) ?? combo.Items.OfType<ComboBoxItem>().FirstOrDefault();
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ComboBoxItem { Tag: string v }) onChange(v); };
        return combo;
    }

    private static string? PickFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Choose a folder" };
        return dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName) ? dlg.FolderName : null;
    }

    private static string FolderLabel(string path) => Path.GetFileName(path.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : path;

    private void Answer(int index, ParamValue value)
    {
        _answers[index] = value;
        Changed();
    }

    private void Changed()
    {
        UpdateState();
        if (!AsksForFiles) { _debounce.Stop(); _debounce.Start(); }
    }

    private void UpdateState()
    {
        if (_dialog is null) return;
        _dialog.SetPrimaryText(PrimaryText());
        _dialog.SetPrimaryEnabled(Missing() is null);
    }

    // ---- filling in, preview, adding -------------------------------------------------------------

    private IReadOnlyList<ParamValue?> Answers()
    {
        var answers = _answers.ToArray();
        foreach (var (question, values, key) in _forms)
            if (values.TryGetValue(key, out var v)) answers[question] = v;
        return answers;
    }

    private Shortcut Filled() => TemplateCatalog.Fill(_template, Answers(), _extra.Select(e => (e.Key.Uid, e.Key.Key, e.Value)));

    private string? Missing()
    {
        if (TemplateCatalog.Missing(_template, Answers()) is { } m) return m;
        if (_repeat != TemplateRepeat.Off && TemplateSchedule.ParseTime(_time) is null) return "Type the time like 08:00.";
        foreach (var a in _template.Actions.Where(a => a.ActionId == "file.getFolder"))
        {
            var filled = Filled().FindAction(a.Uid);
            if (filled?.Parameters.GetValueOrDefault("folder") is ChoiceValue { Value: "chosen" } && filled.Parameters.GetValueOrDefault("chosen") is null or TextValue { IsBlank: true })
                return "Choose the folder first.";
        }
        return null;
    }

    private async Task RefreshPreviewAsync(bool force = false)
    {
        if (!_info.ChangesFiles && !force) return;
        var version = ++_previewVersion;
        if (Missing() is { } missing)
        {
            _preview.Children.Clear();
            _preview.Children.Add(Ui.Text(missing, secondary: true, size: 13));
            return;
        }
        var target = new StackPanel();
        await FilePlanView.ShowPreviewAsync(target, _host.Runner, Filled(), ContentValue.Empty);
        if (version != _previewVersion) return;   // a newer answer started another preview
        _preview.Children.Clear();
        var label = Ui.Text("Preview, right now:", secondary: true, size: 12);
        _preview.Children.Add(label);
        _preview.Children.Add(target);
    }

    private async Task<string?> AddAsync(Window? owner, bool schedule)
    {
        var copy = Filled();
        copy.ImportQuestions.Clear();   // answered here
        ImportResult result;
        try { result = _host.Library.Import(copy); }
        catch (LibraryException ex) { await Prompts.MessageAsync(owner, "Couldn't add it", ex.Message); return null; }
        await _host.FinishImportAsync(result, owner, warningsShown: true);

        if (schedule && _repeat != TemplateRepeat.Off && TemplateSchedule.ParseTime(_time) is { } time
            && new TemplateSchedule(_repeat, time).ToAutomation(result.Shortcut.Id, _info.RunIfMissed) is { } automation)
        {
            try { _host.Library.SaveAutomation(automation); }
            catch (LibraryException ex) { await Prompts.MessageAsync(owner, "Added, but couldn't turn on the schedule", ex.Message); }
        }
        return result.Shortcut.Name;
    }
}
