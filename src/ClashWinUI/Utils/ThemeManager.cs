using ClashDotNetFramework.Models.Enums;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.UI;

namespace ClashDotNetFramework.Utils
{
    /// <summary>
    /// 主题调色板管理。
    ///
    /// 设计：三套主题（经典 / 现代 / 深色）各对应一份完整的颜色资源字典
    /// （Themes/Palette.*.xaml），键名完全一致。切换主题时把 App.xaml 中合并的
    /// 那一份整份替换掉；页面上的颜色属性统一以 {ThemeResource ...} 引用，
    /// 资源替换后会重新求值，因此不需要重建页面。
    ///
    /// "经典"和"现代"都是浅色底，靠强调色与表面色区分；深浅明暗由
    /// MainWindow 设置的 RequestedTheme 负责，两者互不冲突。
    /// </summary>
    public static class ThemeManager
    {
        private const string PaletteFolder = "ms-appx:///Themes/";
        private const string PaletteMarker = "Palette.";

        private static ThemeType? _applied;

        /// <summary>当前生效的主题</summary>
        public static ThemeType Current { get; private set; } = ThemeType.Modern;

        /// <summary>
        /// 应用调色板。可重复调用，主题未变化时只刷新代码侧颜色缓存。
        /// 必须在 UI 线程调用（会读取 Application.Resources）。
        /// </summary>
        public static void Apply(ThemeType theme)
        {
            if (Application.Current == null)
                return;

            Current = theme;

            if (_applied == theme)
            {
                ThemeTokens.Refresh();
                return;
            }

            string file = theme switch
            {
                ThemeType.Classic => "Palette.Classic.xaml",
                ThemeType.Dark => "Palette.Dark.xaml",
                _ => "Palette.Modern.xaml"
            };

            try
            {
                var merged = Application.Current.Resources.MergedDictionaries;
                int index = FindPaletteIndex(merged);
                var palette = new ResourceDictionary { Source = new Uri(PaletteFolder + file, UriKind.Absolute) };

                if (index >= 0)
                    merged[index] = palette;
                else
                    merged.Add(palette);
            }
            catch (Exception ex)
            {
                // 调色板替换失败不应该让应用崩溃：保留 App.xaml 里合并的默认调色板，
                // 界面顶多停在上一套配色。
                System.Diagnostics.Debug.WriteLine($"[ThemeManager] 切换调色板失败: {ex}");
            }

            _applied = theme;
            ThemeTokens.Refresh();
        }

        /// <summary>在合并字典中定位调色板所在位置，保证替换后合并顺序不变</summary>
        private static int FindPaletteIndex(System.Collections.Generic.IList<ResourceDictionary> merged)
        {
            for (int i = 0; i < merged.Count; i++)
            {
                string source = merged[i].Source?.OriginalString;
                if (source != null && source.IndexOf(PaletteMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return i;
            }
            return -1;
        }

        /// <summary>按资源键取颜色；键不存在时返回透明，调用方自行兜底</summary>
        public static Color GetColor(string key)
        {
            if (Application.Current != null &&
                Application.Current.Resources.TryGetValue(key, out object value) &&
                value is SolidColorBrush brush)
            {
                return brush.Color;
            }
            return Color.FromArgb(0x00, 0x00, 0x00, 0x00);
        }

        /// <summary>按资源键取画刷；键不存在时返回透明画刷</summary>
        public static SolidColorBrush GetBrush(string key)
            => new SolidColorBrush(GetColor(key));
    }

    /// <summary>
    /// 颜色令牌的代码侧缓存。
    ///
    /// XAML 覆盖不到的地方（IValueConverter、SkiaSharp 图表画笔）从这里取色，
    /// 这样色值仍然只在 Themes/Palette.*.xaml 里定义一次，不会出现"两个真源"。
    /// 读的是普通静态字段，后台线程调用也安全；主题切换时由 ThemeManager 统一刷新。
    /// </summary>
    public static class ThemeTokens
    {
        // Refresh() 之前的兜底值用中性灰，避免在代码里出现第二份主题色。
        private static readonly Color Placeholder = Color.FromArgb(0xFF, 0x80, 0x80, 0x80);
        private static readonly Color PlaceholderSubtle = Color.FromArgb(0x1A, 0x80, 0x80, 0x80);
        private static readonly Color Transparent = Color.FromArgb(0x00, 0x00, 0x00, 0x00);

        public static Color Brand { get; private set; } = Placeholder;
        public static Color BrandHover { get; private set; } = Placeholder;
        public static Color BrandSubtle { get; private set; } = PlaceholderSubtle;
        public static Color TextPrimary { get; private set; } = Placeholder;
        public static Color TextSecondary { get; private set; } = Placeholder;
        public static Color TextTertiary { get; private set; } = Placeholder;

        public static Color Success { get; private set; } = Placeholder;
        public static Color SuccessSubtle { get; private set; } = PlaceholderSubtle;
        public static Color Warning { get; private set; } = Placeholder;
        public static Color WarningSubtle { get; private set; } = PlaceholderSubtle;
        public static Color Danger { get; private set; } = Placeholder;
        public static Color DangerSubtle { get; private set; } = PlaceholderSubtle;
        public static Color Neutral { get; private set; } = Placeholder;
        public static Color NeutralSubtle { get; private set; } = PlaceholderSubtle;
        public static Color Info { get; private set; } = Placeholder;
        public static Color InfoSubtle { get; private set; } = PlaceholderSubtle;

        public static Color ChartUpload { get; private set; } = Placeholder;
        public static Color ChartDownload { get; private set; } = Placeholder;
        public static Color ChartGrid { get; private set; } = Transparent;

        /// <summary>从当前调色板重新读取全部令牌，由 ThemeManager 在切换后调用</summary>
        public static void Refresh()
        {
            Brand = ThemeManager.GetColor("BrandPrimaryBrush");
            BrandHover = ThemeManager.GetColor("BrandPrimaryHoverBrush");
            BrandSubtle = ThemeManager.GetColor("BrandSubtleBrush");
            TextPrimary = ThemeManager.GetColor("TextPrimaryBrush");
            TextSecondary = ThemeManager.GetColor("TextSecondaryBrush");
            TextTertiary = ThemeManager.GetColor("TextTertiaryBrush");

            Success = ThemeManager.GetColor("StatusSuccessBrush");
            SuccessSubtle = ThemeManager.GetColor("StatusSuccessSubtleBrush");
            Warning = ThemeManager.GetColor("StatusWarningBrush");
            WarningSubtle = ThemeManager.GetColor("StatusWarningSubtleBrush");
            Danger = ThemeManager.GetColor("StatusDangerBrush");
            DangerSubtle = ThemeManager.GetColor("StatusDangerSubtleBrush");
            Neutral = ThemeManager.GetColor("StatusNeutralBrush");
            NeutralSubtle = ThemeManager.GetColor("StatusNeutralSubtleBrush");
            Info = ThemeManager.GetColor("StatusInfoBrush");
            InfoSubtle = ThemeManager.GetColor("StatusInfoSubtleBrush");

            ChartUpload = ThemeManager.GetColor("ChartUploadBrush");
            ChartDownload = ThemeManager.GetColor("ChartDownloadBrush");
            ChartGrid = ThemeManager.GetColor("ChartGridBrush");
        }
    }
}
