using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Orva.Domain.Billing;
using Orva.ViewModels;

namespace Orva.Controls
{
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = value is bool && (bool)value;
            string param = parameter as string;
            if (!string.IsNullOrEmpty(param) && string.Equals(param.Trim(), "Invert", StringComparison.OrdinalIgnoreCase))
            {
                flag = !flag;
            }
            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool visible = value is Visibility && (Visibility)value == Visibility.Visible;
            string param = parameter as string;
            if (!string.IsNullOrEmpty(param) && string.Equals(param.Trim(), "Invert", StringComparison.OrdinalIgnoreCase))
            {
                visible = !visible;
            }
            return visible;
        }
    }

    public sealed class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(value is bool) || !(bool)value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(value is bool) || !(bool)value;
        }
    }

    public sealed class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value == null ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class BillingSeverityToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is BillingIssueSeverity)
            {
                var issue = (BillingIssueSeverity)value;
                if (issue == BillingIssueSeverity.Blocking)
                    return new SolidColorBrush(Color.FromRgb(0xC9, 0x36, 0x2B));
                if (issue == BillingIssueSeverity.Warning)
                    return new SolidColorBrush(Color.FromRgb(0xFF, 0x4B, 0x1F));
            }
            return new SolidColorBrush(Color.FromRgb(0x17, 0x2C, 0x30));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class PaiseToRupeesConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return "—";
            try
            {
                long paise = System.Convert.ToInt64(value, CultureInfo.InvariantCulture);
                string param = parameter as string;
                if (!string.IsNullOrEmpty(param) && string.Equals(param.Trim(), "symbol", StringComparison.OrdinalIgnoreCase))
                {
                    return BillingDisplay.FormatRupees(paise);
                }
                return BillingDisplay.FormatPaise(paise);
            }
            catch
            {
                return "—";
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class QuantityMilliToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return "0";
            try { return BillingDisplay.FormatQuantity(System.Convert.ToInt64(value, CultureInfo.InvariantCulture)); }
            catch { return "0"; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class ExpiryYyyyMmToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;
            int parsed;
            if (!int.TryParse(System.Convert.ToString(value, CultureInfo.InvariantCulture), out parsed))
                return string.Empty;

            var year = parsed / 100;
            var month = parsed % 100;
            if (year < 1 || year > 9999 || month < 1 || month > 12) return string.Empty;
            return new DateTime(year, month, 1).ToString("MMM yyyy", culture ?? CultureInfo.CurrentCulture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}