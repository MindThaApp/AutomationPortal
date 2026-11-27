using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutomationPortal.App.Logic;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Controls;

/// <summary>
/// Click it, then press the keys you want ("Ctrl+Alt+K"). Backspace clears, Esc leaves without changing.
/// Value is the stored text form used everywhere in Automation Portal.
/// </summary>
public sealed class HotkeyRecorderBox : Border
{
    private readonly System.Windows.Controls.TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly SymbolIcon _icon = new(SymbolRegular.Keyboard24) { FontSize = 18, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    private string _value = "";
    private string? _hint;

    public static readonly DependencyProperty RequireModifierProperty =
        DependencyProperty.Register(nameof(RequireModifier), typeof(bool), typeof(HotkeyRecorderBox), new PropertyMetadata(true));

    public bool RequireModifier { get => (bool)GetValue(RequireModifierProperty); set => SetValue(RequireModifierProperty, value); }

    public event EventHandler? ValueChanged;

    public HotkeyRecorderBox()
    {
        Focusable = true;
        KeyboardNavigation.SetIsTabStop(this, true);
        MinHeight = 34;
        MinWidth = 180;
        CornerRadius = AutomationPortal.Editor.Radius.Control;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(10, 6, 10, 6);
        Cursor = Cursors.Hand;
        FocusVisualStyle = null;
        ToolTip = Strings.Get("HotkeyRecorderBox_ClickThenPressThe");
        AutomationProperties.SetName(this, Strings.Get("HotkeyRecorderBox_Hotkey"));
        SetResourceReference(BackgroundProperty, "ControlFillColorDefaultBrush");
        SetResourceReference(BorderBrushProperty, "DsStrokeBrush");
        _icon.SetResourceReference(Control.ForegroundProperty, "DsTextSecondaryBrush");
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_icon);
        row.Children.Add(_text);
        Child = row;
        GotKeyboardFocus += (_, _) => { _hint = null; Refresh(); SetResourceReference(BorderBrushProperty, "DsAccentBrush"); BorderThickness = new Thickness(1, 1, 1, 2); };
        LostKeyboardFocus += (_, _) => { _hint = null; Refresh(); SetResourceReference(BorderBrushProperty, "DsStrokeBrush"); BorderThickness = new Thickness(1); };
        MouseLeftButtonDown += (_, e) => { Focus(); e.Handled = true; };
        Refresh();
    }

    public string Value
    {
        get => _value;
        set
        {
            value ??= "";
            if (value == _value) return;
            _value = value;
            Refresh();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Refresh()
    {
        if (IsKeyboardFocused)
        {
            _text.Text = _hint ?? (_value.Length == 0 ? Strings.Get("HotkeyRecorderBox_PressTheKeys") : _value + Strings.Get("HotkeyRecorderBox_PressNewKeysTo"));
            _text.SetResourceReference(TextBlock.ForegroundProperty, "DsTextPrimaryBrush");
        }
        else
        {
            _text.Text = _value.Length == 0 ? Strings.Get("HotkeyRecorderBox_ClickToSet") : _value;
            _text.SetResourceReference(TextBlock.ForegroundProperty, _value.Length == 0 ? "DsTextTertiaryBrush" : "DsTextPrimaryBrush");
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (!IsKeyboardFocused) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        if (key == Key.Tab && (mods == ModifierKeys.None || mods == ModifierKeys.Shift)) return;   // keep keyboard navigation working
        e.Handled = true;

        if (HotkeyFormat.IsModifierKey(key))
        {
            _hint = mods == ModifierKeys.None ? null : HotkeyFormat.ModifierText(mods) + "…";
            Refresh();
            return;
        }
        if (mods == ModifierKeys.None)
        {
            if (key == Key.Escape) { Keyboard.ClearFocus(); FocusManager.SetFocusedElement(FocusManager.GetFocusScope(this), null); return; }
            if (key is Key.Back or Key.Delete) { Value = ""; return; }
        }
        var text = HotkeyFormat.Format(mods, key);
        if (text is null) return;
        var isFunctionKey = key >= Key.F1 && key <= Key.F24;
        if (RequireModifier && mods == ModifierKeys.None && !isFunctionKey)
        {
            _hint = Strings.Get("HotkeyRecorderBox_IncludeCtrlAltOr");
            Refresh();
            return;
        }
        Value = text;
        _hint = null;
        Refresh();
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (!IsKeyboardFocused) return;
        var mods = Keyboard.Modifiers;
        _hint = mods == ModifierKeys.None ? null : HotkeyFormat.ModifierText(mods) + "…";
        Refresh();
    }
}
