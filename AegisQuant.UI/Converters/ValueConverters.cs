using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AegisQuant.UI.Services;

namespace AegisQuant.UI.Converters;

/// <summary>
/// Converts a boolean to its inverse.
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return value;
    }
}

/// <summary>
/// Converts a boolean to Visibility (inverse - true = Collapsed, false = Visible).
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility != Visibility.Visible;
        }
        return false;
    }
}

/// <summary>
/// Checks if a number is negative.
/// </summary>
public class IsNegativeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double doubleValue)
        {
            return doubleValue < 0;
        }
        if (value is decimal decimalValue)
        {
            return decimalValue < 0;
        }
        if (value is int intValue)
        {
            return intValue < 0;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}


/// <summary>
/// Converts a ratio (0-1) to a width value.
/// ConverterParameter specifies the maximum width.
/// </summary>
public class RatioToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double ratio)
        {
            double maxWidth = 200; // Default max width
            if (parameter is string paramStr && double.TryParse(paramStr, out var parsed))
            {
                maxWidth = parsed;
            }
            else if (parameter is double paramDouble)
            {
                maxWidth = paramDouble;
            }

            // Clamp ratio between 0 and 1
            ratio = Math.Max(0, Math.Min(1, ratio));
            return ratio * maxWidth;
        }
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a price change value to a color brush.
/// Positive = Up color, Negative = Down color, Zero = Flat color.
/// </summary>
public class PriceChangeToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double change = 0;
        if (value is double d)
        {
            change = d;
        }
        else if (value is decimal dec)
        {
            change = (double)dec;
        }

        return ColorSchemeService.Instance.GetPriceChangeBrush(change);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a boolean to a color brush.
/// True = Up color, False = Down color.
/// </summary>
public class BoolToUpDownColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isUp)
        {
            var colorService = ColorSchemeService.Instance;
            return isUp ? colorService.UpBrush : colorService.DownBrush;
        }
        return ColorSchemeService.Instance.FlatBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts two values (current and reference) to a color brush.
/// </summary>
public class PriceToColorConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is double current && values[1] is double reference)
        {
            return ColorSchemeService.Instance.GetPriceBrush(current, reference);
        }
        return ColorSchemeService.Instance.FlatBrush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}


/// <summary>
/// Checks if a number is positive.
/// </summary>
public class IsPositiveConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double doubleValue)
        {
            return doubleValue > 0;
        }
        if (value is decimal decimalValue)
        {
            return decimalValue > 0;
        }
        if (value is int intValue)
        {
            return intValue > 0;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a non-empty string to Visibility.Visible, empty to Collapsed.
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            return string.IsNullOrEmpty(str) ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a boolean to Visibility (true = Visible, false = Collapsed).
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility == Visibility.Visible;
        }
        return false;
    }
}
