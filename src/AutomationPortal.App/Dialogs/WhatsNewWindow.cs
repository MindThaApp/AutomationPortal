using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AutomationPortal.App.Infrastructure;
using AutomationPortal.App.Logic;
using AutomationPortal.App.Pages;
using AutomationPortal.Core;
using AutomationPortal.Editor;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Dialogs;

/// <summary>
/// The What's new window: a thank-you line, the fixes of this version, then a card per new feature. It is its own window, so the app stays usable,
/// and it sizes to its content up to the work area (the middle scrolls only when the screen is too small). One button, "Got it"; Esc closes.
/// </summary>
public sealed class WhatsNewWindow : FluentWindow
{
    private const double Inset = 32;
    private readonly AppHost _host;

    public WhatsNewWindow(AppHost host, WhatsNewContent content)
    {
        _host = host;
        Title = Strings.Get("WhatsNew_Title");
        var work = SystemParameters.WorkArea;
        var grow = Math.Max(1, host.Settings.Current.TextScale / 100.0);
        Width = Math.Min(640 * grow, work.Width * 0.95);
        MinWidth = Math.Min(420, Width);
        MaxHeight = work.Height * 0.95;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        WindowBackdropType = WindowBackdropType.None;
        ExtendsContentIntoTitleBar = true;
        UseLayoutRounding = true;
        FontSize = Fs(TypeRamp.Body);
        SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
        SetResourceReference(ForegroundProperty, "DsTextPrimaryBrush");
        FontFamily = (FontFamily)Application.Current.FindResource("FlowFont");
        AutomationProperties.SetAutomationId(this, "WhatsNewWindow");

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var bar = new TitleBar { Title = Title, ShowMinimize = false, ShowMaximize = false, CanMaximize = false };
        bar.Icon = new ImageIcon { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/automationportal.png")) };
        root.Children.Add(bar);

        var dark = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme() == Wpf.Ui.Appearance.ApplicationTheme.Dark;
        var body = new StackPanel { Margin = new Thickness(Inset, 8, Inset, 16) };
        if (content.Thanks.Length > 0)
            body.Children.Add(Pad(WelcomeWindow.SecondaryText(content.Thanks, Fs(TypeRamp.Body), dark), 0, 0, 0, 16));

        body.Children.Add(Pad(Heading(Strings.Get("WhatsNew_Fixed")), 0, 0, 0, 6));
        foreach (var fix in content.Fixes)
            body.Children.Add(Pad(Bullet(fix), 0, 0, 0, 4));

        if (content.Cards.Count > 0)
        {
            body.Children.Add(Pad(Heading(Strings.Get("WhatsNew_Highlights")), 0, 12, 0, 8));
            foreach (var card in content.Cards) body.Children.Add(BuildCard(card, dark));
        }

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var divider = new Border { Height = 1 };
        divider.SetResourceReference(Border.BackgroundProperty, "DsStrokeBrush");
        Grid.SetRow(divider, 2);
        root.Children.Add(divider);

        var gotIt = new Wpf.Ui.Controls.Button
        {
            Content = Strings.Get("WhatsNew_GotIt"),
            Appearance = ControlAppearance.Primary,
            IsDefault = true,
            MinWidth = 110,
            FontSize = Fs(TypeRamp.Body),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(Inset, 14, Inset, 20),
            ToolTip = Strings.Get("WhatsNew_GotItTip"),
        };
        AutomationProperties.SetHelpText(gotIt, Strings.Get("WhatsNew_GotItTip"));
        AutomationProperties.SetAutomationId(gotIt, "WhatsNewGotIt");
        gotIt.Click += (_, _) => Close();
        Grid.SetRow(gotIt, 3);
        root.Children.Add(gotIt);
        Content = root;

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        DialogMotion.Attach(this);
    }

    /// <summary>Opens the window (centred on the main window when it is showing) and returns at once.</summary>
    public void ShowFor(Window? owner)
    {
        if (owner is { IsVisible: true })
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Show();
            WindowPlacement.BringToFront(this);
        }
        else WindowPlacement.ShowCentered(this);
    }

    private double Fs(double size) => AppearanceMath.FontSize(size, _host.Settings.Current.TextScale);

    private static FrameworkElement Pad(FrameworkElement e, double l, double t, double r, double b) { e.Margin = new Thickness(l, t, r, b); return e; }

    private System.Windows.Controls.TextBlock Heading(string text) => Ui.Text(text, false, Fs(TypeRamp.PanelTitle), FontWeights.SemiBold);

    private FrameworkElement Bullet(string text)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(Ui.Text("•", false, Fs(TypeRamp.Body)));
        var t = Ui.Text(text, false, Fs(TypeRamp.Body));
        Grid.SetColumn(t, 1);
        row.Children.Add(t);
        return row;
    }

    private FrameworkElement BuildCard(WhatsNewCard card, bool dark)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var imageArea = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Top };
        imageArea.SetResourceReference(Border.BackgroundProperty, "FlowEmptyGradient");
        var source = LoadImage(card.Image);
        if (source is not null) imageArea.Child = new System.Windows.Controls.Image { Source = source, Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        else imageArea.Child = new SymbolIcon { Symbol = SymbolRegular.Flash24, FontSize = 32, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(imageArea);

        var text = new StackPanel();
        text.Children.Add(Ui.Text(card.Title, false, Fs(TypeRamp.PanelTitle), FontWeights.SemiBold));
        if (card.Body.Length > 0) text.Children.Add(Pad(WelcomeWindow.SecondaryText(card.Body, Fs(TypeRamp.Body), dark), 0, 4, 0, 0));
        if (card.ShowMe is { } target && NavFor(target) is { } nav)
        {
            var link = new Wpf.Ui.Controls.Button
            {
                Content = Strings.Get("WhatsNew_ShowMe"),
                Appearance = ControlAppearance.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0, 6, 0, 0),
                FontSize = Fs(TypeRamp.Body),
                ToolTip = Strings.Get("WhatsNew_ShowMeTip"),
            };
            link.SetResourceReference(Control.ForegroundProperty, "DsAccentTextBrush");
            AutomationProperties.SetHelpText(link, Strings.Get("WhatsNew_ShowMeTip"));
            link.Click += (_, _) => _host.ShowMainWindow(nav);
            text.Children.Add(link);
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var frame = new Border { Padding = new Thickness(16), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 10), Child = grid };
        frame.SetResourceReference(Border.BorderBrushProperty, "DsStrokeBrush");
        return frame;
    }

    private static ImageSource? LoadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return new BitmapImage(new Uri("pack://application:,,,/" + path.TrimStart('/'))); }
        catch (Exception ex) when (ex is UriFormatException or IOException or NotSupportedException) { return null; }
    }

    /// <summary>The page a card's "showMe" value names. Unknown names give no link.</summary>
    public static NavTarget? NavFor(string showMe) => showMe.Trim().ToLowerInvariant() switch
    {
        "library" => new NavTarget(AppPage.Library),
        "pinned" => new NavTarget(AppPage.Pinned),
        "automations" => new NavTarget(AppPage.Automations),
        "snippets" => new NavTarget(AppPage.Snippets),
        "gallery" => new NavTarget(AppPage.Gallery),
        "history" => new NavTarget(AppPage.History),
        "settings" => new NavTarget(AppPage.Settings),
        "ai" => new NavTarget(AppPage.Settings, Item: "ai"),
        _ => null,
    };
}
