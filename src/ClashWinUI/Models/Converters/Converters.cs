using ClashDotNetFramework.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.UI;

namespace ClashDotNetFramework.Models.Converters
{
    /// <summary>
    /// 语义色板：集中管理「成功 / 警告 / 严重 / 中性」四类语义颜色。
    ///
    /// 色值本身不再写在这里，而是来自当前主题的调色板（Themes/Palette.*.xaml），
    /// 由 <see cref="ThemeTokens"/> 缓存后供转换器等无法使用 ThemeResource 的场景读取。
    /// 这样新增一套主题时只需要加一个 xaml 文件，代码侧无需改动。
    ///
    /// 参数 <c>dark</c> 保留是为了兼容既有调用点，实际取值已由调色板决定。
    /// </summary>
    public static class SemanticPalette
    {
        public static Color Success(bool dark) => ThemeTokens.Success;

        public static Color SuccessBackground(bool dark) => ThemeTokens.SuccessSubtle;

        public static Color Caution(bool dark) => ThemeTokens.Warning;

        public static Color CautionBackground(bool dark) => ThemeTokens.WarningSubtle;

        public static Color Critical(bool dark) => ThemeTokens.Danger;

        public static Color CriticalBackground(bool dark) => ThemeTokens.DangerSubtle;

        public static Color Neutral(bool dark) => ThemeTokens.Neutral;

        public static Color NeutralBackground(bool dark) => ThemeTokens.NeutralSubtle;

        /// <summary>强调色，用于协议 / 策略等中性的分类标签</summary>
        public static Color Brand(bool dark) => ThemeTokens.Brand;

        /// <summary>信息色，用于"负载均衡"这类既非成功也非警告的分类</summary>
        public static Color Info(bool dark) => ThemeTokens.Info;

        public static Color InfoBackground(bool dark) => ThemeTokens.InfoSubtle;
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
                _ => new SolidColorBrush(SemanticPalette.NeutralBackground(dark))
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

    /// <summary>
    /// 规则策略 / 连接协议的语义色：
    /// parameter = "proxy" 时 REJECT 用严重色、DIRECT 用中性色、其余用强调蓝；
    /// parameter = "network" 时 TCP 用蓝、UDP 用警告色。
    /// 前景色统一走这里，避免 XAML 里出现硬编码色值。
    /// </summary>
    public class SemanticTagConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string text = value?.ToString()?.ToLowerInvariant() ?? "";
            bool dark = Global.IsDarkTheme;
            Color color;

            if (string.Equals(parameter as string, "network", StringComparison.OrdinalIgnoreCase))
                color = text == "udp" ? SemanticPalette.Caution(dark) : SemanticPalette.Brand(dark);
            else
                color = text == "reject" ? SemanticPalette.Critical(dark)
                    : text == "direct" ? SemanticPalette.Neutral(dark)
                    : SemanticPalette.Brand(dark);

            return new SolidColorBrush(color);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 更新时间 → 相对时间（刚刚 / N 分钟前 / N 小时前 / N 天前）。
    /// 用于订阅列表，避免直接显示原始时间戳难以判断新旧。
    /// </summary>
    public class RelativeTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is not DateTime time)
                return string.Empty;

            TimeSpan delta = DateTime.Now - time;
            if (delta.TotalSeconds < 0)
                return Localization.Instance.JustNow;
            if (delta.TotalMinutes < 1)
                return Localization.Instance.JustNow;
            if (delta.TotalHours < 1)
                return string.Format(Localization.Instance.MinutesAgo, (int)delta.TotalMinutes);
            if (delta.TotalDays < 1)
                return string.Format(Localization.Instance.HoursAgo, (int)delta.TotalHours);
            return string.Format(Localization.Instance.DaysAgo, (int)delta.TotalDays);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}
