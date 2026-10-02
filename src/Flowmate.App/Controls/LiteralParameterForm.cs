using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Flowmate.App.Infrastructure;
using Flowmate.App.Integration;
using Flowmate.App.Logic;
using Flowmate.Core;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace Flowmate.App.Controls;

/// <summary>Default <see cref="IParameterFormFactory"/>: plain, literal-only editors for trigger and condition options.</summary>
public sealed class LiteralParameterFormFactory : IParameterFormFactory
{
    public IParameterForm Create(IReadOnlyList<ParameterDescriptor> parameters, Dictionary<string, ParamValue> values, FlowmateServices services) =>
        new LiteralParameterForm(parameters, values, services);
}

public sealed class LiteralParameterForm : IParameterForm
{
    private readonly IReadOnlyList<ParameterDescriptor> _parameters;
    private readonly Dictionary<string, ParamValue> _values;
    private readonly FlowmateServices _services;
    private readonly StackPanel _root = new();
    private readonly List<(ParameterDescriptor P, FrameworkElement Row)> _rows = new();
    private bool _building = true;

    public LiteralParameterForm(IReadOnlyList<ParameterDescriptor> parameters, Dictionary<string, ParamValue> values, FlowmateServices services)
    {
        _parameters = parameters;
        _values = values;
        _services = services;
        foreach (var p in parameters) AddRow(p);
        _building = false;
        UpdateVisibility();
    }

    public FrameworkElement Element => _root;
    public event EventHandler? Changed;

    public IReadOnlyList<string> MissingRequired() => LiteralFormatter.MissingRequired(_parameters, _values);

    // ---- plumbing --------------------------------------------------------------------------

    private void Set(ParameterDescriptor p, ParamValue? value)
    {
        if (_building) return;
        if (value is null || LiteralFormatter.IsBlank(value) && p.Type is not (ParamType.Boolean or ParamType.Number or ParamType.Duration))
            _values.Remove(p.Key);
        else _values[p.Key] = value;
        UpdateVisibility();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateVisibility()
    {
        foreach (var (p, row) in _rows)
            row.Visibility = p.IsVisible(k => LiteralFormatter.Effective(_parameters.First(x => x.Key == k), _values)?.ToLiteralText()) ? Visibility.Visible : Visibility.Collapsed;
    }

    private ParamValue? Current(ParameterDescriptor p) => LiteralFormatter.Effective(p, _values);

    private void AddRow(ParameterDescriptor p)
    {
        var editor = BuildEditor(p);
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        if (p.Type != ParamType.Boolean)
        {
            var label = Ui.Text(p.Required ? p.Label + " (required)" : p.Label, secondary: true, size: 12);
            label.Margin = new Thickness(0, 0, 0, 4);
            row.Children.Add(label);
        }
        row.Children.Add(editor);
        if (!string.IsNullOrWhiteSpace(p.Hint))
        {
            var hint = Ui.Text(p.Hint!, secondary: true, size: 12);
            hint.Margin = new Thickness(0, 4, 0, 0);
            row.Children.Add(hint);
        }
        _rows.Add((p, row));
        _root.Children.Add(row);
    }

    private FrameworkElement BuildEditor(ParameterDescriptor p) => p.Type switch
    {
        ParamType.Boolean => BuildToggle(p),
        ParamType.Number => BuildNumber(p),
        ParamType.Choice => BuildChoice(p),
        ParamType.List when p.Choices is { Count: > 0 } => BuildChips(p),
        ParamType.List => BuildTextList(p),
        ParamType.Time => BuildTime(p),
        ParamType.Date => BuildDate(p, withTime: false),
        ParamType.DateTime => BuildDate(p, withTime: true),
        ParamType.Duration => BuildDuration(p),
        ParamType.File or ParamType.SaveFile => BuildPath(p, PathKind.File),
        ParamType.Folder => BuildPath(p, PathKind.Folder),
        ParamType.App => BuildApp(p),
        ParamType.Hotkey => BuildHotkey(p),
        ParamType.Color => BuildColor(p),
        ParamType.Shortcut => BuildShortcutPicker(p),
        ParamType.MultilineText => BuildText(p, multiline: true),
        _ => BuildText(p, multiline: p.Multiline),
    };

    // ---- editors ---------------------------------------------------------------------------

    private FrameworkElement BuildText(ParameterDescriptor p, bool multiline)
    {
        var box = new Wpf.Ui.Controls.TextBox
        {
            Text = Current(p)?.ToLiteralText() ?? "", PlaceholderText = p.Placeholder ?? "",
            AcceptsReturn = multiline, TextWrapping = TextWrapping.Wrap, MinHeight = multiline ? 90 : 0,
            VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
        };
        AutomationProperties.SetName(box, p.Label);
        box.TextChanged += (_, _) => Set(p, string.IsNullOrEmpty(box.Text) ? null : ParamValue.Text(box.Text));
        return box;
    }

    private FrameworkElement BuildTextList(ParameterDescriptor p)
    {
        var existing = Current(p) is ListValue l ? string.Join("\n", l.Items.Select(i => i.ToLiteralText())) : "";
        var box = new Wpf.Ui.Controls.TextBox { Text = existing, PlaceholderText = p.Placeholder ?? "One per line", AcceptsReturn = true, MinHeight = 90, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        AutomationProperties.SetName(box, p.Label);
        box.TextChanged += (_, _) =>
        {
            var lines = box.Text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            Set(p, lines.Length == 0 ? null : ParamValue.List(lines));
        };
        return box;
    }

    private FrameworkElement BuildToggle(ParameterDescriptor p)
    {
        var toggle = new ToggleSwitch { Content = p.Label, IsChecked = Current(p) is BoolValue { Value: true } };
        AutomationProperties.SetName(toggle, p.Label);
        toggle.Click += (_, _) => Set(p, ParamValue.Bool(toggle.IsChecked == true));
        return toggle;
    }

    private FrameworkElement BuildNumber(ParameterDescriptor p)
    {
        var box = new NumberBox
        {
            Minimum = p.Min ?? double.MinValue, Maximum = p.Max ?? double.MaxValue, SmallChange = p.Step ?? 1, LargeChange = (p.Step ?? 1) * 10,
            PlaceholderText = p.Placeholder ?? "", MaxDecimalPlaces = 4,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            Value = Current(p) is NumberValue n ? n.Value : null,
        };
        AutomationProperties.SetName(box, p.Label);
        box.ValueChanged += (_, _) => Set(p, box.Value is { } v ? ParamValue.Number(v) : null);
        if (string.IsNullOrEmpty(p.Unit)) return box;
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(box);
        var unit = Ui.Text(p.Unit!, secondary: true);
        unit.Margin = new Thickness(10, 0, 0, 0);
        unit.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(unit, 1);
        row.Children.Add(unit);
        return row;
    }

    private FrameworkElement BuildChoice(ParameterDescriptor p)
    {
        var combo = new System.Windows.Controls.ComboBox { DisplayMemberPath = nameof(Choice.Label), HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(combo, p.Label);
        var selectedId = Current(p)?.ToLiteralText();

        void Fill(IReadOnlyList<Choice> choices)
        {
            _building = true;
            combo.ItemsSource = choices;
            combo.SelectedItem = choices.FirstOrDefault(c => c.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
            _building = false;
        }

        combo.SelectionChanged += (_, _) =>
        {
            if (_building || combo.SelectedItem is not Choice c) return;
            selectedId = c.Id;
            Set(p, ParamValue.Choice(c.Id));
        };

        if (p.Choices is not null) { Fill(p.Choices); return combo; }
        if (p.ChoicesProvider is null) { Fill(Array.Empty<Choice>()); return combo; }

        // Dynamic list (Wi-Fi networks, audio devices …): show "Loading…" and fill in when ready.
        var panel = new Grid();
        panel.Children.Add(combo);
        var status = Ui.Text("Loading…", secondary: true, size: 12);
        status.Margin = new Thickness(12, 0, 0, 0);
        status.VerticalAlignment = VerticalAlignment.Center;
        status.IsHitTestVisible = false;
        panel.Children.Add(status);
        combo.IsEnabled = false;
        _ = LoadChoicesAsync();
        return panel;

        async Task LoadChoicesAsync()
        {
            try
            {
                var choices = await Task.Run(() => p.ChoicesProvider!(_services, CancellationToken.None));
                await combo.Dispatcher.InvokeAsync(() =>
                {
                    Fill(choices);
                    combo.IsEnabled = true;
                    status.Visibility = Visibility.Collapsed;
                });
            }
            catch (Exception)
            {
                await combo.Dispatcher.InvokeAsync(() =>
                {
                    status.Text = "Couldn't load the list. Try again later.";
                });
            }
        }
    }

    private FrameworkElement BuildChips(ParameterDescriptor p)
    {
        var selected = new HashSet<string>(Current(p) is ListValue l ? l.Items.Select(i => i.ToLiteralText()) : Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var wrap = new WrapPanel();
        foreach (var c in p.Choices!)
        {
            var chip = new ToggleButton
            {
                Content = c.Label.Length > 3 && DateTimeFormatInfo.CurrentInfo.DayNames.Contains(c.Label) ? c.Label[..3] : c.Label,
                ToolTip = c.Label, IsChecked = selected.Contains(c.Id), Margin = new Thickness(0, 0, 6, 6), MinWidth = 52, Padding = new Thickness(12, 6, 12, 6),
            };
            AutomationProperties.SetName(chip, c.Label);
            var id = c.Id;
            chip.Click += (_, _) =>
            {
                if (chip.IsChecked == true) selected.Add(id); else selected.Remove(id);
                var ordered = p.Choices!.Where(x => selected.Contains(x.Id)).Select(x => x.Id).ToArray();
                Set(p, ParamValue.List(ordered));
            };
            wrap.Children.Add(chip);
        }
        return wrap;
    }

    private FrameworkElement BuildTime(ParameterDescriptor p)
    {
        var picker = new TimePicker { SelectedTime = Current(p) is TimeValue t ? t.Value.ToTimeSpan() : null, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 220 };
        AutomationProperties.SetName(picker, p.Label);
        return picker.WithSelectedTimeChanged(() => Set(p, picker.SelectedTime is { } ts ? ParamValue.Time(TimeOnly.FromTimeSpan(ts)) : null));
    }

    private FrameworkElement BuildDate(ParameterDescriptor p, bool withTime)
    {
        var existing = Current(p) is DateValue dv ? dv.Value.ToLocalTime().DateTime : (DateTime?)null;
        var date = new CalendarDatePicker { Date = existing?.Date, HorizontalAlignment = HorizontalAlignment.Stretch, IsTodayHighlighted = true };
        AutomationProperties.SetName(date, p.Label);
        var time = new TimePicker { SelectedTime = existing?.TimeOfDay, MinWidth = 150, Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetName(time, p.Label + " time");

        void Push()
        {
            if (date.Date is not { } d) { Set(p, null); return; }
            var combined = d.Date.Add(withTime ? time.SelectedTime ?? TimeSpan.Zero : TimeSpan.Zero);
            Set(p, ParamValue.Date(new DateTimeOffset(DateTime.SpecifyKind(combined, DateTimeKind.Unspecified))));
        }

        date.DateChanged(Push);
        if (!withTime) return date;
        time.WithSelectedTimeChanged(Push);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(date);
        Grid.SetColumn(time, 1);
        grid.Children.Add(time);
        return grid;
    }

    private static readonly (string Name, double Seconds)[] DurationUnits = { ("seconds", 1), ("minutes", 60), ("hours", 3600), ("days", 86400) };

    private FrameworkElement BuildDuration(ParameterDescriptor p)
    {
        var seconds = Current(p) is DurationValue d ? d.Seconds : (double?)null;
        var unitIndex = 1;
        if (seconds is { } s0 && s0 > 0)
            for (var i = DurationUnits.Length - 1; i >= 0; i--)
                if (s0 % DurationUnits[i].Seconds == 0) { unitIndex = i; break; }

        var number = new NumberBox { Minimum = 0, SmallChange = 1, MaxDecimalPlaces = 2, Value = seconds is { } sec ? sec / DurationUnits[unitIndex].Seconds : null, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var unit = new System.Windows.Controls.ComboBox { ItemsSource = DurationUnits.Select(u => u.Name).ToList(), SelectedIndex = unitIndex, MinWidth = 120, Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetName(number, p.Label);
        AutomationProperties.SetName(unit, p.Label + " unit");

        void Push() => Set(p, number.Value is { } v && unit.SelectedIndex >= 0 ? ParamValue.Duration(v * DurationUnits[unit.SelectedIndex].Seconds) : null);
        number.ValueChanged += (_, _) => Push();
        unit.SelectionChanged += (_, _) => Push();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(number);
        Grid.SetColumn(unit, 1);
        grid.Children.Add(unit);
        return grid;
    }

    private enum PathKind { File, Folder }

    private FrameworkElement BuildPath(ParameterDescriptor p, PathKind kind)
    {
        var box = new Wpf.Ui.Controls.TextBox { Text = Current(p)?.ToLiteralText() ?? "", PlaceholderText = p.Placeholder ?? (kind == PathKind.Folder ? "Choose a folder" : "Choose a file") };
        AutomationProperties.SetName(box, p.Label);
        box.TextChanged += (_, _) => Set(p, string.IsNullOrWhiteSpace(box.Text) ? null : ParamValue.File(box.Text.Trim()));
        var browse = new Wpf.Ui.Controls.Button { Content = "Browse…", Margin = new Thickness(8, 0, 0, 0), Icon = new SymbolIcon(kind == PathKind.Folder ? SymbolRegular.Folder24 : SymbolRegular.Document24) };
        browse.Click += (_, _) =>
        {
            var owner = Window.GetWindow(browse);
            if (kind == PathKind.Folder)
            {
                var dlg = new OpenFolderDialog { Title = p.Label, InitialDirectory = Directory.Exists(box.Text) ? box.Text : "" };
                if (dlg.ShowDialog(owner) == true) box.Text = dlg.FolderName;
            }
            else if (p.Type == ParamType.SaveFile)
            {
                var dlg = new SaveFileDialog { Title = p.Label, FileName = box.Text };
                if (dlg.ShowDialog(owner) == true) box.Text = dlg.FileName;
            }
            else
            {
                var dlg = new OpenFileDialog { Title = p.Label, FileName = box.Text };
                if (dlg.ShowDialog(owner) == true) box.Text = dlg.FileName;
            }
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(box);
        Grid.SetColumn(browse, 1);
        grid.Children.Add(browse);
        return grid;
    }

    private FrameworkElement BuildApp(ParameterDescriptor p)
    {
        var app = Current(p) as AppValue;
        var box = new Wpf.Ui.Controls.TextBox { Text = app?.Name ?? app?.Id ?? "", PlaceholderText = p.Placeholder ?? "App name, like Spotify" };
        AutomationProperties.SetName(box, p.Label);
        string? browsedPath = app is not null && File.Exists(app.Id) ? app.Id : null;
        box.TextChanged += (_, _) =>
        {
            var text = box.Text.Trim();
            if (text.Length == 0) { Set(p, null); return; }
            var id = browsedPath is not null && string.Equals(Path.GetFileNameWithoutExtension(browsedPath), text, StringComparison.OrdinalIgnoreCase) ? browsedPath : text;
            Set(p, ParamValue.App(id, text));
        };
        var browse = new Wpf.Ui.Controls.Button { Content = "Browse…", Margin = new Thickness(8, 0, 0, 0), Icon = new SymbolIcon(SymbolRegular.Apps24) };
        browse.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Title = "Choose an app", Filter = "Apps (*.exe)|*.exe|All files (*.*)|*.*" };
            if (dlg.ShowDialog(Window.GetWindow(browse)) != true) return;
            browsedPath = dlg.FileName;
            box.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(box);
        Grid.SetColumn(browse, 1);
        grid.Children.Add(browse);
        return grid;
    }

    private FrameworkElement BuildHotkey(ParameterDescriptor p)
    {
        var rec = new HotkeyRecorderBox { Value = Current(p)?.ToLiteralText() ?? "", HorizontalAlignment = HorizontalAlignment.Stretch };
        rec.ValueChanged += (_, _) => Set(p, string.IsNullOrWhiteSpace(rec.Value) ? null : ParamValue.Hotkey(rec.Value));
        return rec;
    }

    private FrameworkElement BuildColor(ParameterDescriptor p)
    {
        var wrap = new WrapPanel();
        var current = Current(p)?.ToLiteralText();
        foreach (var key in Palette.Keys)
        {
            var rb = new RadioButton { GroupName = p.Key, IsChecked = key == current, ToolTip = key, Margin = new Thickness(0, 0, 6, 6), Width = 28, Height = 28 };
            rb.Template = Flowmate.App.Dialogs.IconColorPanel.SwatchTemplate(key);
            rb.Checked += (_, _) => Set(p, ParamValue.Choice(key));
            wrap.Children.Add(rb);
        }
        return wrap;
    }

    private FrameworkElement BuildShortcutPicker(ParameterDescriptor p)
    {
        var shortcuts = _services.LibraryOrNull?.All ?? Array.Empty<Shortcut>();
        var combo = new System.Windows.Controls.ComboBox { ItemsSource = shortcuts, DisplayMemberPath = nameof(Shortcut.Name), HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(combo, p.Label);
        var id = Current(p)?.ToLiteralText();
        combo.SelectedItem = shortcuts.FirstOrDefault(s => s.Id.ToString() == id);
        combo.SelectionChanged += (_, _) => Set(p, combo.SelectedItem is Shortcut s ? ParamValue.Choice(s.Id.ToString()) : null);
        return combo;
    }
}

internal static class PickerExtensions
{
    // WPF-UI's pickers expose their selection as a dependency property; these small helpers hook a change callback to it.
    public static TimePicker WithSelectedTimeChanged(this TimePicker picker, Action changed)
    {
        var d = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TimePicker.SelectedTimeProperty, typeof(TimePicker));
        d.AddValueChanged(picker, (_, _) => changed());
        return picker;
    }

    public static void DateChanged(this CalendarDatePicker picker, Action changed)
    {
        var d = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(CalendarDatePicker.DateProperty, typeof(CalendarDatePicker));
        d.AddValueChanged(picker, (_, _) => changed());
    }
}
