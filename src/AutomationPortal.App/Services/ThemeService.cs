using System.Windows;
using Flowmate.Core;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Flowmate.App.Services;

/// <summary>Applies Light / Dark / System live, to every open window and to windows opened later.</summary>
public sealed class ThemeService
{
    private ThemeSetting _setting = ThemeSetting.System;

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
                Application.Current?.Dispatcher.BeginInvoke(() => Apply(_setting));
        };
    }

    public void Apply(ThemeSetting setting)
    {
        _setting = setting;
        var theme = Effective;
        ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, updateAccent: true);
        foreach (Window w in Application.Current.Windows)
            Refresh(w, theme);
    }

    /// <summary>Call for a window that was just created so its title bar and backdrop match the current theme.</summary>
    public void Refresh(Window window, ApplicationTheme? theme = null)
    {
        theme ??= Effective;
        try
        {
            if (window is FluentWindow fw)
                WindowBackgroundManager.UpdateBackground(fw, theme.Value, fw.WindowBackdropType);
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
