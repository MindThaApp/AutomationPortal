using System.Windows;
using System.Windows.Media;

namespace AutomationPortal.Editor;

/// <summary>Follows the Windows high contrast setting: custom-coloured parts use system colours so text stays readable.</summary>
public static class HighContrast
{
    public static bool Active => SystemParameters.HighContrast;

    /// <summary>Raised on the UI thread after the Windows high contrast setting changes.</summary>
    public static event Action? Changed;

    private static Application? _app;

    /// <summary>Starts following the setting and keeps the app-wide chip resources in step with it.</summary>
    public static void Attach(Application app)
    {
        if (_app is not null) return;
        _app = app;
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

    private static void Apply()
    {
        if (_app is null) return;
        var r = _app.Resources;
        if (Active)
        {
            r["FlowEmptyGradient"] = SystemColors.HighlightBrush;
            r["FlowChipForeground"] = SystemColors.HighlightTextBrush;
            r["FlowSuccessBrush"] = SystemColors.HighlightBrush;
            r["FlowFailBrush"] = SystemColors.HighlightBrush;
        }
        else
        {
            r.Remove("FlowEmptyGradient"); r.Remove("FlowChipForeground"); r.Remove("FlowSuccessBrush"); r.Remove("FlowFailBrush");
        }
    }
}
