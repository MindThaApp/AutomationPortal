using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutomationPortal.Core;
using AutomationPortal.Editor.Logic;

namespace AutomationPortal.Editor.UI;

/// <summary>A parameter shown inline in an action's sentence: accent-colored text, or a colored variable pill. Click to edit.</summary>
internal sealed class TokenView : Border
{
    private readonly EditorEnv _env;
    private readonly StackPanel _content = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

    public TokenView(ParameterDescriptor parameter, EditorEnv env)
    {
        Param = parameter;
        _env = env;
        CornerRadius = Radius.Control;
        Padding = new Thickness(4, 1, 4, 1);
        Margin = new Thickness(1, 1, 1, 1);
        Cursor = Cursors.Hand;
        Background = Brushes.Transparent;
        Focusable = true;
        Child = _content;
        VerticalAlignment = VerticalAlignment.Center;
        MouseEnter += (_, _) => this.Dyn(BackgroundProperty, Ui.SubtleHover);
        MouseLeave += (_, _) => Background = Brushes.Transparent;
        MouseLeftButtonUp += (_, e) => { e.Handled = true; Activated?.Invoke(this); };
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space) { e.Handled = true; Activated?.Invoke(this); }
        };
        GotKeyboardFocus += (_, _) => { this.Dyn(BorderBrushProperty, "DsTextPrimaryBrush"); BorderThickness = new Thickness(2); };
        LostKeyboardFocus += (_, _) => BorderThickness = new Thickness(0);
        FocusVisualStyle = null;
    }

    public ParameterDescriptor Param { get; }
    public event Action<TokenView>? Activated;
    /// <summary>The value before this one is 1, so a unit shown here ("minutes") reads singular ("minute").</summary>
    public bool Singular { get; set; }

    public void SetValue(ParamValue? value, bool missing)
    {
        _content.Children.Clear();
        var parts = ValueFormat.Parts(Param, value, _env);
        var automation = new System.Text.StringBuilder(Param.Label).Append(": ");
        foreach (var raw in parts)
        {
            var part = Singular && raw.Kind == DisplayPartKind.Text ? raw with { Text = PluralAgreement.Singular(raw.Text) } : raw;
            automation.Append(part.Text);
            switch (part.Kind)
            {
                case DisplayPartKind.Variable:
                    _content.Children.Add(Pills.Create(_env.Describe(part.Var!)));
                    break;
                case DisplayPartKind.Ask:
                    _content.Children.Add(Pills.Ask());
                    break;
                case DisplayPartKind.Color:
                    _content.Children.Add(Pills.ColorSwatch(part.Text));
                    _content.Children.Add(Accent(part.Text, false, false));
                    break;
                case DisplayPartKind.Placeholder:
                    _content.Children.Add(Accent(part.Text, true, missing));
                    break;
                default:
                    _content.Children.Add(Accent(part.Text, false, false));
                    break;
            }
        }
        ToolTip = missing ? Strings.Format("TokenView_0IsNeededClick", Param.Label) : Strings.Format("TokenView_0ClickToChange", Param.Label);
        System.Windows.Automation.AutomationProperties.SetName(this, automation.ToString());
    }

    private static TextBlock Accent(string text, bool placeholder, bool missing)
    {
        var t = new TextBlock { Text = text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 360 };
        t.SetResourceReference(TextBlock.ForegroundProperty, missing ? Ui.Critical : Ui.Accent);
        if (placeholder) { t.FontStyle = FontStyles.Italic; t.Opacity = missing ? 1 : 0.75; }
        else t.FontWeight = FontWeights.Medium;
        return t;
    }
}
