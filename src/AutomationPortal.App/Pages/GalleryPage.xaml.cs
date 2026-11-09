using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutomationPortal.App.Dialogs;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Services;
using AutomationPortal.Core;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Pages;

public partial class GalleryPage : UserControl, IAppPage
{
    private readonly AppHost _host;
    private string? _lastAddedName;

    public GalleryPage(AppHost host)
    {
        _host = host;
        InitializeComponent();
    }

    private Window? Owner => Window.GetWindow(this);

    public void Navigated(NavTarget target)
    {
        Load();
        if (target.Item is { Length: > 0 } file)
        {
            var item = _host.GallerySections().SelectMany(s => s.Items).FirstOrDefault(i => string.Equals(Path.GetFileName(i.FilePath), file, StringComparison.OrdinalIgnoreCase));
            if (item is not null) _ = ShowDetailsAsync(item);
        }
    }

    private void Load()
    {
        var sections = _host.GallerySections(reload: true);
        Sections.Children.Clear();
        EmptyState.Visibility = sections.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var section in sections)
        {
            var heading = new TextBlock { Text = section.Title, Margin = new Thickness(2, 14, 0, 8) };
            heading.Style = (Style)FindResource("SectionTitle");
            Sections.Children.Add(heading);
            var wrap = new WrapPanel();
            foreach (var item in section.Items) wrap.Children.Add(BuildTile(item));
            Sections.Children.Add(wrap);
        }
    }

    private UIElement BuildTile(GalleryItem item)
    {
        var s = item.Shortcut;
        var button = new System.Windows.Controls.Button
        {
            Style = (Style)FindResource("FlowTileButton"), Background = Palette.Gradient(s.Color), Width = 236, Height = 124, Margin = new Thickness(0, 0, 14, 14),
            ToolTip = string.IsNullOrWhiteSpace(s.Description) ? s.Name : $"{s.Name}\n{s.Description}",
        };
        AutomationProperties.SetName(button, s.Name);
        var grid = new Grid { Margin = new Thickness(14, 12, 14, 12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.Children.Add(Ui.Icon(s.Icon, 26, Brushes.White));
        var name = new TextBlock { Text = s.Name, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(name, 1);
        head.Children.Add(name);
        grid.Children.Add(head);
        var desc = new TextBlock
        {
            Text = s.Description ?? "", Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 255, 255, 255)), FontSize = 12, TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 52, Margin = new Thickness(0, 10, 0, 0),
        };
        Grid.SetRow(desc, 1);
        grid.Children.Add(desc);
        var add = new TextBlock { Text = "See details", Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 255, 255, 255)), FontSize = 12, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetRow(add, 2);
        grid.Children.Add(add);
        button.Content = grid;
        button.Click += (_, _) => _ = ShowDetailsAsync(item);
        return button;
    }

    // ---- details sheet ---------------------------------------------------------------------

    private async Task ShowDetailsAsync(GalleryItem item)
    {
        var s = item.Shortcut;
        var body = new StackPanel();

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.Children.Add(Ui.Chip(s.Icon, s.Color, 56, 28));
        var titles = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Ui.Text(s.Name, size: 22, weight: FontWeights.SemiBold));
        if (!string.IsNullOrWhiteSpace(s.Description)) titles.Children.Add(Ui.Text(s.Description!, secondary: true, size: 14));
        Grid.SetColumn(titles, 1);
        head.Children.Add(titles);
        body.Children.Add(head);

        var warnings = ShortcutPackage.Inspect(s, _host.Actions);
        if (warnings.Count > 0)
        {
            var kinds = warnings.Select(w => w.Kind == ImportWarningKind.Unknown ? $"{w.ActionId} (not available on this PC)" : w.Describe()).Distinct();
            var warn = new Border { Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(8), Child = Ui.Text("Heads up: this shortcut includes " + string.Join(", ", kinds) + ".", size: 13) };
            warn.SetResourceReference(Border.BackgroundProperty, "SystemFillColorCautionBackgroundBrush");
            body.Children.Add(warn);
        }

        var stepsHeading = Ui.Text("What it does", size: 14, weight: FontWeights.SemiBold);
        stepsHeading.Margin = new Thickness(0, 20, 0, 8);
        body.Children.Add(stepsHeading);
        var steps = s.Actions.Where(a => a.Flow is null or { Role: FlowRole.Start }).ToList();
        if (steps.Count == 0) body.Children.Add(Ui.Text("This shortcut has no steps yet.", secondary: true));
        var n = 0;
        foreach (var a in steps)
        {
            n++;
            var d = _host.Actions.Describe(a.ActionId);
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var chip = Ui.Chip(d?.Icon ?? "Question24", d?.Color ?? "gray", 30, 16);
            chip.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(chip);
            var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            text.Children.Add(Ui.Text(d?.Title ?? "Unknown step", weight: FontWeights.SemiBold, size: 14));
            if (d is not null && d.Summary.Length > 0)
                text.Children.Add(Ui.Text(Logic.AutomationSentence.RenderSummary(d.SummarySegments, d.Parameters, a.Parameters), secondary: true, size: 12));
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            body.Children.Add(row);
        }

        var dlg = new FlowDialog(s.Name, body, "Add Shortcut", "Close", width: 560);
        dlg.AddLeftButton("Add with Automation", () => { dlg.Close(); _ = AddAsync(item, withAutomation: true); });
        if (await dlg.ShowTopmostAsync(default, Owner)) await AddAsync(item, withAutomation: false);
    }

    // ---- adding ----------------------------------------------------------------------------

    private async Task AddAsync(GalleryItem item, bool withAutomation)
    {
        var copy = item.Shortcut.Clone();
        if (copy.ImportQuestions.Count > 0 && !await AskImportQuestionsAsync(copy)) return;
        ImportResult result;
        try { result = _host.Library.Import(copy); }
        catch (LibraryException ex) { await Prompts.MessageAsync(Owner, "Couldn't add it", ex.Message); return; }

        await _host.FinishImportAsync(result, Owner, warningsShown: true);   // the details sheet already listed any heads-up
        _lastAddedName = result.Shortcut.Name;
        AddedText.Text = result.RenamedFrom is null
            ? $"Added \"{result.Shortcut.Name}\" to your shortcuts."
            : $"Added \"{result.Shortcut.Name}\" to your shortcuts. You already had one called \"{result.RenamedFrom}\", so this one got a new name.";
        AddedBar.Visibility = Visibility.Visible;

        if (withAutomation) await AutomationWizardWindow.ShowAsync(_host, Owner, result.Shortcut.Id);
    }

    private async Task<bool> AskImportQuestionsAsync(Shortcut copy)
    {
        var parameters = new List<ParameterDescriptor>();
        var values = new Dictionary<string, ParamValue>();
        for (var i = 0; i < copy.ImportQuestions.Count; i++)
        {
            var q = copy.ImportQuestions[i];
            var actual = copy.FindAction(q.ActionUid) is { } a ? _host.Actions.Describe(a.ActionId)?.FindParameter(q.ParameterKey) : null;
            var type = actual?.Type switch
            {
                ParamType.Folder or ParamType.File or ParamType.SaveFile or ParamType.Number or ParamType.Boolean or ParamType.Choice or ParamType.Time or ParamType.Duration or ParamType.Date or ParamType.App => actual.Type,
                _ => ParamType.Text,
            };
            var key = i.ToString();
            parameters.Add(new ParameterDescriptor
            {
                Key = key, Label = q.Question, Type = type, Choices = actual?.Choices, ChoicesProvider = actual?.ChoicesProvider,
                Min = actual?.Min, Max = actual?.Max, Unit = actual?.Unit, Placeholder = actual?.Placeholder,
            });
            if (q.DefaultValue is { } def) values[key] = def;
        }

        var form = _host.ParameterForms.Create(parameters, values, _host.Services);
        var body = new StackPanel();
        body.Children.Add(Ui.Text("A few quick questions so this shortcut works for you.", secondary: true, size: 13));
        form.Element.Margin = new Thickness(0, 14, 0, 0);
        body.Children.Add(form.Element);
        var dlg = new FlowDialog("Set up " + copy.Name, body, "Add Shortcut", "Cancel", width: 520);
        if (!await dlg.ShowTopmostAsync(default, Owner)) return false;

        var answers = new List<ParamValue?>();
        for (var i = 0; i < copy.ImportQuestions.Count; i++)
            answers.Add(values.TryGetValue(i.ToString(), out var v) ? v : null);
        copy.ApplyImportAnswers(answers);
        return true;
    }

    private void OnShowAdded(object sender, RoutedEventArgs e)
    {
        AddedBar.Visibility = Visibility.Collapsed;
        (Owner as MainWindow)?.NavigateTo(new NavTarget(AppPage.Library, Query: _lastAddedName));
    }

    private void OnDismissAdded(object sender, RoutedEventArgs e) => AddedBar.Visibility = Visibility.Collapsed;

    private void OnCreate(object sender, RoutedEventArgs e) => _host.EditorLauncher.OpenNew(null);

    private async void OnOpenFile(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Open a shortcut file", Filter = "Automation Portal shortcut (*.autoportal;*.flowmate)|*.autoportal;*.flowmate" };
        if (dlg.ShowDialog(Owner) != true) return;
        try
        {
            var shortcut = ShortcutPackage.Parse(File.ReadAllText(dlg.FileName));
            await ShowDetailsAsync(new GalleryItem { FilePath = dlg.FileName, Shortcut = shortcut });
        }
        catch (Exception ex) when (ex is LibraryException or IOException)
        {
            await Prompts.MessageAsync(Owner, "Couldn't open that file", ex.Message);
        }
    }
}
