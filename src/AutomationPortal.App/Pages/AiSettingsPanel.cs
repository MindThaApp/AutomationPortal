using System.Windows;
using System.Windows.Controls;
using Flowmate.Actions.Documents;
using Flowmate.Ai.Windows;
using Flowmate.Core;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Flowmate.App.Pages;

/// <summary>Settings › AI: one card per AI provider (key, address, model, Test) and the default provider choice.</summary>
public sealed class AiSettingsPanel : UserControl
{
    private readonly AppHost _host;
    private readonly StackPanel _root = new();
    private readonly HashSet<string> _expanded = new();
    private readonly Dictionary<string, string> _status = new();

    public AiSettingsPanel(AppHost host)
    {
        _host = host;
        Content = _root;
        Rebuild();
        _ = RefreshWindowsAiAsync();
    }

    private AiProviderStore Store => AiProviderStore.For(_host.Services);
    private static Style? Res(string key) => Application.Current.TryFindResource(key) as Style;

    private TextBlock Text(string text, bool secondary = true, double size = 12, FontWeight? weight = null, bool wrap = true)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, FontWeight = weight ?? FontWeights.Normal };
        t.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush");
        return t;
    }

    public void Rebuild()
    {
        _root.Children.Clear();
        var store = Store;
        var defaultId = store.DefaultProviderId;

        _root.Children.Add(Text("Choose which AI the AI actions and Quick Run use. Your keys are stored encrypted on this PC.", wrap: true));
        ((TextBlock)_root.Children[0]).Margin = new Thickness(0, 0, 0, 10);

        var local = AiProviderRegistry.All.Where(p => p.IsLocal && p.Id != AiProviderIds.WindowsAi).ToList();
        foreach (var info in AiProviderRegistry.All.Where(p => !p.IsLocal)) _root.Children.Add(Card(info, store, defaultId));

        var header = Text("On this PC (free and private)", false, 14, FontWeights.SemiBold);
        header.Margin = new Thickness(0, 12, 0, 2);
        _root.Children.Add(header);
        var hint = Text("Windows AI needs nothing to install on a Copilot+ PC. Otherwise install Ollama or LM Studio, start it, and download a model. Then turn it on here. Nothing leaves your PC.");
        hint.Margin = new Thickness(0, 0, 0, 8);
        _root.Children.Add(hint);
        if (AiProviderRegistry.Get(AiProviderIds.WindowsAi) is not null) _root.Children.Add(WindowsAiCard(store, defaultId));
        foreach (var info in local) _root.Children.Add(Card(info, store, defaultId));
    }

    // ---- Windows AI (on this PC) -------------------------------------------------------------------

    private readonly WindowsAiProvider _windowsAi = new();
    private AiAvailability? _windowsAiState;
    private string? _windowsAiProgress;
    private bool _windowsAiBusy;

    private async Task RefreshWindowsAiAsync()
    {
        try { _windowsAiState = await _windowsAi.IsAvailableAsync(); }
        catch (Exception) { _windowsAiState = new AiAvailability(false, WindowsAiProvider.NotSupportedReason); }
        Rebuild();
    }

    private UIElement WindowsAiCard(AiProviderStore store, string defaultId)
    {
        var id = AiProviderIds.WindowsAi;
        var settings = store.Get(id);
        var state = _windowsAiState;
        var available = state?.IsAvailable == true;

        var card = new Border { Style = Res("SurfaceCard"), Margin = new Thickness(0, 0, 0, 6) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var texts = new StackPanel();
        var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
        nameLine.Children.Add(Text(WindowsAiProvider.Info.Name, false, 14, FontWeights.SemiBold, wrap: false));
        var suggested = available && id != defaultId && store.SetupProblem(defaultId) is not null;
        if (id == defaultId || suggested)
        {
            var badge = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            badge.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
            var b = Text(id == defaultId ? "Default" : "Suggested", false, 11, wrap: false);
            b.Foreground = System.Windows.Media.Brushes.White;
            badge.Child = b;
            nameLine.Children.Add(badge);
        }
        texts.Children.Add(nameLine);
        var status = _windowsAiProgress
            ?? (state is null ? "Checking this PC…"
                : available ? (settings.Enabled ? "Ready to use. Free, private and works offline." : "Available on this PC. Free, private and works offline.")
                : state.Reason + ".");
        texts.Children.Add(Text(status));
        grid.Children.Add(texts);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (state is not null && WindowsAiProvider.CanGetReady(state))
        {
            var ready = new Button { Content = "Get it ready", IsEnabled = !_windowsAiBusy, Margin = new Thickness(0, 0, 8, 0), Appearance = ControlAppearance.Primary };
            ready.Click += async (_, _) =>
            {
                _windowsAiBusy = true;
                _windowsAiProgress = "Windows is getting on-device AI ready…";
                Rebuild();
                var progress = new Progress<double>(p => { _windowsAiProgress = $"Windows is getting on-device AI ready… {Math.Round(p * 100)}%"; Rebuild(); });
                string? problem;
                try { problem = await _windowsAi.EnsureReadyAsync(progress); }
                catch (Exception ex) { problem = ex.Message; }
                _windowsAiBusy = false;
                _windowsAiProgress = problem;
                await RefreshWindowsAiAsync();
            };
            right.Children.Add(ready);
        }
        if (available && id != defaultId && settings.Enabled)
        {
            var make = new Button { Content = "Use as default", Margin = new Thickness(0, 0, 8, 0) };
            make.Click += (_, _) => { Store.Update(c => c.DefaultProvider = id); Rebuild(); };
            right.Children.Add(make);
        }
        var toggle = new ToggleSwitch { IsEnabled = available, IsChecked = available && settings.Enabled, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(toggle, available ? "Turn on " + WindowsAiProvider.Info.Name : WindowsAiProvider.Info.Name + ", not available: " + state?.Reason);
        toggle.Click += (_, _) =>
        {
            var on = toggle.IsChecked == true;
            Store.Update(c =>
            {
                var s = Store.Get(id); s.Enabled = on; c.Providers[id] = s;
                // Free and private: if nothing else is set up yet, it becomes the default the moment it is turned on.
                if (on && Store.SetupProblem(c.DefaultProvider) is not null) c.DefaultProvider = id;
            });
            Rebuild();
        };
        right.Children.Add(toggle);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        card.Child = grid;
        return card;
    }
    /// <summary>The line under a provider's name. Wording for this page: it is already on Settings › AI, so it says what to do, not where.</summary>
    public static string StatusText(AiProviderInfo info, AiProviderSettings settings, bool hasKey, string endpoint, bool ready)
    {
        if (ready) return "Ready to use.";
        var isClaude = info.Id == AiProviderIds.Claude;
        if (!isClaude && !settings.Enabled) return "Turned off.";
        var name = ShortName(info);
        if (info.NeedsKey && !hasKey) return $"Add your key to start using {name}.";
        if (info.NeedsEndpoint && string.IsNullOrWhiteSpace(endpoint)) return $"Enter the address to start using {name}.";
        if (info.UsesDeployment && string.IsNullOrWhiteSpace(settings.Deployment)) return $"Enter the deployment name to start using {name}.";
        return $"Finish setting up {name} to start using it.";
    }

    /// <summary>"Claude (Anthropic)" becomes "Claude".</summary>
    public static string ShortName(AiProviderInfo info)
    {
        var i = info.Name.IndexOf(" (", StringComparison.Ordinal);
        return i > 0 ? info.Name[..i] : info.Name;
    }

    private UIElement Card(AiProviderInfo info, AiProviderStore store, string defaultId)
    {
        var id = info.Id;
        var isClaude = id == AiProviderIds.Claude;
        var settings = store.Get(id);
        var problem = store.SetupProblem(id);
        var ready = problem is null;

        var card = new Border { Style = Res("SurfaceCard"), Margin = new Thickness(0, 0, 0, 6) };
        var body = new StackPanel();
        card.Child = body;

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel();
        var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
        nameLine.Children.Add(Text(info.Name, false, 14, FontWeights.SemiBold, wrap: false));
        if (id == defaultId)
        {
            var badge = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            badge.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
            var b = Text("Default", false, 11, wrap: false);
            b.Foreground = System.Windows.Media.Brushes.White;
            badge.Child = b;
            nameLine.Children.Add(badge);
        }
        title.Children.Add(nameLine);
        title.Children.Add(Text(StatusText(info, settings, store.GetKey(id) is not null, store.EffectiveEndpoint(id), ready)));
        head.Children.Add(title);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (!isClaude)
        {
            var toggle = new ToggleSwitch { IsChecked = settings.Enabled };
            System.Windows.Automation.AutomationProperties.SetName(toggle, "Turn on " + info.Name);
            toggle.Click += (_, _) =>
            {
                var on = toggle.IsChecked == true;
                Store.Update(c => { var s = Store.Get(id); s.Enabled = on; c.Providers[id] = s; });
                if (on) _expanded.Add(id);
                Rebuild();
            };
            right.Children.Add(toggle);
        }
        var expand = new Button { Content = _expanded.Contains(id) ? "Hide" : "Set up", Margin = new Thickness(8, 0, 0, 0) };
        expand.Click += (_, _) => { if (!_expanded.Remove(id)) _expanded.Add(id); Rebuild(); };
        right.Children.Add(expand);
        Grid.SetColumn(right, 1);
        head.Children.Add(right);
        body.Children.Add(head);

        if (!_expanded.Contains(id)) return card;

        var form = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        body.Children.Add(form);
        form.Children.Add(Text(info.Help));

        if (info.NeedsKey || info.OptionalKey)
        {
            var row = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var key = new Wpf.Ui.Controls.PasswordBox { PlaceholderText = store.GetKey(id) is null ? "Paste your key here" : "A key is saved. Paste a new one to replace it." };
            System.Windows.Automation.AutomationProperties.SetName(key, info.Name + " key");
            row.Children.Add(key);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
            var save = new Button { Content = "Save", Appearance = ControlAppearance.Primary };
            save.Click += (_, _) =>
            {
                if (key.Password.Trim().Length == 0) return;
                Store.SetKey(id, key.Password);
                if (!isClaude) Store.Update(c => { var s = Store.Get(id); s.Enabled = true; c.Providers[id] = s; });
                _status[id] = "Key saved.";
                Rebuild();
            };
            buttons.Children.Add(save);
            if (store.GetKey(id) is not null)
            {
                var remove = new Button { Content = "Remove", Margin = new Thickness(8, 0, 0, 0) };
                remove.Click += (_, _) => { Store.SetKey(id, null); _status[id] = "The key was removed."; Rebuild(); };
                buttons.Children.Add(remove);
            }
            Grid.SetColumn(buttons, 1);
            row.Children.Add(buttons);
            form.Children.Add(row);
        }

        if (info.NeedsEndpoint || info.IsLocal)
        {
            var box = new TextBox { Text = store.EffectiveEndpoint(id), PlaceholderText = info.DefaultEndpoint ?? "https://", Margin = new Thickness(0, 10, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(box, info.Name + " address");
            box.LostFocus += (_, _) => Store.Update(c => { var s = Store.Get(id); s.Endpoint = box.Text.Trim(); c.Providers[id] = s; });
            form.Children.Add(Text("Address", true, 12, FontWeights.SemiBold));
            ((TextBlock)form.Children[^1]).Margin = new Thickness(0, 10, 0, 0);
            box.Margin = new Thickness(0, 4, 0, 0);
            form.Children.Add(box);
        }

        if (info.UsesDeployment)
        {
            var box = new TextBox { Text = settings.Deployment ?? "", PlaceholderText = "for example: gpt-4o" };
            System.Windows.Automation.AutomationProperties.SetName(box, "Deployment name");
            box.LostFocus += (_, _) => Store.Update(c => { var s = Store.Get(id); s.Deployment = box.Text.Trim(); c.Providers[id] = s; });
            form.Children.Add(Text("Deployment name", true, 12, FontWeights.SemiBold));
            ((TextBlock)form.Children[^1]).Margin = new Thickness(0, 10, 0, 4);
            form.Children.Add(box);
        }

        // Model
        form.Children.Add(Text(isClaude ? "Model (leave empty to use the Fast / Smart / Best choice below)" : "Model", true, 12, FontWeights.SemiBold));
        ((TextBlock)form.Children[^1]).Margin = new Thickness(0, 10, 0, 4);
        var modelRow = new Grid();
        modelRow.ColumnDefinitions.Add(new ColumnDefinition());
        modelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var model = new System.Windows.Controls.ComboBox { IsEditable = true, Text = settings.Model ?? "" };
        System.Windows.Automation.AutomationProperties.SetName(model, info.Name + " model");
        void SaveModel() => Store.Update(c => { var s = Store.Get(id); s.Model = string.IsNullOrWhiteSpace(model.Text) ? null : model.Text.Trim(); c.Providers[id] = s; });
        model.LostFocus += (_, _) => SaveModel();
        model.SelectionChanged += (_, _) => { if (model.SelectedItem is string) Dispatcher.BeginInvoke(SaveModel); };
        modelRow.Children.Add(model);
        var refresh = new Button { Content = "Find models", Margin = new Thickness(8, 0, 0, 0) };
        var modelNote = Text("");
        refresh.Click += async (_, _) =>
        {
            if (Store.SetupProblem(id) is { } why) { modelNote.Text = why; return; }
            refresh.IsEnabled = false;
            modelNote.Text = "Looking for models…";
            try
            {
                var models = await Store.ListModelsAsync(id, default, refresh: true);
                model.ItemsSource = models;
                if (string.IsNullOrWhiteSpace(model.Text) && AiProviderStore.SuggestModel(models) is { } suggested) { model.Text = suggested; SaveModel(); }
                modelNote.Text = models.Count == 0 ? "No models found. Download one first." : $"{models.Count} models found.";
            }
            catch (Exception ex) { modelNote.Text = ex is ActionException ? ex.Message : "Couldn't get the list of models. " + ex.Message; }
            finally { refresh.IsEnabled = true; }
        };
        Grid.SetColumn(refresh, 1);
        modelRow.Children.Add(refresh);
        form.Children.Add(modelRow);
        form.Children.Add(modelNote);

        // Test + default
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var test = new Button { Content = "Test" };
        var busy = new ProgressRing { IsIndeterminate = true, Width = 16, Height = 16, Visibility = Visibility.Collapsed, Margin = new Thickness(10, 0, 0, 0) };
        var result = Text(_status.GetValueOrDefault(id) ?? "");
        result.VerticalAlignment = VerticalAlignment.Center;
        result.Margin = new Thickness(10, 0, 0, 0);
        test.Click += async (_, _) =>
        {
            test.IsEnabled = false;
            busy.Visibility = Visibility.Visible;
            result.Text = "Checking…";
            try
            {
                var r = await Store.TestConnectionAsync(id);
                _status[id] = result.Text = (r.Ok ? "It works. " : "") + r.Message;
            }
            catch (Exception ex) { result.Text = "Couldn't check. " + ex.Message; }
            finally { test.IsEnabled = true; busy.Visibility = Visibility.Collapsed; }
        };
        actions.Children.Add(test);
        if (id != defaultId)
        {
            var make = new Button { Content = "Use as default", Margin = new Thickness(8, 0, 0, 0) };
            make.Click += (_, _) => { Store.Update(c => c.DefaultProvider = id); Rebuild(); };
            actions.Children.Add(make);
        }
        actions.Children.Add(busy);
        actions.Children.Add(result);
        form.Children.Add(actions);
        return card;
    }
}
