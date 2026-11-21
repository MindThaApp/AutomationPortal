namespace AutomationPortal.Editor;

/// <summary>The type ramp in pixels, for views built in code. XAML uses the DsFontSize* resources, which hold the same values.</summary>
public static class TypeRamp
{
    public const double Caption = 12;
    /// <summary>Floor for sentences and descriptions a user has to read.</summary>
    public const double Readable = 13;
    public const double Body = 14;
    public const double PanelTitle = 16;
    public const double Subtitle = 20;
    public const double Title = 28;
}
