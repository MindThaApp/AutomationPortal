using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutomationPortal.Core;
using AutomationPortal.Editor.Logic;
using Wpf.Ui.Controls;
using UiButton = Wpf.Ui.Controls.Button;
using UiTextBox = Wpf.Ui.Controls.TextBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfDatePicker = System.Windows.Controls.DatePicker;

namespace AutomationPortal.Editor.UI;

internal sealed record EditorArgs(ParameterDescriptor Param, ParamValue? Value, EditorEnv Env, Func<IReadOnlyList<VariableOption>> Variables, Action<ParamValue?> Commit);

/// <summary>One literal editor per <see cref="ParamType"/>. Each returns a control that calls <see cref="EditorArgs.Commit"/> on every change.</summary>
internal static class ValueEditors
{
    public static bool IsTokenCapable(ParamType t) => t is ParamType.Text or ParamType.MultilineText or ParamType.Url or ParamType.Content or ParamType.SaveFile;

    public static FrameworkElement Build(EditorArgs a, out TokenTextBox? token)
    {
        token = null;
        switch (a.Param.Type)
        {
            case ParamType.Text:
            case ParamType.MultilineText:
            case ParamType.Url:
            case ParamType.Content:
                token = NewToken(a);
                return WithResultButton(token);
            case ParamType.SaveFile:
                {
                    token = NewToken(a);
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition());
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.Children.Add(token);
                    var t = token;
                    var browse = Ui.TextButton("Browse…", () =>
                    {
                        var dlg = new Microsoft.Win32.SaveFileDialog { Title = a.Param.Label };
                        if (dlg.ShowDialog() == true) { var v = ParamValue.Text(dlg.FileName); t.SetValue(v); a.Commit(v); }
                    });
                    browse.Margin = new Thickness(8, 0, 0, 0);
                    Grid.SetColumn(browse, 1);
                    grid.Children.Add(browse);
                    return grid;
                }
            case ParamType.Number: return Number(a);
            case ParamType.Boolean: return Boolean(a);
            case ParamType.Choice: return ChoiceList(a);
            case ParamType.Shortcut: return ChoiceList(a);
            case ParamType.App: return a.Param.ChoicesProvider is not null || AppPickerHooks.Provider is not null ? ChoiceList(a) : AppPath(a);
            case ParamType.Date: return DateEditor(a, withTime: false);
            case ParamType.DateTime: return DateEditor(a, withTime: true);
            case ParamType.Time: return TimeEditor(a);
            case ParamType.Duration: return Duration(a);
            case ParamType.File:
            case ParamType.Folder: return PathEditor(a);
            case ParamType.Hotkey: return Hotkey(a);
            case ParamType.Color: return ColorEditor(a);
            case ParamType.List: return a.Param.Choices is { Count: > 0 } ? new MultiChoiceEditor(a) : new ListEditor(a);
            case ParamType.Dictionary: return new DictEditor(a);
            case ParamType.Conditions: return new ConditionsEditor(a);
            case ParamType.VariableName: return VariableName(a);
            default:
                token = NewToken(a);
                return WithResultButton(token);
        }
    }

    /// <summary>A small "+ result" button at the right edge of a value box, shown on hover and focus, that opens the result picker.</summary>
    private static FrameworkElement WithResultButton(TokenTextBox tb)
    {
        if (!tb.AllowVariables) return tb;
        var grid = new Grid();
        grid.Children.Add(tb);
        tb.Padding = new Thickness(tb.Padding.Left, tb.Padding.Top, tb.Padding.Right + 72, tb.Padding.Bottom);
        var add = Ui.TextButton("result", () => { tb.Focus(); tb.OpenPicker(); }, SymbolRegular.Add16);
        add.HorizontalAlignment = HorizontalAlignment.Right;
        add.VerticalAlignment = multilineTop(tb) ? VerticalAlignment.Top : VerticalAlignment.Center;
        add.Margin = new Thickness(0, 3, 4, 3);
        add.Padding = new Thickness(8, 2, 8, 2);
        add.Opacity = 0;
        add.ToolTip = "Use a result from an earlier step";
        System.Windows.Automation.AutomationProperties.SetName(add, "Use a result from an earlier step");
        grid.Children.Add(add);
        void Sync() => add.Opacity = grid.IsMouseOver || grid.IsKeyboardFocusWithin ? 1 : 0;
        grid.MouseEnter += (_, _) => Sync();
        grid.MouseLeave += (_, _) => Sync();
        grid.IsKeyboardFocusWithinChanged += (_, _) => Sync();
        return grid;

        static bool multilineTop(TokenTextBox t) => t.AcceptsReturn;
    }

    private static TokenTextBox NewToken(EditorArgs a)
    {
        var multiline = a.Param.Type == ParamType.MultilineText || a.Param.Multiline;
        var tb = new TokenTextBox(a.Env, a.Variables, multiline, a.Param.AllowVariables)
        {
            Placeholder = a.Param.Placeholder ?? (a.Param.Type == ParamType.Url ? "https://" : a.Param.Type == ParamType.Content ? "Type something or choose a variable" : ""),
        };
        tb.SetValue(a.Value ?? a.Param.Default);
        tb.ValueEdited += v => a.Commit(v);
        return tb;
    }

    // ---- number ------------------------------------------------------------------------------

    private static FrameworkElement Number(EditorArgs a)
    {
        var p = a.Param;
        var step = p.Step is > 0 ? p.Step.Value : 1;
        var current = (a.Value ?? p.Default) is NumberValue n ? n.Value : (double?)null;
        var box = new UiTextBox { PlaceholderText = p.Placeholder ?? "0", MinWidth = 110, Text = current is { } c ? ContentFormat.Number(c) : "" };
        var hint = Ui.Text("", Ui.Critical, 12);
        hint.Visibility = Visibility.Collapsed;
        var suppress = false;

        bool TryParse(string text, out double value) =>
            double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value) || double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        void Validate()
        {
            if (suppress) return;
            if (string.IsNullOrWhiteSpace(box.Text)) { hint.Visibility = Visibility.Collapsed; return; }
            if (!TryParse(box.Text, out var v)) { hint.Text = "Type a number, like 5 or 2.5."; hint.Visibility = Visibility.Visible; return; }
            if (p.Min is { } min && v < min) { hint.Text = $"The smallest allowed is {ContentFormat.Number(min)}."; hint.Visibility = Visibility.Visible; return; }
            if (p.Max is { } max && v > max) { hint.Text = $"The largest allowed is {ContentFormat.Number(max)}."; hint.Visibility = Visibility.Visible; return; }
            hint.Visibility = Visibility.Collapsed;
            a.Commit(ParamValue.Number(v));
        }

        void Nudge(double dir)
        {
            var v = TryParse(box.Text, out var cur) ? cur : (p.Min ?? 0);
            v = Math.Round(v + dir * step, 6);
            if (p.Min is { } min) v = Math.Max(min, v);
            if (p.Max is { } max) v = Math.Min(max, v);
            box.Text = ContentFormat.Number(v);
        }

        box.TextChanged += (_, _) => Validate();
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Up) { Nudge(+1); e.Handled = true; }
            else if (e.Key == System.Windows.Input.Key.Down) { Nudge(-1); e.Handled = true; }
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(box);
        row.Children.Add(Ui.IconButton(SymbolRegular.Subtract16, "Decrease", () => Nudge(-1), 30, 12));
        row.Children.Add(Ui.IconButton(SymbolRegular.Add16, "Increase", () => Nudge(+1), 30, 12));
        if (!string.IsNullOrEmpty(p.Unit))
        {
            var unit = Ui.Text(p.Unit!, Ui.Secondary, 13);
            unit.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(unit);
        }
        var root = new StackPanel();
        root.Children.Add(row);
        root.Children.Add(hint);
        return root;
    }

    // ---- boolean -----------------------------------------------------------------------------

    private static FrameworkElement Boolean(EditorArgs a)
    {
        var on = (a.Value ?? a.Param.Default) is BoolValue { Value: true };
        var toggle = new ToggleSwitch { IsChecked = on, OnContent = "On", OffContent = "Off" };
        toggle.Checked += (_, _) => a.Commit(ParamValue.Bool(true));
        toggle.Unchecked += (_, _) => a.Commit(ParamValue.Bool(false));
        return toggle;
    }

    // ---- choices (Choice, Shortcut, App with provider) ---------------------------------------

    private static FrameworkElement ChoiceList(EditorArgs a)
    {
        var host = new ContentControl { Focusable = false };
        void Build()
        {
            var choices = a.Env.TryGetChoices(a.Param);
            if (choices is null)
            {
                var loading = new StackPanel { Orientation = Orientation.Horizontal };
                loading.Children.Add(new ProgressRing { IsIndeterminate = true, Width = 18, Height = 18 });
                var t = Ui.Text("Loading choices…", Ui.Secondary, 13);
                t.Margin = new Thickness(8, 0, 0, 0);
                loading.Children.Add(t);
                host.Content = loading;
                void OnLoaded() { a.Env.ChoicesLoaded -= OnLoaded; Build(); }
                a.Env.ChoicesLoaded += OnLoaded;
                return;
            }
            host.Content = BuildChoices(a, choices);
        }
        Build();
        return host;
    }

    private static FrameworkElement BuildChoices(EditorArgs a, IReadOnlyList<Choice> choices)
    {
        var selectedId = (a.Value ?? a.Param.Default) switch { ChoiceValue c => c.Value, AppValue app => app.Id, TextValue t => t.ToLiteralText(), _ => null };
        var root = new StackPanel { MinWidth = 240 };
        UiTextBox? filter = null;
        if (choices.Count > 10)
        {
            filter = new UiTextBox { PlaceholderText = "Search", Margin = new Thickness(0, 0, 0, 6) };
            root.Children.Add(filter);
        }
        var list = new WpfListBox { MaxHeight = 260, Style = Application.Current?.TryFindResource("FmPlainListStyle") as Style };
        root.Children.Add(list);
        if (choices.Count == 0) root.Children.Add(Ui.Text("There is nothing to choose from yet.", Ui.Secondary, 13));

        void Fill()
        {
            list.Items.Clear();
            var q = filter?.Text.Trim() ?? "";
            foreach (var c in choices.Where(c => q.Length == 0 || c.Label.Contains(q, StringComparison.CurrentCultureIgnoreCase)))
            {
                var row = new DockPanel { Margin = new Thickness(8, 5, 8, 5), LastChildFill = true };
                if (a.Param.Type == ParamType.App && AppPickerHooks.IconLoader is { } loadIcon)
                {
                    var image = new System.Windows.Controls.Image { Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0) };
                    DockPanel.SetDock(image, Dock.Left);
                    row.Children.Add(image);
                    var id = c.Id;
                    _ = Task.Run(() => { try { return loadIcon(id); } catch { return null; } }).ContinueWith(t =>
                    {
                        if (t.Result is { } src) image.Dispatcher.BeginInvoke(() => image.Source = src);
                    }, TaskScheduler.Default);
                }
                else if (c.Icon is { Length: > 0 } && IconNames.IsValid(c.Icon))
                {
                    var icon = Ui.Icon(c.Icon, 16, Ui.Secondary);
                    icon.Margin = new Thickness(0, 0, 8, 0);
                    DockPanel.SetDock(icon, Dock.Left);
                    row.Children.Add(icon);
                }
                var check = Ui.Icon(SymbolRegular.Checkmark16, 14, Ui.Accent);
                DockPanel.SetDock(check, Dock.Right);
                check.Visibility = string.Equals(c.Id, selectedId, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
                row.Children.Add(check);
                row.Children.Add(Ui.Text(c.Label, Ui.Primary, 14));
                var item = new ListBoxItem { Content = row, Tag = c, IsSelected = check.Visibility == Visibility.Visible };
                System.Windows.Automation.AutomationProperties.SetName(item, c.Label);
                item.MouseLeftButtonUp += (_, _) =>
                {
                    selectedId = c.Id;
                    a.Commit(a.Param.Type == ParamType.App ? ParamValue.App(c.Id, c.Label) : ParamValue.Choice(c.Id));
                    foreach (var other in list.Items.OfType<ListBoxItem>())
                        if (other.Content is DockPanel dp) foreach (var ic in dp.Children.OfType<SymbolIcon>().Where(i => i.Symbol == SymbolRegular.Checkmark16))
                                ic.Visibility = other == item ? Visibility.Visible : Visibility.Collapsed;
                };
                list.Items.Add(item);
            }
        }
        if (filter is not null) filter.TextChanged += (_, _) => Fill();
        Fill();
        return root;
    }

    // ---- date & time -------------------------------------------------------------------------

    private static FrameworkElement DateEditor(EditorArgs a, bool withTime)
    {
        var current = (a.Value ?? a.Param.Default) is DateValue d ? d.Value.LocalDateTime : (DateTime?)null;
        var picker = new WpfDatePicker { SelectedDate = current?.Date, MinWidth = 160 };
        var time = new UiTextBox { PlaceholderText = "09:00", Width = 90, Text = current is { } c && withTime ? c.ToString("HH:mm", CultureInfo.InvariantCulture) : "", Margin = new Thickness(8, 0, 0, 0) };
        var error = Ui.Text("Type a time like 09:30.", Ui.Critical, 12);
        error.Visibility = Visibility.Collapsed;

        void Push()
        {
            if (picker.SelectedDate is not { } date) return;
            var t = TimeOnly.MinValue;
            if (withTime && !string.IsNullOrWhiteSpace(time.Text))
            {
                if (!TryParseTime(time.Text, out t)) { error.Visibility = Visibility.Visible; return; }
            }
            error.Visibility = Visibility.Collapsed;
            var local = date.Date.Add(t.ToTimeSpan());
            a.Commit(ParamValue.Date(new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local))));
        }

        picker.SelectedDateChanged += (_, _) => Push();
        time.TextChanged += (_, _) => Push();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(picker);
        if (withTime) row.Children.Add(time);
        var today = Ui.TextButton("Today", () => { picker.SelectedDate = DateTime.Today; });
        today.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(today);
        var root = new StackPanel();
        root.Children.Add(row);
        root.Children.Add(error);
        return root;
    }

    internal static bool TryParseTime(string text, out TimeOnly time) =>
        TimeOnly.TryParse(text.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out time) ||
        TimeOnly.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private static FrameworkElement TimeEditor(EditorArgs a)
    {
        var current = (a.Value ?? a.Param.Default) is TimeValue t ? t.Value : (TimeOnly?)null;
        var box = new UiTextBox { PlaceholderText = "07:30", Width = 110, Text = current?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "" };
        var error = Ui.Text("Type a time like 07:30.", Ui.Critical, 12);
        error.Visibility = Visibility.Collapsed;
        box.TextChanged += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(box.Text)) { error.Visibility = Visibility.Collapsed; return; }
            if (TryParseTime(box.Text, out var v)) { error.Visibility = Visibility.Collapsed; a.Commit(ParamValue.Time(v)); }
            else error.Visibility = Visibility.Visible;
        };
        var root = new StackPanel();
        root.Children.Add(box);
        root.Children.Add(error);
        return root;
    }

    private static FrameworkElement Duration(EditorArgs a)
    {
        var seconds = (a.Value ?? a.Param.Default) is DurationValue d ? d.Seconds : (double?)null;
        var units = new (string Label, double Factor)[] { ("seconds", 1), ("minutes", 60), ("hours", 3600) };
        var unitIndex = 0;
        if (seconds is { } s && s > 0) unitIndex = s % 3600 == 0 ? 2 : s % 60 == 0 ? 1 : 0;
        var box = new UiTextBox { Width = 100, PlaceholderText = "0", Text = seconds is { } sv ? ContentFormat.Number(sv / units[unitIndex].Factor) : "" };
        var combo = new WpfComboBox { Width = 110, Margin = new Thickness(8, 0, 0, 0), ItemsSource = units.Select(u => u.Label).ToList(), SelectedIndex = unitIndex };
        void Push()
        {
            if (!double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var v) &&
                !double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return;
            if (v < 0) return;
            a.Commit(ParamValue.Duration(v * units[Math.Max(0, combo.SelectedIndex)].Factor));
        }
        box.TextChanged += (_, _) => Push();
        combo.SelectionChanged += (_, _) => Push();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(box);
        row.Children.Add(combo);
        return row;
    }

    // ---- files, apps -------------------------------------------------------------------------

    private static FrameworkElement PathEditor(EditorArgs a)
    {
        var current = (a.Value ?? a.Param.Default) switch { FileValue f => f.Path, TextValue t => t.ToLiteralText(), _ => "" };
        var folder = a.Param.Type == ParamType.Folder;
        var box = new UiTextBox { PlaceholderText = a.Param.Placeholder ?? (folder ? "Choose a folder…" : "Choose a file…"), Text = current };
        box.TextChanged += (_, _) => a.Commit(string.IsNullOrWhiteSpace(box.Text) ? null : ParamValue.File(box.Text.Trim()));
        var browse = Ui.TextButton("Browse…", () =>
        {
            if (folder)
            {
                var dlg = new Microsoft.Win32.OpenFolderDialog { Title = a.Param.Label };
                if (dlg.ShowDialog() == true) box.Text = dlg.FolderName;
            }
            else
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = a.Param.Label };
                if (dlg.ShowDialog() == true) box.Text = dlg.FileName;
            }
        });
        browse.Margin = new Thickness(8, 0, 0, 0);
        var grid = new Grid { MinWidth = 320 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(box);
        Grid.SetColumn(browse, 1);
        grid.Children.Add(browse);
        return grid;
    }

    private static FrameworkElement AppPath(EditorArgs a)
    {
        var current = (a.Value ?? a.Param.Default) is AppValue app ? app.Name ?? app.Id : "";
        var box = new UiTextBox { PlaceholderText = "App name or program file", Text = current };
        box.TextChanged += (_, _) =>
        {
            var text = box.Text.Trim();
            a.Commit(text.Length == 0 ? null : ParamValue.App(text, System.IO.Path.GetFileNameWithoutExtension(text) is { Length: > 0 } n ? n : text));
        };
        var browse = Ui.TextButton("Browse…", () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = a.Param.Label, Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*" };
            if (dlg.ShowDialog() == true) box.Text = dlg.FileName;
        });
        browse.Margin = new Thickness(8, 0, 0, 0);
        var grid = new Grid { MinWidth = 320 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(box);
        Grid.SetColumn(browse, 1);
        grid.Children.Add(browse);
        return grid;
    }

    private static FrameworkElement Hotkey(EditorArgs a)
    {
        var rec = new HotkeyRecorder { Value = (a.Value ?? a.Param.Default) is HotkeyValue h ? h.Keys : null };
        rec.ValueChanged += v => a.Commit(v is null ? null : ParamValue.Hotkey(v));
        return rec;
    }

    // ---- color ------------------------------------------------------------------------------

    private static readonly string[] Swatches =
    {
        "#000000", "#FFFFFF", "#D92D20", "#E8590C", "#F08C00", "#2B8A3E", "#099268", "#0C8599", "#1971C2", "#2B5DD8", "#4C3FD1", "#8132D6", "#D6336C", "#8B5E34", "#6C757D", "#343A40",
    };

    private static FrameworkElement ColorEditor(EditorArgs a)
    {
        var current = (a.Value ?? a.Param.Default)?.ToLiteralText() ?? "";
        var hex = new UiTextBox { PlaceholderText = "#RRGGBB", Width = 130, Text = current };
        var wrap = new WrapPanel { Width = 280 };
        void Select(string c) { hex.Text = c; }
        foreach (var c in Swatches)
        {
            var sw = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, 6, 6), Cursor = System.Windows.Input.Cursors.Hand,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c)), BorderThickness = new Thickness(1), ToolTip = c,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 0x80, 0x80, 0x80)),
            };
            sw.MouseLeftButtonUp += (_, _) => Select(c);
            wrap.Children.Add(sw);
        }
        hex.TextChanged += (_, _) =>
        {
            var t = hex.Text.Trim();
            if (t.Length == 7 && t[0] == '#' && IsHex(t)) a.Commit(ParamValue.Text(t.ToUpperInvariant()));
        };
        var root = new StackPanel();
        root.Children.Add(wrap);
        root.Children.Add(hex);
        return root;
    }

    private static bool IsHex(string t) => t.Skip(1).All(Uri.IsHexDigit);

    // ---- variable name -----------------------------------------------------------------------

    private static FrameworkElement VariableName(EditorArgs a)
    {
        var names = a.Variables().Where(v => v.Group == VariableGroup.Named).Select(v => v.Name).ToList();
        var combo = new WpfComboBox { IsEditable = true, ItemsSource = names, MinWidth = 220, Text = (a.Value ?? a.Param.Default)?.ToLiteralText() ?? "" };
        combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => a.Commit(ParamValue.Text(combo.Text))));
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string s) a.Commit(ParamValue.Text(s)); };
        var root = new StackPanel();
        root.Children.Add(combo);
        root.Children.Add(Ui.Text("Pick one you made earlier or type a new name.", Ui.Secondary, 12, wrap: true));
        return root;
    }
}
