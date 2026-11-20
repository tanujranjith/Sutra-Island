using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DynamicIsland.Windows.Views;

public sealed class IslandFontSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        parameter is string text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var size)
            ? size * (value is double factor ? factor : 1d)
            : DependencyProperty.UnsetValue;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}
