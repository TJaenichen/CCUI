using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CCUI.App.Converters;

/// <summary>Visible when the value is truthy: true, a non-zero number, a non-empty string or any other non-null object.</summary>
public sealed class VisibleWhenConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var truthy = value switch
        {
            null => false,
            bool b => b,
            int i => i != 0,
            double d => d != 0,
            string s => s.Length > 0,
            _ => true,
        };

        return truthy ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
