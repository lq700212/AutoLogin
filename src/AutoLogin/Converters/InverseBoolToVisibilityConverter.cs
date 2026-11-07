using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AutoLogin.Converters
{
    /// <summary>
    /// true -> Collapsed, false -> Visible。用于“显示密码”时的密码框/文本框切换。
    /// </summary>
    public sealed class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = value is bool && (bool)value;
            return flag ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
