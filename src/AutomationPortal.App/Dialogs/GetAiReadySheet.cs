using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AutomationPortal.Actions.Documents;
using AutomationPortal.Ai.Windows;
using AutomationPortal.App.Infrastructure;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Dialogs;

/// <summary>
/// "Get AI ready": the one place that offers the ways to get an AI going, in order: Portal AI Host, Windows on-device AI,
/// then the user's own API key. It never asks the host for access until the user presses Allow.
/// </summary>
public sealed class GetAiReadySheet
{
    private readonly AppHost _host;
    private readonly StackPanel _body = new();
    private readonly WindowsAiProvider _windows = new();
    private FlowDialog? _dialog;
    private PortalAiHostState? _hostState;
    private AiAvailability? _windowsState;
    private string? _note;
    private string? _windowsNote;
    private bool _busy;

    private GetAiReadySheet(AppHost host) { _host = host; }

    private AiProviderStore Store => AiProviderStore.For(_host.Services);

    public static void Show(AppHost host)
    {
        var sheet = new GetAiReadySheet(host);
        sheet._dialog = new FlowDialog(Strings.Get("GetAiReady_Title"), sheet._body, primaryText: null, secondaryText: Strings.Get("GetAiReady_Close"), width: 480);
        sheet.Rebuild();
        _ = sheet._dialog.ShowTopmostAsync(default, host.MainWindow);
        _ = sheet.RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try { _hostState = await Store.Host.GetStateAsync(); }
        catch (Exception) { _hostState = new PortalAiHostState(PortalAiHostPhase.NotRunning, "Portal AI Host isn't running"); }
        if (AiProviderRegistry.Get(AiProviderIds.WindowsAi) is not null)
        {
            try { _windowsState = await _windows.IsAvailableAsync(); }
            catch (Exception) { _windowsState = new AiAvailability(false, WindowsAiProvider.NotSupportedReason); }
        }
        Rebuild();
    }

    private void Rebuild()
    {
        _body.Children.Clear();
        var intro = Ui.Text(Strings.Get("GetAiReady_Intro"), secondary: true, size: 13);
        intro.Margin = new Thickness(0, 0, 0, 12);
        _body.Children.Add(intro);
        var primaryTaken = false;

        _body.Children.Add(Row(Strings.Get("GetAiReady_HostName"), HostLine(), HostButtons(ref primaryTaken)));
        if (_windowsState is { } w && (w.IsAvailable || WindowsAiProvider.CanGetReady(w)))
            _body.Children.Add(Row(Strings.Get("GetAiReady_WindowsName"), _windowsNote ?? Strings.Get("GetAiReady_WindowsAbout"), WindowsButtons(w, ref primaryTaken)));
        var key = Button(Strings.Get("GetAiReady_ApiKey"), Strings.Get("GetAiReady_ApiKeyName"), () => { _dialog?.Close(); _host.OpenAiSettings(); }, ref primaryTaken);
        _body.Children.Add(Row(Strings.Get("GetAiReady_ApiKeyName"), Strings.Get("GetAiReady_ApiKeyAbout"), new[] { key }));
    }

    private string HostLine()
    {
        if (_note is not null) return _note;
        if (_hostState is null) return Strings.Get("GetAiReady_Checking");
        return _hostState.IsReady ? Strings.Get("GetAiReady_HostReady") : PortalAiHostProvider.Describe(_hostState);
    }

    private IEnumerable<Wpf.Ui.Controls.Button> HostButtons(ref bool primaryTaken)
    {
        var list = new List<Wpf.Ui.Controls.Button>();
        if (_hostState is null || _busy) return list;
        var name = Strings.Get("GetAiReady_HostName");
        switch (_hostState.Phase)
        {
            case PortalAiHostPhase.NotInstalled:
                list.Add(Button(Strings.Get("GetAiReady_Get"), name, OpenStore, ref primaryTaken));
                break;
            case PortalAiHostPhase.NotRunning:
                list.Add(Button(Strings.Get("GetAiReady_Start"), name, () => _ = StartAsync(), ref primaryTaken));
                break;
            case PortalAiHostPhase.NeedsAccess or PortalAiHostPhase.AccessRemoved:
                list.Add(Button(Strings.Get("GetAiReady_Allow"), name, () => _ = AllowAsync(), ref primaryTaken));
                break;
            case PortalAiHostPhase.NoModel:
                list.Add(Button(Strings.Get("GetAiReady_Start"), name, () => _ = StartAsync(), ref primaryTaken));
                break;
            case PortalAiHostPhase.Ready:
                list.Add(Button(Strings.Get("GetAiReady_UseHost"), name, () => Use(AiProviderIds.PortalAiHost), ref primaryTaken));
                break;
        }
        return list;
    }

    private IEnumerable<Wpf.Ui.Controls.Button> WindowsButtons(AiAvailability state, ref bool primaryTaken)
    {
        if (_busy) return Array.Empty<Wpf.Ui.Controls.Button>();
        var name = Strings.Get("GetAiReady_WindowsName");
        return new[]
        {
            WindowsAiProvider.CanGetReady(state)
                ? Button(Strings.Get("AiSettingsPanel_GetItReady"), name, () => _ = PrepareWindowsAsync(), ref primaryTaken)
                : Button(Strings.Get("GetAiReady_UseWindows"), name, () => Use(AiProviderIds.WindowsAi), ref primaryTaken),
        };
    }

    private static Wpf.Ui.Controls.Button Button(string text, string forName, Action click, ref bool primaryTaken)
    {
        var b = new Wpf.Ui.Controls.Button { Content = text, Margin = new Thickness(8, 0, 0, 0), Appearance = primaryTaken ? ControlAppearance.Secondary : ControlAppearance.Primary };
        primaryTaken = true;
        AutomationProperties.SetName(b, text + ", " + forName);
        b.Click += (_, _) => click();
        return b;
    }

    private static UIElement Row(string title, string line, IEnumerable<Wpf.Ui.Controls.Button> buttons)
    {
        var card = new Border { Style = Application.Current.TryFindResource("SurfaceCard") as Style, Margin = new Thickness(0, 0, 0, 8) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var texts = new StackPanel();
        texts.Children.Add(Ui.Text(title, weight: FontWeights.SemiBold));
        texts.Children.Add(Ui.Text(line, secondary: true, size: 12));
        grid.Children.Add(texts);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var b in buttons) right.Children.Add(b);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        card.Child = grid;
        return card;
    }

    private static void OpenStore()
    {
        try { Process.Start(new ProcessStartInfo(PortalAiHostConstants.StoreUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    private async Task StartAsync()
    {
        _busy = true; _note = Strings.Get("GetAiReady_Starting"); Rebuild();
        try { _hostState = await Store.Host.StartAsync(); }
        catch (Exception) { _hostState = new PortalAiHostState(PortalAiHostPhase.NotRunning, "Portal AI Host isn't running"); }
        _busy = false; _note = null; Rebuild();
    }

    private async Task AllowAsync()
    {
        _busy = true; _note = Strings.Get("GetAiReady_Waiting"); Rebuild();
        try
        {
            var session = Store.Host;
            var result = await session.RequestAccessAsync(typeof(GetAiReadySheet).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
            _hostState = await session.GetStateAsync();
            _note = result.Outcome == PortalAiHostAccessOutcome.Approved ? null : result.Message;
        }
        catch (Exception ex) { _note = ex.Message; }
        _busy = false; Rebuild();
    }

    private async Task PrepareWindowsAsync()
    {
        _busy = true; _windowsNote = null; Rebuild();
        string? problem;
        try { problem = await _windows.EnsureReadyAsync(); }
        catch (Exception ex) { problem = ex.Message; }
        try { _windowsState = await _windows.IsAvailableAsync(); }
        catch (Exception) { _windowsState = new AiAvailability(false, WindowsAiProvider.NotSupportedReason); }
        _busy = false; _windowsNote = problem; Rebuild();
    }

    /// <summary>Makes the provider the default and closes the sheet.</summary>
    private void Use(string id)
    {
        Store.Update(c =>
        {
            var s = Store.Get(id);
            s.Enabled = true;
            c.Providers[id] = s;
            c.DefaultProvider = id;
            c.DefaultChosen = true;
        });
        _dialog?.Close();
    }
}
