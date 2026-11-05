using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using CCUI.App.Hosting;

namespace CCUI.App.Converters;

/// <summary>A colour as typed ("#D97757", "Red") to a brush for a swatch; unparseable text gives a transparent one.</summary>
public sealed class ColorTextToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var brush = new SolidColorBrush(ThemeResources.ParseColor(value as string, Colors.Transparent));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
