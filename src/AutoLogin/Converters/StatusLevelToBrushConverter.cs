using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using AutoLogin.Models;

namespace AutoLogin.Converters
{
    public sealed class StatusLevelToBrushConverter : IValueConverter
    {
        private static readonly Brush InfoBrush = Freeze(Color.FromRgb(0xC9, 0xD4, 0xE8));
        private static readonly Brush SuccessBrush = Freeze(Color.FromRgb(0x6E, 0xE0, 0x7A));
        private static readonly Brush WarningBrush = Freeze(Color.FromRgb(0xFF, 0xBF, 0x59));
        private static readonly Brush ErrorBrush = Freeze(Color.FromRgb(0xFF, 0x7A, 0x7A));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            StatusLevel level = value is StatusLevel ? (StatusLevel)value : StatusLevel.Info;
            switch (level)
            {
                case StatusLevel.Success: return SuccessBrush;
                case StatusLevel.Warning: return WarningBrush;
                case StatusLevel.Error: return ErrorBrush;
                default: return InfoBrush;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }

        private static Brush Freeze(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }

    public sealed class LogLevelToBrushConverter : IValueConverter
    {
        private static readonly Brush InfoBrush = Freeze(Color.FromRgb(0xC9, 0xD4, 0xE8));
        private static readonly Brush SuccessBrush = Freeze(Color.FromRgb(0x6E, 0xE0, 0x7A));
        private static readonly Brush WarningBrush = Freeze(Color.FromRgb(0xFF, 0xBF, 0x59));
        private static readonly Brush ErrorBrush = Freeze(Color.FromRgb(0xFF, 0x7A, 0x7A));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            LogLevel level = value is LogLevel ? (LogLevel)value : LogLevel.Info;
            switch (level)
            {
                case LogLevel.Success: return SuccessBrush;
                case LogLevel.Warning: return WarningBrush;
                case LogLevel.Error: return ErrorBrush;
                default: return InfoBrush;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }

        private static Brush Freeze(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
