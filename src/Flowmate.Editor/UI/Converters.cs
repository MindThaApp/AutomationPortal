using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Flowmate.Core;
using Flowmate.Editor.ViewModels;

namespace Flowmate.Editor.UI;

internal sealed class IconNameConverter : IValueConverter
{
    public static readonly IconNameConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => IconNames.Resolve(value as string);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

internal sealed class PaletteBrushConverter : IValueConverter
{
    public static readonly PaletteBrushConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Palette.Solid(value as string);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

internal sealed class HelpConverter : IValueConverter
{
    public static readonly HelpConverter Instance = new();
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ActionDescriptor d ? HelpCard.Build(d) : null;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

internal sealed class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) ^ (parameter is "invert") ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

internal sealed class PanelStyleSelector : StyleSelector
{
    public override Style? SelectStyle(object item, DependencyObject container)
    {
        var key = item is PanelHeader ? "FmHeaderItemStyle" : "FmPlainItemStyle";
        return Application.Current?.TryFindResource(key) as Style;
    }
}
