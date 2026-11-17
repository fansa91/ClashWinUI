using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace ClashDotNetFramework.Utils
{
    /// <summary>
    /// 轻量动效层（阶段 5）
    /// =====================
    /// 全部使用 WinUI 3 自带的 Composition API（ElementCompositionPreview + KeyFrame/Expression 动画），
    /// 不引入第三方动画库。时长统一控制在 150–250ms；所有调用都包在 try/catch 里，
    /// 合成器不可用（如远程会话、驱动异常）时静默降级为无动画，绝不影响功能。
    ///
    /// 动效时长令牌（本类集中管理，与 Themes/Tokens.xaml 的尺寸令牌对应）：
    ///   DurationFast   150ms —— 图标 / 徽章这类小元素
    ///   DurationHover  160ms —— 卡片悬停进 / 出
    ///   DurationNormal 200ms —— 页面入场
    ///   DurationPulse  180ms —— 数值变化渐变
    /// </summary>
    public static class Motion
    {
        public static readonly TimeSpan DurationFast = TimeSpan.FromMilliseconds(150);
        public static readonly TimeSpan DurationHover = TimeSpan.FromMilliseconds(160);
        public static readonly TimeSpan DurationPulse = TimeSpan.FromMilliseconds(180);
        public static readonly TimeSpan DurationNormal = TimeSpan.FromMilliseconds(200);

        /// <summary>卡片悬停的放大比例，肉眼可辨但不干扰阅读</summary>
        private const float HoverScaleTarget = 1.012f;

        /// <summary>数值变化渐变的起始透明度：轻微变亮收尾，避免每秒刷新看起来像闪烁</summary>
        private const float PulseFromOpacity = 0.82f;

        /// <summary>已挂接悬停动效的元素表，防止重复挂 Pointer 事件</summary>
        private static readonly ConditionalWeakTable<FrameworkElement, object> _hoverAttached = new();

        // ==================== 页面切换过渡 ====================

        /// <summary>
        /// 页面入场：透明度 0→1 + 自当前上方 20px 滑落。
        /// 终点关键帧用 this.StartingValue 表达式，动画结束后属性回到布局值，
        /// 不会与布局管理的 Offset 打架，窗口缩放期间也不会落错位。
        /// </summary>
        public static void PlayEntrance(FrameworkElement element)
        {
            if (element == null)
                return;

            try
            {
                var visual = ElementCompositionPreview.GetElementVisual(element);
                var compositor = visual.Compositor;
                var ease = CreateDecelerate(compositor);

                var slide = compositor.CreateVector3KeyFrameAnimation();
                slide.Duration = DurationNormal;
                slide.InsertExpressionKeyFrame(0f,
                    "Vector3(this.StartingValue.X, this.StartingValue.Y + 20.0f, this.StartingValue.Z)", ease);
                slide.InsertExpressionKeyFrame(1f, "this.StartingValue", ease);
                visual.StartAnimation("Offset", slide);

                var fade = compositor.CreateScalarKeyFrameAnimation();
                fade.Duration = DurationNormal;
                fade.InsertKeyFrame(0f, 0f, ease);
                fade.InsertKeyFrame(1f, 1f, ease);
                visual.StartAnimation("Opacity", fade);
            }
            catch
            {
                // 动效失败不构成功能问题：保持无动画即可
            }
        }

        // ==================== 卡片悬停 ====================

        /// <summary>
        /// 遍历整棵控件树，给所有基于 CardBorderStyle 的卡片挂上悬停轻抬动效。
        /// 页面初始化完成后调用一次即可；本项目 XAML 编译器不加载本程序集，
        /// 无法在样式里引用 Motion，因此统一由代码挂接（PageCollection.Init 调用）。
        /// </summary>
        public static void AttachHoverToCards(DependencyObject root)
        {
            if (root == null)
                return;

            try
            {
                var cardBase = GetCardBaseStyle();
                if (cardBase == null)
                    return;

                WalkAndAttach(root, cardBase);
            }
            catch
            {
            }
        }

        private static Style GetCardBaseStyle()
        {
            var resources = (IDictionary<object, object>)Application.Current.Resources;
            return resources.TryGetValue("CardBorderStyle", out var value) ? value as Style : null;
        }

        private static void WalkAndAttach(DependencyObject node, Style cardBase)
        {
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is Border border && IsStyleBasedOn(border.Style, cardBase))
                    AttachHoverScale(border);

                WalkAndAttach(child, cardBase);
            }
        }

        /// <summary>判断样式是否派生自卡片基础样式（沿 BasedOn 链向上找，覆盖所有派生卡样式）</summary>
        private static bool IsStyleBasedOn(Style style, Style cardBase)
        {
            for (var s = style; s != null; s = s.BasedOn)
            {
                if (ReferenceEquals(s, cardBase))
                    return true;
            }
            return false;
        }

        /// <summary>给单个元素挂悬停缩放；进入围绕中心放大 1.2%，移出还原，进/出各 160ms</summary>
        public static void AttachHoverScale(FrameworkElement element)
        {
            if (element == null)
                return;

            // 同一元素只挂一次（树遍历可能多次命中容器嵌套）
            if (_hoverAttached.TryGetValue(element, out _))
                return;
            _hoverAttached.Add(element, null);

            element.PointerEntered += (s, e) => HoverScaleTo(element, HoverScaleTarget);
            element.PointerExited += (s, e) => HoverScaleTo(element, 1f);
            element.PointerCanceled += (s, e) => HoverScaleTo(element, 1f);
        }

        private static void HoverScaleTo(FrameworkElement element, float target)
        {
            try
            {
                var visual = ElementCompositionPreview.GetElementVisual(element);

                // 围绕卡片中心缩放；尺寸取悬停时刻的实测值，避免首帧测量未完成时中心点为 0
                float halfWidth = (float)element.ActualWidth / 2f;
                float halfHeight = (float)element.ActualHeight / 2f;
                if (halfWidth > 0 && halfHeight > 0)
                    visual.CenterPoint = new Vector3(halfWidth, halfHeight, 0f);

                var compositor = visual.Compositor;
                var ease = CreateDecelerate(compositor);

                var scaleX = compositor.CreateScalarKeyFrameAnimation();
                var scaleY = compositor.CreateScalarKeyFrameAnimation();
                scaleX.Duration = DurationHover;
                scaleY.Duration = DurationHover;
                // 起点用 this.StartingValue：上次动画播到一半时来新的悬停，也从当前位置平滑续走
                scaleX.InsertExpressionKeyFrame(0f, "this.StartingValue", ease);
                scaleY.InsertExpressionKeyFrame(0f, "this.StartingValue", ease);
                scaleX.InsertKeyFrame(1f, target, ease);
                scaleY.InsertKeyFrame(1f, target, ease);
                visual.StartAnimation("Scale.X", scaleX);
                visual.StartAnimation("Scale.Y", scaleY);

                // 底层值同步写为目标：动画结束后放大状态能“停住”，移出后再归位
                visual.Scale = new Vector3(target, target, 1f);
            }
            catch
            {
            }
        }

        // ==================== 显隐与开关反馈 ====================

        /// <summary>
        /// 为元素注册“出现动画”：每次 Visibility 变为可见时自动播放
        /// 轻微放大 + 系统自带的淡入（状态药丸 / 选中标记这类反馈用）。
        /// </summary>
        public static void SetPopInOnShow(FrameworkElement element)
        {
            if (element == null)
                return;

            try
            {
                var visual = ElementCompositionPreview.GetElementVisual(element);
                var compositor = visual.Compositor;
                var ease = CreateDecelerate(compositor);

                var pop = compositor.CreateVector3KeyFrameAnimation();
                pop.Duration = DurationFast;
                pop.InsertKeyFrame(0f, new Vector3(0.92f, 0.92f, 1f), ease);
                pop.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f), ease);
                ElementCompositionPreview.SetImplicitShowAnimation(element, pop);
            }
            catch
            {
            }
        }

        /// <summary>
        /// 把元素的透明度平滑过渡到目标值（代理模式选中标记的点亮 / 熄灭）。
        /// 先启动动画再写底层值：从当前值渐变过去，落点即底层值，不会有跳变。
        /// </summary>
        public static void AnimateOpacity(FrameworkElement element, double target)
        {
            if (element == null)
                return;

            try
            {
                var visual = ElementCompositionPreview.GetElementVisual(element);
                var compositor = visual.Compositor;
                var ease = CreateDecelerate(compositor);

                var fade = compositor.CreateScalarKeyFrameAnimation();
                fade.Duration = DurationFast;
                fade.InsertExpressionKeyFrame(0f, "this.StartingValue", ease);
                fade.InsertKeyFrame(1f, (float)target, ease);
                visual.StartAnimation("Opacity", fade);
            }
            catch
            {
            }
            finally
            {
                // 底层值无论如何都要落定，保证合成器不可用时状态仍然正确
                element.Opacity = target;
            }
        }

        // ==================== 数值变化渐变 ====================

        /// <summary>
        /// 数值更新时轻微变亮一次（0.82 → 1.0）。
        /// 供每秒刷新的指标文字调用：值在变、且有一个柔和的“已更新”提示，
        /// 又不至于每秒闪一下。同一个元素重复调用直接重启动画。
        /// </summary>
        public static void PlayValuePulse(FrameworkElement element)
        {
            if (element == null)
                return;

            try
            {
                var visual = ElementCompositionPreview.GetElementVisual(element);
                var compositor = visual.Compositor;
                var ease = CreateDecelerate(compositor);

                var pulse = compositor.CreateScalarKeyFrameAnimation();
                pulse.Duration = DurationPulse;
                pulse.InsertKeyFrame(0f, PulseFromOpacity, ease);
                pulse.InsertKeyFrame(1f, 1f, ease);
                visual.StartAnimation("Opacity", pulse);
            }
            catch
            {
            }
        }

        // ==================== 内部 ====================

        /// <summary>标准减速曲线：前段快后段缓，与 Fluent 动效观感一致</summary>
        private static CubicBezierEasingFunction CreateDecelerate(Compositor compositor)
            => compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.1f, 0.9f), new Vector2(0.1f, 1.0f));
    }
}
