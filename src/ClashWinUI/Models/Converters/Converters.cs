using ClashDotNetFramework.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.UI;

namespace ClashDotNetFramework.Models.Converters
{
    /// <summary>
    /// 语义色板：集中管理“成功 / 警告 / 严重 / 中性”四类语义颜色。
    /// 所有颜色都在此处以代码方式定义，XAML 中不再出现任何硬编码色值；
    /// 深浅两套取值根据 <see cref="Global.IsDarkTheme"/> 选择，保证浅色 / 深色主题下均有足够对比度。
    /// </summary>
    public static class SemanticPalette
    {
        public static Color Success(bool dark) => dark
            ? Color.FromArgb(0xFF, 0x6C, 0xCB, 0x5F)
            : Color.FromArgb(0xFF, 0x0F, 0x7B, 0x0F);

        public static Color SuccessBackground(bool dark) => dark
            ? Color.FromArgb(0xFF, 0x1F, 0x3A, 0x1B)
            : Color.FromArgb(0xFF, 0xD8, 0xF2, 0xD5);

        public static Color Caution(bool dark) => dark
            ? Color.FromArgb(0xFF, 0xFC, 0xE1, 0x00)
            : Color.FromArgb(0xFF, 0x8A, 0x50, 0x00);

        public static Color CautionBackground(bool dark) => dark
            ? Color.FromArgb(0xFF, 0x3B, 0x2C, 0x00)
            : Color.FromArgb(0xFF, 0xFF, 0xF3, 0xCE);

        public static Color Critical(bool dark) => dark
            ? Color.FromArgb(0xFF, 0xFF, 0x99, 0xA4)
            : Color.FromArgb(0xFF, 0xC4, 0x2B, 0x1C);

        public static Color CriticalBackground(bool dark) => dark
            ? Color.FromArgb(0xFF, 0x42, 0x14, 0x18)
            : Color.FromArgb(0xFF, 0xFD, 0xE7, 0xE9);

        public static Color Neutral(bool dark) => dark
            ? Color.FromArgb(0xFF, 0xC8, 0xC8, 0xC8)
            : Color.FromArgb(0xFF, 0x6B, 0x6B, 0x6B);
    }

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

    /// <summary>
    /// 把延迟数值转换为可读文本，未测试(-2)与超时(&lt;=0)使用本地化文案。
    /// </summary>
    public class LatencyToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            long latency = ToLatency(value);
            if (latency == -2) return Localization.Instance["Untested"];
            if (latency <= 0) return Localization.Instance["Timeout"];
            return $"{latency} ms";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();

        internal static long ToLatency(object value)
        {
            try
            {
                return System.Convert.ToInt64(value);
            }
            catch
            {
                return -2;
            }
        }
    }

    /// <summary>
    /// 延迟徽章前景色：
    /// &lt; 150ms 绿色(成功)，150 - 400ms 橙色(警告)，&gt; 400ms 或超时为红色(严重)。
    /// </summary>
    public class LatencyToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            long latency = LatencyToTextConverter.ToLatency(value);
            bool dark = Global.IsDarkTheme;

            if (latency <= 0)
                return new SolidColorBrush(SemanticPalette.Critical(dark));
            if (latency < 150)
                return new SolidColorBrush(SemanticPalette.Success(dark));
            if (latency <= 400)
                return new SolidColorBrush(SemanticPalette.Caution(dark));
            return new SolidColorBrush(SemanticPalette.Critical(dark));
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 延迟徽章背景色，与 <see cref="LatencyToBrushConverter"/> 使用同一套阈值语义。
    /// </summary>
    public class LatencyToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            long latency = LatencyToTextConverter.ToLatency(value);
            bool dark = Global.IsDarkTheme;

            if (latency <= 0)
                return new SolidColorBrush(SemanticPalette.CriticalBackground(dark));
            if (latency < 150)
                return new SolidColorBrush(SemanticPalette.SuccessBackground(dark));
            if (latency <= 400)
                return new SolidColorBrush(SemanticPalette.CautionBackground(dark));
            return new SolidColorBrush(SemanticPalette.CriticalBackground(dark));
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 兼容旧调用点的延迟前景色转换器，语义与 <see cref="LatencyToBrushConverter"/> 一致。
    /// </summary>
    public class LatencyToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => new LatencyToBrushConverter().Convert(value, targetType, parameter, language);

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 日志等级前景色：error 严重、warning 警告、info 成功、其余中性。
    /// </summary>
    public class LevelToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string level = (value as string)?.ToLowerInvariant() ?? "";
            bool dark = Global.IsDarkTheme;

            return level switch
            {
                "error" => new SolidColorBrush(SemanticPalette.Critical(dark)),
                "warning" => new SolidColorBrush(SemanticPalette.Caution(dark)),
                "info" => new SolidColorBrush(SemanticPalette.Success(dark)),
                _ => new SolidColorBrush(SemanticPalette.Neutral(dark))
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 日志等级徽章背景色。
    /// </summary>
    public class LevelToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string level = (value as string)?.ToLowerInvariant() ?? "";
            bool dark = Global.IsDarkTheme;

            return level switch
            {
                "error" => new SolidColorBrush(SemanticPalette.CriticalBackground(dark)),
                "warning" => new SolidColorBrush(SemanticPalette.CautionBackground(dark)),
                "info" => new SolidColorBrush(SemanticPalette.SuccessBackground(dark)),
                _ => new SolidColorBrush(SemanticPalette.Neutral(dark))
            };
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

    /// <summary>
    /// 把 0 - 1 的比例转换为 ProgressBar 需要的 0 - 100 数值。
    /// </summary>
    public class RatioToPercentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            double ratio = value is double d ? d : 0;
            return Math.Clamp(ratio, 0, 1) * 100;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 节点数量 -> "N 个节点"：直接显示裸数字（例如 "0"）看不出含义。
    /// </summary>
    public class NodesCountConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            long count = value is IConvertible convertible ? convertible.ToInt64(null) : 0;
            return string.Format(Localization.Instance.NodesCount, count);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}
