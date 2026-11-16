using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutomationPortal.App.Dialogs;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Logic;
using AutomationPortal.App.Services;
using AutomationPortal.Core;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Pages;

public partial class GalleryPage : UserControl, IAppPage
{
    private readonly AppHost _host;
    private string? _lastAddedName;
    private IReadOnlyList<string> _libraryNames = Array.Empty<string>();

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
            if (item is not null) _ = TemplateCatalog.ForFile(item.FilePath) is { } template ? SetUpAsync(item, template) : ShowDetailsAsync(item);
        }
    }

    private void Load()
    {
        var sections = _host.GallerySections(reload: true)
            .Select(sec => new GallerySection { Title = sec.Title, Items = sec.Items.Where(i => !StarterSnippets.IsGalleryDuplicate(i.Shortcut.Name)).ToList() })
            .Where(sec => sec.Items.Count > 0).ToList();
        _libraryNames = _host.Library.All.Select(x => x.Name).ToList();
        Sections.Children.Clear();
        EmptyState.Visibility = sections.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var section in sections)
        {
            var heading = new TextBlock { Text = section.Title, Margin = new Thickness(2, 14, 0, 8) };
            heading.Style = (Style)FindResource("SectionTitle");
            Sections.Children.Add(heading);
            var wrap = new WrapPanel();
            foreach (var item in section.Items)
                wrap.Children.Add(TemplateCatalog.ForFile(item.FilePath) is { } template ? BuildTemplateCard(item, template) : BuildTile(item));
            if (section.Items.Any(i => TemplateCatalog.ForFile(i.FilePath) is not null))
                foreach (var soon in TemplateCatalog.All.Where(t => t.ComingSoon)) wrap.Children.Add(BuildTemplateCard(null, soon));
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
        var fg = (SolidColorBrush)new BrushConverter().ConvertFromString(Logic.TileAppearance.TextFor(Palette.Hex(s.Color).Bottom))!;
        var grid = new Grid { Margin = new Thickness(14, 12, 14, 12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.Children.Add(Ui.Icon(s.Icon, 26, fg));
        var name = new TextBlock { Text = s.Name, Foreground = fg, FontWeight = FontWeights.SemiBold, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(name, 1);
        head.Children.Add(name);
        grid.Children.Add(head);
        var desc = new TextBlock
        {
            Text = s.Description ?? "", Foreground = fg, FontSize = 13, TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 34, Margin = new Thickness(0, 10, 0, 0),
        };
        Grid.SetRow(desc, 1);
        grid.Children.Add(desc);
        var footer = new Grid { VerticalAlignment = VerticalAlignment.Bottom };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var soft = fg;
        if (GalleryCardInfo.NeededApp(s) is { } needed)
        {
            var badge = new TextBlock { Text = "Needs " + needed, Foreground = soft, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            footer.Children.Add(badge);
        }
        if (_libraryNames.Count > 0 && GalleryCardInfo.IsAdded(s.Name, _libraryNames))
        {
            var added = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            added.Children.Add(Ui.Icon("Checkmark24", 14, fg));
            added.Children.Add(new TextBlock { Text = "Added", Foreground = fg, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 0, 0, 0) });
            Grid.SetColumn(added, 1);
            footer.Children.Add(added);
        }
        else
        {
            var risky = ShortcutPackage.Inspect(s, _host.Actions).Count > 0;
            var quick = new Wpf.Ui.Controls.Button { Content = risky ? "Add…" : "Add", Appearance = ControlAppearance.Secondary, Padding = new Thickness(12, 3, 12, 3), Opacity = 0 };
            AutomationProperties.SetName(quick, "Add " + s.Name);
            quick.Click += (_, e) => { e.Handled = true; if (risky) _ = ShowDetailsAsync(item); else _ = AddAsync(item, withAutomation: false); };
            Grid.SetColumn(quick, 1);
            footer.Children.Add(quick);
            void Sync() => quick.Opacity = button.IsMouseOver || button.IsKeyboardFocusWithin ? 1 : 0;
            button.MouseEnter += (_, _) => Sync();
            button.MouseLeave += (_, _) => Sync();
            button.IsKeyboardFocusWithinChanged += (_, _) => Sync();
        }
        Grid.SetRow(footer, 2);
        grid.Children.Add(footer);
        button.Content = grid;
        button.Click += (_, _) => _ = ShowDetailsAsync(item);
        return button;
    }

    // ---- templates ("Start here") -----------------------------------------------------------

    /// <summary>A wide card: icon, name, badge, one sentence, what it will do in three lines, and Set up.</summary>
    private UIElement BuildTemplateCard(GalleryItem? item, TemplateInfo t)
    {
        var card = new Border { Width = 486, Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(16), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        var stack = new StackPanel();

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Ui.Chip(t.Icon, t.Color, 36, 18));
        var name = Ui.Text(t.Title, size: 15, weight: FontWeights.SemiBold);
        name.Margin = new Thickness(12, 0, 8, 0);
        name.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(name, 1);
        head.Children.Add(name);
        var badge = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center, Child = Ui.Text(t.Badge, size: 13) };
        badge.SetResourceReference(Border.BackgroundProperty, t.ChangesFiles && t.Badge == "Changes files" ? "SystemFillColorCautionBackgroundBrush" : "SubtleFillColorSecondaryBrush");
        Grid.SetColumn(badge, 2);
        head.Children.Add(badge);
        stack.Children.Add(head);

        var summary = Ui.Text(t.Summary, secondary: true, size: 13);
        summary.Margin = new Thickness(0, 10, 0, 10);
        stack.Children.Add(summary);

        var story = new StackPanel();
        story.Children.Add(Ui.Text(Strings.Get("GalleryPage_WhatItWillDo"), size: 13, weight: FontWeights.SemiBold));
        for (var i = 0; i < t.Story.Count; i++)
        {
            var line = Ui.Text($"{i + 1}  {t.Story[i]}", size: 13);
            line.Margin = new Thickness(0, 4, 0, 0);
            story.Children.Add(line);
        }
        var storyBox = new Border { Padding = new Thickness(12, 8, 12, 10), CornerRadius = new CornerRadius(6), Child = story };
        storyBox.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorSecondaryBrush");
        stack.Children.Add(storyBox);

        var button = new Wpf.Ui.Controls.Button
        {
            Content = t.ComingSoon ? Strings.Get("Templates_ComingSoon") : Strings.Get("GalleryPage_SetUp"), Appearance = t.ComingSoon ? ControlAppearance.Secondary : ControlAppearance.Primary,
            IsEnabled = !t.ComingSoon && item is not null, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0),
        };
        AutomationProperties.SetName(button, (t.ComingSoon ? "Coming soon: " : "Set up ") + t.Title);
        if (item is not null) button.Click += (_, _) => _ = SetUpAsync(item, t);
        stack.Children.Add(button);
        card.Child = stack;
        return card;
    }

    private async Task SetUpAsync(GalleryItem item, TemplateInfo t)
    {
        var name = await TemplateSheet.ShowAsync(_host, Owner, item, t);
        if (name is null) return;
        _lastAddedName = name;
        AddedText.Text = $"Added \"{name}\" to your shortcuts.";
        AddedBar.Visibility = Visibility.Visible;
        Load();
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

        var stepsHeading = Ui.Text(Strings.Get("GalleryPage_WhatItDoes"), size: 14, weight: FontWeights.SemiBold);
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
            text.Children.Add(Ui.Text(d?.Title ?? Strings.Get("GalleryPage_UnknownStep"), weight: FontWeights.SemiBold, size: 14));
            if (d is not null && d.Summary.Length > 0)
                text.Children.Add(Ui.Text(Logic.AutomationSentence.RenderSummary(d.SummarySegments, d.Parameters, a.Parameters), secondary: true, size: 12));
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            body.Children.Add(row);
        }

        var dlg = new FlowDialog(s.Name, body, Strings.Get("GalleryPage_AddShortcut"), "Close", width: 560);
        dlg.AddLeftButton(Strings.Get("GalleryPage_AddAndSchedule"), () => { dlg.Close(); _ = AddAsync(item, withAutomation: true); });
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
        Load();   // the card now says Added

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
        var dlg = new FlowDialog("Set up " + copy.Name, body, Strings.Get("GalleryPage_AddShortcut"), "Cancel", width: 520);
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
        var dlg = new OpenFileDialog { Title = Strings.Get("GalleryPage_OpenAShortcutFile"), Filter = "Automation Portal shortcut (*.autoportal;*.flowmate)|*.autoportal;*.flowmate" };
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
