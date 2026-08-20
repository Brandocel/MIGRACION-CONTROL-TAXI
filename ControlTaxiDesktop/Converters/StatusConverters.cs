using System.Globalization;
using System.Windows.Data;
using System.Windows;
using System.Windows.Media;

namespace ControlTaxiDesktop.Converters;

public sealed class UpperCaseConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as string ?? string.Empty).ToUpperInvariant();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StatusBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        IsNeutral(value) ? new SolidColorBrush(Color.FromRgb(0xF1, 0xF4, 0xF9))
        : IsDone(value) ? new SolidColorBrush(Color.FromRgb(0xEA, 0xF8, 0xF2))
        : new SolidColorBrush(Color.FromRgb(0xFB, 0xF3, 0xE4));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    internal static bool IsDone(object? value) => (value as string ?? string.Empty).Contains("PAGAD", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Estados que no exigen accion ("SIN COMISION", "SIN DEJADA"): van en gris neutro. Antes
    /// caian en el ambar de "pendiente" y parecia que habia algo por cobrar.
    /// </summary>
    internal static bool IsNeutral(object? value)
    {
        var text = (value as string ?? string.Empty).Trim();
        return text.Length == 0 || text.StartsWith("SIN", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class StatusForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        StatusBackgroundConverter.IsNeutral(value) ? new SolidColorBrush(Color.FromRgb(0x6B, 0x7F, 0xA6))
        : StatusBackgroundConverter.IsDone(value) ? new SolidColorBrush(Color.FromRgb(0x0F, 0x6F, 0x58))
        : new SolidColorBrush(Color.FromRgb(0x9A, 0x6F, 0x13));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StatusBorderConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        StatusBackgroundConverter.IsNeutral(value) ? new SolidColorBrush(Color.FromRgb(0xDC, 0xE5, 0xF2))
        : StatusBackgroundConverter.IsDone(value) ? new SolidColorBrush(Color.FromRgb(0xBF, 0xE7, 0xD2))
        : new SolidColorBrush(Color.FromRgb(0xEC, 0xD8, 0xA7));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class PaidStatusVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isPaid = StatusBackgroundConverter.IsDone(value);
        var mode = (parameter as string ?? string.Empty).Trim().ToUpperInvariant();
        return mode switch
        {
            "PAIDONLY" => isPaid ? Visibility.Visible : Visibility.Collapsed,
            _ => isPaid ? Visibility.Collapsed : Visibility.Visible
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
