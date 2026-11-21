using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using AReport.Model;

namespace AReport.Designer.Views;

/// <summary>Turns "#RRGGBB" into a brush (for the colour swatch in the properties panel). Invalid text gives no fill.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string text && RgbColor.TryParse(text, out var color))
        {
            var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
