using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StoryBoarder.Controls;

/// <summary>Divides the first bound number by the second.</summary>
public sealed class DivideConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is [double value, double divisor] && divisor > 0 ? value / divisor : DependencyProperty.UnsetValue;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
