using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Flowmate.App.Dialogs;
using Flowmate.App.Infrastructure;
using Flowmate.Core;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace Flowmate.App.Services;

/// <summary>
/// Real <see cref="IUserInteraction"/>: topmost Fluent dialogs centred on the active monitor. Safe to call from any
/// thread; everything runs on the UI thread. Cancelling throws <see cref="UserCancelledException"/>.
/// </summary>
public sealed class UserInteraction : IUserInteraction
{
    private readonly IUiThread _ui;

    public UserInteraction(IUiThread ui) { _ui = ui; }

    private Task<T> OnUi<T>(Func<Task<T>> work) => _ui.InvokeTaskAsync(work);

    // ---- alert ------------------------------------------------------------------------------

    public Task<bool> ShowAlertAsync(string title, string message, bool showCancel, CancellationToken ct = default) =>
        OnUi(async () =>
        {
            var body = new StackPanel();
            body.Children.Add(Ui.Text(message, size: 14));
            var dlg = new FlowDialog(string.IsNullOrWhiteSpace(title) ? "Flow Mate" : title, body, "OK", showCancel ? "Cancel" : null);
            var ok = await dlg.ShowTopmostAsync(ct);
            if (showCancel && !ok) return false;
            return true;
        });

    // ---- ask for input ----------------------------------------------------------------------

    public Task<ContentValue> AskForInputAsync(string prompt, InputKind kind, string? defaultValue, CancellationToken ct = default) =>
        OnUi(async () =>
        {
            var body = new StackPanel();
            body.Children.Add(Ui.Text(prompt, size: 14));
            var error = Ui.Text("", size: 12);
            error.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
            error.Margin = new Thickness(0, 6, 0, 0);
            error.Visibility = Visibility.Collapsed;

            var field = new InputField(kind, defaultValue);
            field.Element.Margin = new Thickness(0, 12, 0, 0);
            body.Children.Add(field.Element);
            body.Children.Add(error);

            var dlg = new FlowDialog("Flow Mate", body, "OK", "Cancel", width: kind == InputKind.Multiline ? 560 : 460);
            dlg.Validate = () =>
            {
                var msg = field.Validate();
                error.Text = msg ?? "";
                error.Visibility = msg is null ? Visibility.Collapsed : Visibility.Visible;
                return msg is null;
            };
            dlg.FocusWhenShown(field.FocusTarget);
            if (!await dlg.ShowTopmostAsync(ct)) throw new UserCancelledException();
            return field.GetValue();
        });

    // ---- form -------------------------------------------------------------------------------

    public Task<ContentValue> AskForFormAsync(string title, IReadOnlyList<FormField> fields, CancellationToken ct = default) =>
        OnUi(async () =>
        {
            var body = new StackPanel();
            var inputs = new List<(FormField Field, InputField Input)>();
            foreach (var f in fields)
            {
                var input = new InputField(f);
                inputs.Add((f, input));
                body.Children.Add(Ui.Labeled(f.Label, input.Element, bottom: 14));
            }
            var error = Ui.Text("", size: 12);
            error.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
            error.Visibility = Visibility.Collapsed;
            body.Children.Add(error);

            var dlg = new FlowDialog(string.IsNullOrWhiteSpace(title) ? "Please fill in" : title, body, "OK", "Cancel", width: 520);
            dlg.Validate = () =>
            {
                foreach (var (f, input) in inputs)
                {
                    var msg = input.Validate();
                    if (msg is null) continue;
                    error.Text = $"{f.Label}: {msg}";
                    error.Visibility = Visibility.Visible;
                    return false;
                }
                return true;
            };
            if (inputs.Count > 0) dlg.FocusWhenShown(inputs[0].Input.FocusTarget);
            if (!await dlg.ShowTopmostAsync(ct)) throw new UserCancelledException();
            return ContentValue.Dict(inputs.Select(i => new KeyValuePair<string, ContentValue>(i.Field.Label, i.Input.GetValue())));
        });

    // ---- choose -----------------------------------------------------------------------------

    public Task<ContentValue> ChooseFromListAsync(string prompt, IReadOnlyList<ContentItem> items, bool multiSelect, CancellationToken ct = default) =>
        OnUi(async () =>
        {
            var body = new StackPanel();
            body.Children.Add(Ui.Text(prompt, size: 14));
            var list = new System.Windows.Controls.ListBox
            {
                SelectionMode = multiSelect ? SelectionMode.Extended : SelectionMode.Single,
                Margin = new Thickness(0, 12, 0, 0), MaxHeight = 320, MinHeight = 80,
                ItemsSource = items.Select(i => new ListEntry(i)).ToList(),
            };
            if (!multiSelect && items.Count > 0) list.SelectedIndex = 0;
            body.Children.Add(list);
            if (multiSelect) body.Children.Add(Ui.Text("Hold Ctrl or Shift to choose more than one.", secondary: true, size: 12));
            var dlg = new FlowDialog("Flow Mate", body, "OK", "Cancel", width: 480);
            list.MouseDoubleClick += (_, e) => { if (!multiSelect && list.SelectedItem is not null && e.OriginalSource is DependencyObject d && Ui.FindAncestor<ListBoxItem>(d) is not null) dlg.Accept(); };
            dlg.Validate = () => list.SelectedItems.Count > 0;
            dlg.FocusWhenShown(list);
            if (!await dlg.ShowTopmostAsync(ct)) throw new UserCancelledException();
            return ContentValue.FromItems(list.SelectedItems.Cast<ListEntry>().Select(e => e.Item));
        });

    private sealed record ListEntry(ContentItem Item)
    {
        public override string ToString() => Item.DisplayText;
    }

    public Task<int> ChooseFromMenuAsync(string prompt, IReadOnlyList<string> labels, CancellationToken ct = default) =>
        OnUi(async () =>
        {
            var body = new StackPanel();
            body.Children.Add(Ui.Text(prompt, size: 14));
            var chosen = -1;
            FlowDialog? dlg = null;
            var first = (UIElement?)null;
            for (var i = 0; i < labels.Count; i++)
            {
                var index = i;
                var b = new Wpf.Ui.Controls.Button
                {
                    Content = labels[i], HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, i == 0 ? 12 : 6, 0, 0), Padding = new Thickness(14, 10, 14, 10),
                };
                b.Click += (_, _) => { chosen = index; dlg!.Accept(); };
                body.Children.Add(b);
                first ??= b;
            }
            dlg = new FlowDialog("Flow Mate", body, null, "Cancel", width: 440);
            if (first is not null) dlg.FocusWhenShown(first);
            if (!await dlg.ShowTopmostAsync(ct) || chosen < 0) throw new UserCancelledException();
            return chosen;
        });

    // ---- show --------------------------------------------------------------------------------

    public Task ShowResultAsync(string title, ContentValue value, CancellationToken ct = default) =>
        OnUi(async () =>
        {
            var dlg = new FlowDialog(string.IsNullOrWhiteSpace(title) ? "Result" : title, ResultView.Build(value), "Done", null, width: 560);
            dlg.AddLeftButton("Copy", () => { try { System.Windows.Clipboard.SetText(value.AsText()); } catch (System.Runtime.InteropServices.COMException) { } });
            await dlg.ShowTopmostAsync(ct);
            return true;
        });

    public Task PreviewAsync(ContentValue value, CancellationToken ct = default) =>
        OnUi(async () =>
        {
            var dlg = new FlowDialog("Preview", ResultView.Build(value), "Close", null, width: 600);
            await dlg.ShowTopmostAsync(ct);
            return true;
        });

    // ---- pickers -----------------------------------------------------------------------------

    private static string BuildFilter(IReadOnlyList<FileFilter>? filters)
    {
        if (filters is null || filters.Count == 0) return "All files (*.*)|*.*";
        return string.Join("|", filters.Select(f => $"{f.Name} ({f.Pattern})|{f.Pattern}")) + "|All files (*.*)|*.*";
    }

    /// <summary>Invisible topmost window that owns native dialogs so they appear in front, on the active monitor.</summary>
    private static Window CreateOwner()
    {
        var owner = new Window
        {
            Width = 1, Height = 1, WindowStyle = WindowStyle.None, ShowInTaskbar = false, Topmost = true,
            AllowsTransparency = true, Background = System.Windows.Media.Brushes.Transparent, ShowActivated = true,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        var mon = Monitors.FromCursor();
        owner.Left = mon.X + mon.WorkWidth / mon.Scale / 2;
        owner.Top = mon.Y + mon.WorkHeight / mon.Scale / 2;
        owner.Show();
        return owner;
    }

    private Task<T> WithNativeDialog<T>(Func<Window, T> show) =>
        OnUi(() =>
        {
            var owner = CreateOwner();
            try { return Task.FromResult(show(owner)); }
            finally { owner.Close(); }
        });

    public Task<ContentValue> PickFilesAsync(string prompt, bool multiple, IReadOnlyList<FileFilter>? filters = null, CancellationToken ct = default) =>
        WithNativeDialog(owner =>
        {
            var d = new OpenFileDialog { Title = string.IsNullOrWhiteSpace(prompt) ? "Choose files" : prompt.TrimEnd(':'), Multiselect = multiple, Filter = BuildFilter(filters), CheckFileExists = true };
            if (d.ShowDialog(owner) != true) throw new UserCancelledException();
            return ContentValue.Files(d.FileNames);
        });

    public Task<ContentValue> PickFolderAsync(string prompt, CancellationToken ct = default) =>
        WithNativeDialog(owner =>
        {
            var d = new OpenFolderDialog { Title = string.IsNullOrWhiteSpace(prompt) ? "Choose a folder" : prompt.TrimEnd(':') };
            if (d.ShowDialog(owner) != true) throw new UserCancelledException();
            return ContentValue.Folder(d.FolderName);
        });

    public Task<string> PickSaveFileAsync(string prompt, string? suggestedName = null, IReadOnlyList<FileFilter>? filters = null, CancellationToken ct = default) =>
        WithNativeDialog(owner =>
        {
            var d = new SaveFileDialog { Title = string.IsNullOrWhiteSpace(prompt) ? "Save as" : prompt.TrimEnd(':'), FileName = suggestedName ?? "", Filter = BuildFilter(filters), OverwritePrompt = true };
            if (d.ShowDialog(owner) != true) throw new UserCancelledException();
            return d.FileName;
        });

    // ---- screen ------------------------------------------------------------------------------

    public Task<ScreenRect?> SelectScreenRegionAsync(CancellationToken ct = default) =>
        OnUi(() => ScreenSelection.SelectRegionAsync(ct));

    public Task<ScreenPoint?> SelectScreenPointAsync(CancellationToken ct = default) =>
        OnUi(() => ScreenSelection.SelectPointAsync(ct));

    // ---- one input control ------------------------------------------------------------------

    private sealed class InputField
    {
        private readonly InputKind _kind;
        private readonly FormFieldKind? _formKind;
        private readonly System.Windows.Controls.TextBox? _text;
        private readonly CalendarDatePicker? _date;
        private readonly TimePicker? _timeOfDay;
        private readonly System.Windows.Controls.ComboBox? _choice;

        public FrameworkElement Element { get; }
        public UIElement FocusTarget { get; }

        public InputField(InputKind kind, string? @default) : this(kind, null, @default, null) { }

        public InputField(FormField f) : this(f.Kind switch
        {
            FormFieldKind.Multiline => InputKind.Multiline, FormFieldKind.Number => InputKind.Number,
            FormFieldKind.Date => InputKind.Date, FormFieldKind.Time => InputKind.Time, _ => InputKind.Text,
        }, f.Kind, f.Default, f.Options) { }

        private InputField(InputKind kind, FormFieldKind? formKind, string? @default, IReadOnlyList<string>? options)
        {
            _kind = kind; _formKind = formKind;
            if (formKind == FormFieldKind.Choice)
            {
                _choice = new System.Windows.Controls.ComboBox { ItemsSource = options ?? Array.Empty<string>(), HorizontalAlignment = HorizontalAlignment.Stretch };
                _choice.SelectedItem = options?.FirstOrDefault(o => o == @default) ?? options?.FirstOrDefault();
                Element = _choice; FocusTarget = _choice;
                return;
            }
            switch (kind)
            {
                case InputKind.Date:
                    _date = new CalendarDatePicker { HorizontalAlignment = HorizontalAlignment.Stretch, Date = TryParseDate(@default) ?? DateTime.Today, IsTodayHighlighted = true };
                    _timeOfDay = new TimePicker { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 8, 0, 0) };
                    var sp = new StackPanel();
                    sp.Children.Add(_date);
                    sp.Children.Add(Ui.Text("Time (optional)", secondary: true, size: 12));
                    ((System.Windows.Controls.TextBlock)sp.Children[1]).Margin = new Thickness(0, 10, 0, 0);
                    sp.Children.Add(_timeOfDay);
                    Element = sp; FocusTarget = _date;
                    break;
                case InputKind.Time:
                    _timeOfDay = new TimePicker { HorizontalAlignment = HorizontalAlignment.Stretch };
                    if (TimeOnly.TryParse(@default, CultureInfo.CurrentCulture, out var t) || TimeOnly.TryParse(@default, CultureInfo.InvariantCulture, out t))
                        _timeOfDay.SelectedTime = t.ToTimeSpan();
                    else _timeOfDay.SelectedTime = DateTime.Now.TimeOfDay;
                    Element = _timeOfDay; FocusTarget = _timeOfDay;
                    break;
                default:
                    _text = new System.Windows.Controls.TextBox
                    {
                        Text = @default ?? "", MinWidth = 200,
                        AcceptsReturn = kind == InputKind.Multiline, TextWrapping = TextWrapping.Wrap,
                        MinHeight = kind == InputKind.Multiline ? 140 : 0, VerticalContentAlignment = kind == InputKind.Multiline ? VerticalAlignment.Top : VerticalAlignment.Center,
                        VerticalScrollBarVisibility = kind == InputKind.Multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
                    };
                    if (_text is { } tb)
                    {
                        tb.SetValue(Wpf.Ui.Controls.TextBox.PlaceholderTextProperty, kind switch
                        {
                            InputKind.Number => "Type a number", InputKind.Url => "https://", _ => "",
                        });
                    }
                    Element = _text; FocusTarget = _text;
                    break;
            }
        }

        private static DateTime? TryParseDate(string? s) =>
            DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out var d) || DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : null;

        private static bool TryNumber(string s, out double n) =>
            double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out n) || double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out n);

        public string? Validate()
        {
            if (_text is null) return null;
            var s = _text.Text;
            switch (_kind)
            {
                case InputKind.Number:
                    return TryNumber(s, out _) ? null : "Type a number, like 12 or 3.5.";
                case InputKind.Url:
                    if (string.IsNullOrWhiteSpace(s)) return null;
                    var withScheme = s.Contains("://") ? s : "https://" + s;
                    return Uri.TryCreate(withScheme, UriKind.Absolute, out var u) && u.Host.Length > 0 ? null : "That doesn't look like a web address.";
                default:
                    return null;
            }
        }

        public ContentValue GetValue()
        {
            if (_choice is not null) return ContentValue.Text(_choice.SelectedItem as string ?? "");
            switch (_kind)
            {
                case InputKind.Number:
                    return TryNumber(_text!.Text, out var n) ? ContentValue.Number(n) : ContentValue.Empty;
                case InputKind.Date:
                    var day = _date!.Date ?? DateTime.Today;
                    var time = _timeOfDay?.SelectedTime ?? TimeSpan.Zero;
                    return ContentValue.Date(new DateTimeOffset(day.Date.Add(time)));
                case InputKind.Time:
                    return ContentValue.Text(TimeOnly.FromTimeSpan(_timeOfDay!.SelectedTime ?? TimeSpan.Zero).ToString("HH:mm", CultureInfo.InvariantCulture));
                case InputKind.Url:
                    var s = _text!.Text.Trim();
                    if (s.Length == 0) return ContentValue.Text("");
                    var full = s.Contains("://") ? s : "https://" + s;
                    return Uri.TryCreate(full, UriKind.Absolute, out var uri) ? ContentValue.Url(uri) : ContentValue.Text(s);
                default:
                    return ContentValue.Text(_text!.Text);
            }
        }
    }
}
