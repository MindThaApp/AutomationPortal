using System.Windows;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace AutomationPortal.Editor;

/// <summary>Follows the Windows high contrast setting: custom-coloured parts use system colours so text stays readable.</summary>
public static class HighContrast
{
    public static bool Active => SystemParameters.HighContrast;

    /// <summary>Raised on the UI thread after the Windows high contrast setting changes.</summary>
    public static event Action? Changed;

    private static Application? _app;
    private static readonly ConditionalWeakTable<FluentWindow, object> Original = new();

    /// <summary>The backdrop to use now: the one asked for, or none (a solid window) in high contrast.</summary>
    public static WindowBackdropType Backdrop(WindowBackdropType wanted) => Active ? WindowBackdropType.None : wanted;

    /// <summary>Translucent materials (Mica, acrylic) go solid in high contrast and come back when it is switched off.</summary>
    public static void ApplyBackdrop(FluentWindow window)
    {
        if (Active)
        {
            if (window.WindowBackdropType == WindowBackdropType.None) return;
            Original.AddOrUpdate(window, window.WindowBackdropType);
            window.WindowBackdropType = WindowBackdropType.None;
        }
        else if (Original.TryGetValue(window, out var was))
        {
            Original.Remove(window);
            window.WindowBackdropType = (WindowBackdropType)was;
        }
    }

    /// <summary>Starts following the setting and keeps the app-wide chip resources in step with it.</summary>
    public static void Attach(Application app)
    {
        if (_app is not null) return;
        _app = app;
        EventManager.RegisterClassHandler(typeof(FluentWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) => { if (Active && s is FluentWindow w) ApplyBackdrop(w); }));
        Apply();
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(SystemParameters.HighContrast)) return;
            app.Dispatcher.BeginInvoke(() => { Apply(); Changed?.Invoke(); });
        };
    }

    /// <summary>Fill behind a coloured chip: the category colour normally, the selection colour in high contrast.</summary>
    public static Brush ChipFill(Brush normal) => Active ? SystemColors.HighlightBrush : normal;

    /// <summary>Icon colour on a chip.</summary>
    public static Brush OnChip => Active ? SystemColors.HighlightTextBrush : Brushes.White;

    /// <summary>A drop shadow for a pop-up or card, or nothing in high contrast (shadows are off there).</summary>
    public static System.Windows.Media.Effects.Effect? Shadow(double blur, double depth, double opacity) =>
        Active ? null : new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = blur, ShadowDepth = depth, Opacity = opacity, Color = Colors.Black };

    private static void Apply()
    {
        if (_app is null) return;
        DesignTokens.Apply(_app);
        foreach (var w in _app.Windows) if (w is FluentWindow fw) ApplyBackdrop(fw);
        var r = _app.Resources;
        if (Active)
        {
            r["FlowEmptyGradient"] = SystemColors.HighlightBrush;
            r["FlowChipForeground"] = SystemColors.HighlightTextBrush;
            r["FlowSuccessBrush"] = SystemColors.HighlightBrush;
            r["FlowFailBrush"] = SystemColors.HighlightBrush;
            r["FlowHoverBorderBrush"] = SystemColors.HighlightBrush;
            r["FlowPressBorderBrush"] = SystemColors.WindowTextBrush;
        }
        else
        {
            r.Remove("FlowEmptyGradient"); r.Remove("FlowChipForeground"); r.Remove("FlowSuccessBrush"); r.Remove("FlowFailBrush"); r.Remove("FlowHoverBorderBrush"); r.Remove("FlowPressBorderBrush");
        }
    }
}
