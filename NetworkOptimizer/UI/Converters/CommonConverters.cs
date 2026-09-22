using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace NetworkOptimizer.UI.Converters;

public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b ? !b : false;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b ? !b : false;
}

public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility v && v != Visibility.Visible;
}

public class LogLevelToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0, 245, 160));  // Neon Emerald
    private static readonly SolidColorBrush InfoBrush = new(Color.FromRgb(0, 210, 255));     // Neon Cyan
    private static readonly SolidColorBrush WarnBrush = new(Color.FromRgb(255, 184, 0));     // Neon Amber
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(255, 83, 112));   // Coral Red
    private static readonly SolidColorBrush DefaultBrush = new(Color.FromRgb(160, 174, 192));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var level = value?.ToString()?.ToUpperInvariant() ?? "INFO";
        return level switch
        {
            "SUCCESS" => SuccessBrush,
            "INFO" => InfoBrush,
            "WARN" => WarnBrush,
            "ERROR" => ErrorBrush,
            _ => DefaultBrush
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class LatencyToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush FastBrush = new(Color.FromRgb(0, 245, 160));
    private static readonly SolidColorBrush MediumBrush = new(Color.FromRgb(255, 184, 0));
    private static readonly SolidColorBrush SlowBrush = new(Color.FromRgb(255, 83, 112));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
        {
            if (d < 30) return FastBrush;
            if (d < 80) return MediumBrush;
            return SlowBrush;
        }
        if (value is long l)
        {
            if (l < 30) return FastBrush;
            if (l < 80) return MediumBrush;
            return SlowBrush;
        }
        return FastBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
