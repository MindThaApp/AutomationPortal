using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutomationPortal.Editor.UI;
using Wpf.Ui.Controls;

namespace AutomationPortal.Editor;

/// <summary>Turns key presses into text like "Ctrl+Alt+K".</summary>
public static class HotkeyText
{
    public static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.None;

    public static string Modifiers(ModifierKeys mods)
    {
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        return string.Join("+", parts);
    }

    public static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + ((int)key - (int)Key.NumPad0),
        Key.Back => "Backspace",
        Key.Return => "Enter",
        Key.Prior => "PageUp",
        Key.Next => "PageDown",
        Key.OemPlus => "=",
        Key.OemMinus => "-",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.Oem1 => ";",
        Key.Oem2 => "/",
        Key.Oem3 => "`",
        Key.Oem4 => "[",
        Key.Oem5 => "\\",
        Key.Oem6 => "]",
        Key.Oem7 => "'",
        _ => key.ToString(),
    };

    public static string Format(ModifierKeys mods, Key key)
    {
        var m = Modifiers(mods);
        var k = KeyName(key);
        return m.Length == 0 ? k : m + "+" + k;
    }

    /// <summary>A shortcut needs a modifier, unless it is a function key.</summary>
    public static bool IsAcceptable(ModifierKeys mods, Key key) =>
        mods != ModifierKeys.None || key is >= Key.F1 and <= Key.F24;
}

/// <summary>Click, then press the keys you want. Backspace clears, Esc cancels.</summary>
public sealed class HotkeyRecorder : Border
{
    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 14 };
    private readonly Wpf.Ui.Controls.Button _clear;
    private string? _value;

    public HotkeyRecorder()
    {
        EditorResources.Ensure();
        Focusable = true;
        MinHeight = 32;
        MinWidth = 180;
        CornerRadius = Radius.Control;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(10, 4, 4, 4);
        Cursor = Cursors.IBeam;
        FocusVisualStyle = null;
        this.Dyn(BackgroundProperty, Ui.ControlFill).Dyn(BorderBrushProperty, Ui.ControlStroke);
        System.Windows.Automation.AutomationProperties.SetName(this, Strings.Get("HotkeyRecorder_Hotkey"));
        _clear = Ui.IconButton(SymbolRegular.Dismiss16, Strings.Get("HotkeyRecorder_ClearHotkey"), () => Set(null, true), 24, 12);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(_text);
        Grid.SetColumn(_clear, 1);
        grid.Children.Add(_clear);
        Child = grid;
        Render(false);
        MouseLeftButtonDown += (_, _) => Focus();
        GotKeyboardFocus += (_, _) => Render(true);
        LostKeyboardFocus += (_, _) => Render(false);
        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += (_, _) => { if (IsKeyboardFocused) Render(true); };
    }

    public string? Value { get => _value; set { _value = string.IsNullOrWhiteSpace(value) ? null : value; Render(IsKeyboardFocused); } }

    public event Action<string?>? ValueChanged;

    private void Set(string? v, bool raise)
    {
        _value = v;
        Render(IsKeyboardFocused);
        if (raise) ValueChanged?.Invoke(v);
    }

    private void Render(bool focused, string? hint = null)
    {
        _clear.Visibility = _value is null ? Visibility.Collapsed : Visibility.Visible;
        if (focused)
        {
            var live = HotkeyText.Modifiers(Keyboard.Modifiers);
            _text.Text = hint ?? (live.Length > 0 ? live + "+…" : Strings.Get("HotkeyRecorder_PressTheKeysYou"));
            _text.SetResourceReference(TextBlock.ForegroundProperty, Ui.Accent);
            SetResourceReference(BorderBrushProperty, "DsAccentBrush");
        }
        else
        {
            _text.Text = _value ?? Strings.Get("HotkeyRecorder_None");
            _text.SetResourceReference(TextBlock.ForegroundProperty, _value is null ? Ui.Tertiary : Ui.Primary);
            SetResourceReference(BorderBrushProperty, Ui.ControlStroke);
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && Ui.FindAncestor<Wpf.Ui.Controls.Button>(d) is not null) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        if (HotkeyText.IsModifier(key)) { Render(true); return; }
        if (mods == ModifierKeys.None && key == Key.Escape) { Keyboard.ClearFocus(); Render(false); return; }
        if (mods == ModifierKeys.None && key is Key.Back or Key.Delete) { Set(null, true); return; }
        if (!HotkeyText.IsAcceptable(mods, key)) { Render(true, Strings.Get("HotkeyRecorder_IncludeCtrlAltShift")); return; }
        Set(HotkeyText.Format(mods, key), true);
    }
}
