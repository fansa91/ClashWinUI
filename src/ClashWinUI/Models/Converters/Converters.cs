using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.UI;

namespace ClashDotNetFramework.Models.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value is bool b && b ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => value is Visibility v && v == Visibility.Visible;
    }

    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value is bool b && !b;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => value is bool b && !b;
    }

    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    public class LatencyToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            long latency = System.Convert.ToInt64(value);
            if (latency == -2) return "Untested";
            if (latency <= 0) return "Timeout";
            return $"{latency} ms";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    public class LatencyToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            long latency = System.Convert.ToInt64(value);
            if (latency <= 0) return new SolidColorBrush(Color.FromArgb(255, 236, 38, 88));
            if (latency < 200) return new SolidColorBrush(Color.FromArgb(255, 65, 184, 131));
            if (latency < 500) return new SolidColorBrush(Color.FromArgb(255, 245, 166, 35));
            return new SolidColorBrush(Color.FromArgb(255, 236, 38, 88));
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    public class LevelToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string level = (value as string)?.ToLowerInvariant() ?? "";
            return level switch
            {
                "error" => new SolidColorBrush(Color.FromArgb(255, 236, 38, 88)),
                "warning" => new SolidColorBrush(Color.FromArgb(255, 245, 166, 35)),
                "info" => new SolidColorBrush(Color.FromArgb(255, 65, 184, 131)),
                _ => new SolidColorBrush(Color.FromArgb(255, 150, 150, 150))
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    public class SelectedToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool selected = value is bool b && b;
            if (selected)
                return new SolidColorBrush(Global.ProxyColor);
            return new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    public class ProfileTypeToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value?.ToString() ?? "";

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}