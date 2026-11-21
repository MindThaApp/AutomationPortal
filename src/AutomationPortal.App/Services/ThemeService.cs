using System.Windows;
using System.Windows.Media;
using AutomationPortal.Core;
using AutomationPortal.Editor;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace AutomationPortal.App.Services;

/// <summary>Applies the Appearance settings (theme, accent, material, corners, density, text size, motion, tile look) live, to every open window and to windows opened later.</summary>
public sealed class ThemeService
{
    private ThemeSetting _setting = ThemeSetting.System;
    private MaterialSetting _material = MaterialSetting.Mica;
    private AppSettings? _last;
    private (ThemeSetting, AccentMode, string, MaterialSetting, CornerStyle, Density, int, MotionSetting, TileLook)? _applied;

    public ThemeSetting Setting => _setting;

    public ApplicationTheme Effective => _setting switch
    {
        ThemeSetting.Light => ApplicationTheme.Light,
        ThemeSetting.Dark => ApplicationTheme.Dark,
        _ => SystemUsesLightTheme() ? ApplicationTheme.Light : ApplicationTheme.Dark,
    };

    public ThemeService()
    {
        // Follow Windows when the user flips the system theme and we're on "System".
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color && _setting == ThemeSetting.System)
                Application.Current?.Dispatcher.BeginInvoke(() => { if (_last is not null) Apply(_last, force: true); });
        };
        EventManager.RegisterClassHandler(typeof(FluentWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => { if (_last is not null && s is FluentWindow w) Refresh(w); }));
        HighContrast.Changed += () => { if (_last is not null) Apply(_last, force: true); };
    }

    public static WindowBackdropType BackdropFor(MaterialSetting material) => material switch
    {
        MaterialSetting.Acrylic => WindowBackdropType.Acrylic,
        MaterialSetting.Solid => WindowBackdropType.None,
        _ => WindowBackdropType.Mica,
    };

    /// <summary>The colour of a preset accent: a category colour name or an old palette key (yellow, purple, pink ...). Null for anything else, which means the Windows accent.</summary>
    public static Color? PresetColor(string? name) =>
        Palette.CategoryNameOrNull(name) is { } category && Application.Current?.TryFindResource("DsCategory" + category + "Color") is Color c ? c : null;

    /// <summary>Applies the Appearance settings. Does nothing when none of them changed since the last call, unless <paramref name="force"/> is set.</summary>
    public void Apply(AppSettings settings, bool force = false)
    {
        _last = settings;
        var key = (settings.Theme, settings.AccentMode, settings.AccentPreset, settings.Material, settings.CornerStyle, settings.Density, settings.TextScale, settings.MotionMode, settings.TileLook);
        if (!force && _applied == key) return;
        _applied = key;
        _setting = settings.Theme;
        _material = settings.Material;
        var app = Application.Current;
        AppearanceTokens.Apply(app, settings);

        var theme = Effective;
        var preset = settings.AccentMode == AccentMode.Preset ? PresetColor(settings.AccentPreset) : null;
        ApplicationThemeManager.Apply(theme, HighContrast.Backdrop(BackdropFor(_material)), updateAccent: preset is null);
        if (preset is { } color) ApplicationAccentColorManager.Apply(color, theme);
        DesignTokens.AccentOverride = preset;
        DesignTokens.Apply(app);
        foreach (Window w in app.Windows)
            Refresh(w, theme);
    }

    /// <summary>Call for a window that was just created so its title bar and backdrop match the current theme and material.</summary>
    public void Refresh(Window window, ApplicationTheme? theme = null)
    {
        theme ??= Effective;
        try
        {
            if (window is FluentWindow fw)
            {
                if (!HighContrast.Active && fw is not Dialogs.CommandPaletteWindow && fw.WindowBackdropType != BackdropFor(_material))
                    fw.WindowBackdropType = BackdropFor(_material);
                WindowBackgroundManager.UpdateBackground(fw, theme.Value, HighContrast.Backdrop(fw.WindowBackdropType));
            }
        }
        catch (Exception) { /* window not shown yet; the backdrop is applied when it is */ }
    }

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int v || v != 0;
        }
        catch { return true; }
    }
}
