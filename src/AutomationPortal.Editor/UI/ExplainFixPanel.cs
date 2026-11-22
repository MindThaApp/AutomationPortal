using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AutomationPortal.Core;
using Wpf.Ui.Controls;

namespace AutomationPortal.Editor.UI;

/// <summary>What the Explain / Fix it panel needs from whoever shows it. Nothing here saves or runs a shortcut.</summary>
/// <param name="Explain">Plain-language explanation of the failure.</param>
/// <param name="Fix">A corrected draft of the shortcut (never saved).</param>
/// <param name="OpenFix">Shows the draft in an editor as an unsaved copy.</param>
/// <param name="OpenAiSettings">Opens Settings › AI.</param>
/// <param name="AiReady">True when the default AI provider is set up.</param>
/// <param name="PrivacyLine">Where the question goes ("Stays on this PC" or "Sends step names and the error to ...").</param>
/// <param name="FixDisabledReason">When set, "Fix it…" is disabled and this is the tooltip.</param>
public sealed record ExplainFixSource(Func<CancellationToken, Task<AiExplanation>> Explain, Func<CancellationToken, Task<AiDraftResult>> Fix,
    Action<AiDraftResult> OpenFix, Action OpenAiSettings, Func<bool> AiReady, string PrivacyLine, string? FixDisabledReason = null);

/// <summary>"Explain" and "Fix it…" for a failed run, with the explanation shown inline (selectable and copyable) and a privacy line.</summary>
public sealed class ExplainFixPanel : StackPanel
{
    private readonly ExplainFixSource _source;
    private readonly Wpf.Ui.Controls.Button _explain;
    private readonly Wpf.Ui.Controls.Button _fix;
    private readonly ProgressRing _busy = new() { IsIndeterminate = true, Width = 18, Height = 18, Visibility = Visibility.Collapsed, Margin = new Thickness(4, 0, 8, 0) };
    private readonly TextBlock _status;
    private readonly System.Windows.Controls.TextBox _text;
    private readonly Wpf.Ui.Controls.Button _copy;
    private readonly TextBlock _problem;
    private readonly TextBlock _setup;
    private CancellationTokenSource? _cts;
    private bool _working;

    public ExplainFixPanel(ExplainFixSource source)
    {
        _source = source;
        Margin = new Thickness(0, 10, 0, 0);

        var row = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        _explain = Ui.TextButton(Strings.Get("ExplainFix_Explain"), () => _ = ExplainAsync(), SymbolRegular.Sparkle24);
        AutomationProperties.SetName(_explain, Strings.Get("ExplainFix_ExplainName"));
        _explain.Margin = new Thickness(0, 0, 8, 4);
        _fix = Ui.TextButton(Strings.Get("ExplainFix_FixIt"), () => _ = FixAsync(), SymbolRegular.Wrench24);
        AutomationProperties.SetName(_fix, Strings.Get("ExplainFix_FixItName"));
        _fix.Margin = new Thickness(0, 0, 8, 4);
        if (source.FixDisabledReason is { } why) { _fix.IsEnabled = false; _fix.ToolTip = why; ToolTipService.SetShowOnDisabled(_fix, true); }
        row.Children.Add(_explain);
        row.Children.Add(_fix);
        row.Children.Add(_busy);
        Children.Add(row);

        _status = Ui.Text("", Ui.Secondary, 12.5, wrap: true);
        _status.Visibility = Visibility.Collapsed;
        _status.SetValue(AutomationProperties.LiveSettingProperty, AutomationLiveSetting.Polite);
        Children.Add(_status);

        if (source.PrivacyLine.Length > 0)
        {
            var privacy = Ui.Text(source.PrivacyLine, Ui.Tertiary, 12, wrap: true);
            privacy.Margin = new Thickness(0, 2, 0, 0);
            Children.Add(privacy);
        }

        _text = new System.Windows.Controls.TextBox
        {
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent,
            Padding = new Thickness(0), Margin = new Thickness(0, 8, 0, 0), FontSize = 13.5, Visibility = Visibility.Collapsed,
        };
        _text.SetResourceReference(Control.ForegroundProperty, Ui.Primary);
        AutomationProperties.SetName(_text, Strings.Get("ExplainFix_TextName"));
        Children.Add(_text);

        _copy = Ui.TextButton(Strings.Get("ExplainFix_Copy"), CopyText, SymbolRegular.Copy20, ControlAppearance.Transparent);
        _copy.HorizontalAlignment = HorizontalAlignment.Left;
        _copy.Visibility = Visibility.Collapsed;
        Children.Add(_copy);

        _problem = Ui.Text("", Ui.Critical, 12.5, wrap: true);
        _problem.Margin = new Thickness(0, 8, 0, 0);
        _problem.Visibility = Visibility.Collapsed;
        Children.Add(_problem);

        _setup = new TextBlock
        {
            Text = Strings.Get("ExplainFix_SetUp"), FontSize = 12.5, Cursor = System.Windows.Input.Cursors.Hand, TextDecorations = TextDecorations.Underline,
            Focusable = true, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0),
        };
        _setup.SetResourceReference(TextBlock.ForegroundProperty, Ui.Accent);
        AutomationProperties.SetName(_setup, Strings.Get("ExplainFix_SetUpName"));
        _setup.MouseLeftButtonUp += (_, _) => _source.OpenAiSettings();
        _setup.KeyDown += (_, e) => { if (e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space) _source.OpenAiSettings(); };
        Children.Add(_setup);

        Unloaded += (_, _) => _cts?.Cancel();
    }

    private void Begin(string status)
    {
        _working = true;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _explain.IsEnabled = false;
        _fix.IsEnabled = false;
        _busy.Visibility = Visibility.Visible;
        _status.Text = status;
        _status.Visibility = Visibility.Visible;
        _problem.Visibility = Visibility.Collapsed;
        _setup.Visibility = Visibility.Collapsed;
    }

    private void End()
    {
        _working = false;
        _explain.IsEnabled = true;
        _fix.IsEnabled = _source.FixDisabledReason is null;
        _busy.Visibility = Visibility.Collapsed;
    }

    private void ShowProblem(string text, bool setup)
    {
        _status.Visibility = Visibility.Collapsed;
        _problem.Text = text;
        _problem.Visibility = Visibility.Visible;
        _setup.Visibility = setup ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task ExplainAsync()
    {
        if (_working) return;
        Begin(Strings.Get("ExplainFix_Thinking"));
        var token = _cts!.Token;
        try
        {
            var r = await _source.Explain(token);
            if (token.IsCancellationRequested) return;
            if (r.Text is { Length: > 0 } text)
            {
                _status.Visibility = Visibility.Collapsed;
                _text.Text = text;
                _text.Visibility = Visibility.Visible;
                _copy.Visibility = Visibility.Visible;
            }
            else ShowProblem(r.Error ?? Strings.Get("ExplainFix_NoAnswer"), r.NeedsSetup);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowProblem(Strings.Get("ExplainFix_CouldntReach") + " " + ex.Message, false); }
        finally { End(); }
    }

    private async Task FixAsync()
    {
        if (_working) return;
        Begin(Strings.Get("ExplainFix_WritingFix"));
        var token = _cts!.Token;
        try
        {
            var r = await _source.Fix(token);
            if (token.IsCancellationRequested) return;
            if (r.Success)
            {
                _source.OpenFix(r);
                _status.Text = Strings.Get("ExplainFix_Opened");
            }
            else ShowProblem(r.Error ?? Strings.Get("ExplainFix_NoFix"), !_source.AiReady());
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowProblem(Strings.Get("ExplainFix_CouldntReach") + " " + ex.Message, false); }
        finally { End(); }
    }

    private void CopyText()
    {
        try { Clipboard.SetText(_text.Text); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException) { }
    }
}
