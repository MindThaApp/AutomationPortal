using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutomationPortal.App.Infrastructure;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Dialogs;

/// <summary>
/// Small Fluent dialog window built in code: title bar, body, and a button row. Used by every host dialog.
/// Topmost and centred on the monitor under the pointer, so it works while the main window is hidden.
/// </summary>
public class FlowDialog : FluentWindow
{
    private readonly TaskCompletionSource<bool> _closed = new();
    private readonly Wpf.Ui.Controls.Button _primary;
    private readonly Wpf.Ui.Controls.Button? _secondary;
    private bool _result;

    public FlowDialog(string title, FrameworkElement body, string? primaryText = "OK", string? secondaryText = "Cancel",
                      double width = 460, bool danger = false)
    {
        Title = title;
        Width = width;
        MinWidth = 320;
        MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height * 0.9);
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowBackdropType = WindowBackdropType.Mica;
        ExtendsContentIntoTitleBar = true;
        UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
        SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("FlowFont");

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var bar = new TitleBar { Title = title, ShowMinimize = false, ShowMaximize = false, CanMaximize = false };
        Grid.SetRow(bar, 0);
        root.Children.Add(bar);

        body.Margin = new Thickness(24, 8, 24, 16);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 0, 24, 20) };
        if (primaryText is not null)
        {
            _primary = new Wpf.Ui.Controls.Button
            {
                Content = primaryText, MinWidth = 96, IsDefault = true,
                Appearance = danger ? ControlAppearance.Danger : ControlAppearance.Primary,
            };
            _primary.Click += (_, _) => TryAccept();
            buttons.Children.Add(_primary);
        }
        else _primary = new Wpf.Ui.Controls.Button();
        if (secondaryText is not null)
        {
            _secondary = new Wpf.Ui.Controls.Button { Content = secondaryText, MinWidth = 96, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
            _secondary.Click += (_, _) => { _result = false; Close(); };
            buttons.Children.Add(_secondary);
        }
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        _buttons = buttons;

        Content = root;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _secondary is null) { _result = false; Close(); e.Handled = true; }
        };
        Closed += (_, _) => _closed.TrySetResult(_result);
    }

    private readonly StackPanel _buttons;

    /// <summary>Adds an extra button to the left of the main ones that doesn't close the dialog.</summary>
    public Wpf.Ui.Controls.Button AddLeftButton(string text, Action onClick)
    {
        var b = new Wpf.Ui.Controls.Button { Content = text, MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
        b.Click += (_, _) => onClick();
        _buttons.Children.Insert(0, b);
        return b;
    }

    /// <summary>Return false to keep the dialog open (after showing your own message).</summary>
    public Func<bool>? Validate { get; set; }

    /// <summary>Raised when the user accepted and validation passed, before the window closes.</summary>
    public event EventHandler? Accepted;

    public void SetPrimaryEnabled(bool enabled) => _primary.IsEnabled = enabled;

    public void SetPrimaryText(string text) => _primary.Content = text;

    public void SetSecondaryText(string text) { if (_secondary is not null) _secondary.Content = text; }

    /// <summary>False so that Enter doesn't press the main button (for prompts that can appear while the user is typing elsewhere).</summary>
    public void SetPrimaryDefault(bool isDefault) => _primary.IsDefault = isDefault;

    public void Accept()
    {
        _result = true;
        Accepted?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void TryAccept()
    {
        if (Validate is not null && !Validate()) return;
        Accept();
    }

    /// <summary>Shows the dialog without blocking and completes with true when accepted.</summary>
    public Task<bool> ShowTopmostAsync(CancellationToken ct = default, Window? centerOn = null)
    {
        if (ct.IsCancellationRequested) return Task.FromCanceled<bool>(ct);
        CancellationTokenRegistration reg = default;
        if (ct.CanBeCanceled)
            reg = ct.Register(() => Dispatcher.BeginInvoke(() => { _result = false; try { Close(); } catch { } }));
        _closed.Task.ContinueWith(_ => reg.Dispose(), TaskScheduler.Default);
        if (centerOn is { IsVisible: true } owner)
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Show();
            WindowPlacement.BringToFront(this);
        }
        else WindowPlacement.ShowCentered(this);
        return _closed.Task.ContinueWith(t =>
        {
            ct.ThrowIfCancellationRequested();
            return t.Result;
        }, TaskScheduler.Default);
    }

    /// <summary>Focus a control once the dialog is on screen.</summary>
    public void FocusWhenShown(UIElement element)
    {
        ContentRendered += (_, _) => { element.Focus(); Keyboard.Focus(element); };
    }
}
