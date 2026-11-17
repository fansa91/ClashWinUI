using Clash.SDK.Models.Enums;
using Clash.SDK.Models.Events;
using ClashDotNetFramework.Controllers;
using ClashDotNetFramework.Utils;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace ClashDotNetFramework.Pages
{
    public sealed partial class OverviewPage : Page
    {
        /// <summary>本地化字符串表，供 x:Bind 编译期绑定使用</summary>
        public Localization Loc => Localization.Instance;

        public ISeries[] Series { get; private set; }

        /// <summary>Y 轴固定从 0 开始，避免流量图出现无意义的负数刻度；X 轴隐藏索引标签。</summary>
        public Axis[] YAxes { get; } = { new Axis { MinLimit = 0 } };

        public Axis[] XAxes { get; } = { new Axis { Labels = Array.Empty<string>(), ShowSeparatorLines = false } };

        private readonly Style _accentButtonStyle;

        private bool _hasTraffic;

        private readonly ObservableCollection<double> _upload = new() { 0.00 };
        private readonly ObservableCollection<double> _download = new() { 0.00 };
        private readonly DispatcherTimer _uptimeTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private DateTime _startTime = DateTime.Now;
        private bool _loading;

        /// <summary>上一次滴答时的内核运行状态，用于识别“启动”瞬间重置计时基准</summary>
        private bool _coreRunning;

        /// <summary>当前代理模式；内核未连接时保持默认值，按钮不可点。</summary>
        private ModeType _mode = ModeType.Rule;

        /// <summary>已订阅事件的客户端实例。内核重启会换新实例，需要重新挂事件。</summary>
        private Clash.SDK.ClashClient _subscribedClient;

        public OverviewPage()
        {
            InitializeComponent();
            DataContext = Localization.Instance;

            // 状态药丸出现时轻微放大 + 淡入，内核启停的状态切换更有反馈（阶段 5）
            Motion.SetPopInOnShow(ConnectedPill);
            Motion.SetPopInOnShow(DisconnectedPill);

            // 记住强调按钮样式：内核运行时切回普通按钮，重新启动时再套回来
            _accentButtonStyle = CoreButton.Style;

            BuildSeries();

            // 运行时赋值而非 x:Bind：LiveCharts 的 Series / Axis[] 在 x:Bind 生成代码里类型解析会失败
            TrafficChart.Series = Series;
            TrafficChart.YAxes = YAxes;
            TrafficChart.XAxes = XAxes;

            RefreshModeSelection();

            _uptimeTimer.Tick += (s, e) => UpdateCoreButton();
        }

        /// <summary>
        /// 按内容区实测宽度切换卡片布局。
        /// 不用 AdaptiveTrigger：导航从展开窗格折成图标栏时，内容区宽度会突然变宽，
        /// 用窗口宽度判断会出现“窗口变窄、卡片反而变成多列”的反直觉跳变。
        /// </summary>
        private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double width = e.NewSize.Width;

            if (width <= 0)
                return;

            string state = width >= 880 ? "WideDashboard"
                : width >= 745 ? "MediumDashboard"
                : "NarrowDashboard";

            VisualStateManager.GoToState(this, state, false);
        }

        /// <summary>
        /// 按当前调色板重建曲线。图表画笔是 SkiaSharp 对象，不参与 ThemeResource，
        /// 主题切换后必须重建一次才能换色。
        /// </summary>
        private void BuildSeries()
        {
            // 坐标轴文字与网格线也取主题色：LiveCharts 默认是固定灰，深色主题下会偏暗
            var labelPaint = new SolidColorPaint(ToSkColor(ThemeTokens.TextTertiary));
            var gridPaint = new SolidColorPaint(ToSkColor(ThemeTokens.ChartGrid)) { StrokeThickness = 1 };

            YAxes[0].LabelsPaint = labelPaint;
            YAxes[0].SeparatorsPaint = gridPaint;
            XAxes[0].LabelsPaint = labelPaint;

            Series = new ISeries[]
            {
                new LineSeries<double>
                {
                    Name = "Upload",
                    Values = _upload,
                    LineSmoothness = 1,
                    GeometrySize = 0,
                    Fill = null,
                    Stroke = new SolidColorPaint(ToSkColor(ThemeTokens.ChartUpload)) { StrokeThickness = 2 }
                },
                new LineSeries<double>
                {
                    Name = "Download",
                    Values = _download,
                    LineSmoothness = 1,
                    GeometrySize = 0,
                    Fill = null,
                    Stroke = new SolidColorPaint(ToSkColor(ThemeTokens.ChartDownload)) { StrokeThickness = 2 }
                }
            };
        }

        /// <summary>主题切换后重算图表配色与由代码着色的模式选项</summary>
        public void RefreshSemanticColors()
        {
            BuildSeries();
            TrafficChart.Series = Series;
            TrafficChart.YAxes = YAxes;
            TrafficChart.XAxes = XAxes;
            RefreshModeSelection();
        }

        private static SKColor ToSkColor(Windows.UI.Color color)
            => new SKColor(color.R, color.G, color.B, color.A);

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _startTime = DateTime.Now;
            _uptimeTimer.Start();

            _loading = true;
            SystemProxySwitch.IsOn = Global.Settings.SystemProxy;
            TunModeSwitch.IsOn = Global.Settings.TunMode;
            _loading = false;
            UpdateCoreButton();

            SubscribeTraffic();
            _ = Task.Run(LoadRuntimeInfoAsync);
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _uptimeTimer.Stop();
            UnsubscribeTraffic();
        }

        /// <summary>
        /// 窗口最小化 / 隐藏时暂停界面刷新（阶段 5）：运行时长计时器停掉，
        /// 流量 / 连接事件里的文字与图表更新由 OnTrafficUpdated / OnConnectionUpdated 里的守卫跳过。
        /// 恢复时若页面仍在前台则重新启动计时器；时间基准不重置，运行时长不回退。
        /// </summary>
        public void SetUiPaused(bool paused)
        {
            if (paused)
            {
                _uptimeTimer.Stop();
            }
            else if (IsLoaded)
            {
                _uptimeTimer.Start();
            }
        }

        /// <summary>
        /// 订阅流量与连接事件。内核启停会重建 ClashClient，页面停留期间也要能重新挂上，
        /// 否则从这张卡启停内核后，数值会停在上一个实例上不再刷新。
        /// </summary>
        private void SubscribeTraffic()
        {
            var client = Global.clashClient;
            if (client == null || ReferenceEquals(client, _subscribedClient))
                return;

            UnsubscribeTraffic();
            client.TrafficReceivedEvt += OnTrafficUpdated;
            client.ConnectionUpdatedEvt += OnConnectionUpdated;
            _subscribedClient = client;
        }

        private void UnsubscribeTraffic()
        {
            if (_subscribedClient == null)
                return;

            _subscribedClient.TrafficReceivedEvt -= OnTrafficUpdated;
            _subscribedClient.ConnectionUpdatedEvt -= OnConnectionUpdated;
            _subscribedClient = null;
        }

        private async Task LoadRuntimeInfoAsync()
        {
            try
            {
                var config = await Global.clashClient.GetClashConfigs();
                string yes = Localization.Instance["Yes"];
                string no = Localization.Instance["No"];
                string allowLan = config.AllowLan ? yes : no;
                string ipv6 = config.IPV6 ? yes : no;
                var mode = config.Mode;
                string version = (await Global.clashClient.GetClashVersion()).Version;

                Global.RunOnUI(() =>
                {
                    _mode = mode;
                    MixedPortText.Text = config.MixedPort.ToString();
                    AllowLanText.Text = allowLan;
                    Ipv6Text.Text = ipv6;
                    CoreVersionText.Text = version;
                    RefreshModeSelection();
                });
            }
            catch
            {
                // 内核未启动 / 断线重连期间取不到配置，保持上一次的显示即可
            }
        }

        private void OnTrafficUpdated(object sender, TrafficEvtArgs e)
        {
            var traffic = e.Response;
            Global.RunOnUI(() =>
            {
                // 最小化 / 隐藏窗口时跳过图表与文字刷新；数据链路照常，恢复后下一事件继续
                if (Global.UiPaused)
                    return;

                string oldUpload = UploadSpeedText.Text;
                string oldDownload = DownloadSpeedText.Text;
                SplitSpeed(UnitConverter.FormatBytes(traffic.Up), UploadSpeedText, UploadUnitText);
                SplitSpeed(UnitConverter.FormatBytes(traffic.Down), DownloadSpeedText, DownloadUnitText);

                // 数值变化渐变：轻微变亮一次，表示“已更新”而不闪烁
                if (oldUpload != UploadSpeedText.Text)
                    Motion.PlayValuePulse(UploadSpeedText);
                if (oldDownload != DownloadSpeedText.Text)
                    Motion.PlayValuePulse(DownloadSpeedText);

                // 有任意方向流量后隐藏"等待流量"提示
                if (!_hasTraffic && (traffic.Up > 0 || traffic.Down > 0))
                {
                    _hasTraffic = true;
                    TrafficEmptyHint.Visibility = Visibility.Collapsed;
                }

                _upload.Add(Convert.ToDouble(traffic.Up));
                _download.Add(Convert.ToDouble(traffic.Down));
                while (_upload.Count > 60)
                    _upload.RemoveAt(0);
                while (_download.Count > 60)
                    _download.RemoveAt(0);
            });
        }

        private void OnConnectionUpdated(object sender, ConnectionEvtArgs e)
        {
            var response = e.Response;
            Global.RunOnUI(() =>
            {
                if (Global.UiPaused)
                    return;

                string oldCount = ConnectionCountText.Text;
                string oldUpload = TotalUploadText.Text;
                string oldDownload = TotalDownloadText.Text;

                ConnectionCountText.Text = (response.Connections?.Count ?? 0).ToString();
                TotalUploadText.Text = UnitConverter.FormatBytes(response.UploadTotal);
                TotalDownloadText.Text = UnitConverter.FormatBytes(response.DownloadTotal);

                if (oldCount != ConnectionCountText.Text)
                    Motion.PlayValuePulse(ConnectionCountText);
                if (oldUpload != TotalUploadText.Text)
                    Motion.PlayValuePulse(TotalUploadText);
                if (oldDownload != TotalDownloadText.Text)
                    Motion.PlayValuePulse(TotalDownloadText);
            });
        }

        private void SystemProxySwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            CoreManager.SetSystemProxy(((ToggleSwitch)sender).IsOn);
        }

        private async void TunModeSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool on = ((ToggleSwitch)sender).IsOn;
            await CoreManager.SetTunMode(on);
            // 开启 TUN 会顺带关闭系统代理（或启动失败时回滚），两个开关一起同步
            Global.RunOnUI(() =>
            {
                _loading = true;
                try
                {
                    TunModeSwitch.IsOn = Global.Settings.TunMode;
                    SystemProxySwitch.IsOn = Global.Settings.SystemProxy;
                }
                finally
                {
                    _loading = false;
                }
            });
        }

        private void ModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            var mode = (button.Tag as string) switch
            {
                "Global" => ModeType.Global,
                "Direct" => ModeType.Direct,
                _ => ModeType.Rule
            };

            if (mode == _mode)
                return;

            _mode = mode;
            RefreshModeSelection();

            if (Global.clashClient == null || Global.clashController?.IsRunning != true)
            {
                // 内核没起时切换无法生效，直接回到真实状态
                _ = Task.Run(LoadRuntimeInfoAsync);
                return;
            }

            _ = Task.Run(() => ChangeModeAsync(mode));
        }

        private async Task ChangeModeAsync(ModeType mode)
        {
            try
            {
                await Global.clashClient.ChangeClashConfigs(
                    new Dictionary<string, dynamic> { { "mode", mode.ToString() } });
                Global.Settings.Mode = mode.ToString().ToLowerInvariant();
                Configuration.Save();

                var selected = Global.Settings.Profile.FirstOrDefault(p => p.IsSelected);
                if (selected != null)
                    ConfigInjector.Apply(selected.FileName);
            }
            catch
            {
                // 写入失败时向内核重新取一次真实模式，避免按钮停在一个没生效的值上
                await LoadRuntimeInfoAsync();
            }
        }

        /// <summary>把当前模式同步到三个选项的选中标记与说明文字</summary>
        private void RefreshModeSelection()
        {
            ApplyModeState(RuleModeCheck, RuleModeText, _mode == ModeType.Rule);
            ApplyModeState(GlobalModeCheck, GlobalModeText, _mode == ModeType.Global);
            ApplyModeState(DirectModeCheck, DirectModeText, _mode == ModeType.Direct);

            if (ModeHintText != null)
            {
                ModeHintText.Text = Localization.Instance[_mode switch
                {
                    ModeType.Direct => "ModeHintDirect",
                    ModeType.Global => "ModeHintGlobal",
                    _ => "ModeHintRule"
                }];
            }
        }

        private static void ApplyModeState(FontIcon check, TextBlock text, bool selected)
        {
            if (check == null || text == null)
                return;

            // 选中标记的点亮 / 熄灭用 150ms 透明度过渡，替代原来的一步切换
            Motion.AnimateOpacity(check, selected ? 1 : 0);
            text.Foreground = ThemeBrush(selected ? "BrandPrimaryBrush" : "TextSecondaryBrush");
            text.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }

        /// <summary>按主题资源取画笔；键不存在时返回 null，由控件自身样式兜底。</summary>
        private static Brush ThemeBrush(string key)
        {
            var resources = (IDictionary<object, object>)Application.Current.Resources;
            return resources.TryGetValue(key, out var value) && value is Brush brush ? brush : null;
        }

        private async void CoreButton_Click(object sender, RoutedEventArgs e)
        {
            CoreButton.IsEnabled = false;
            if (Global.clashController?.IsRunning == true)
                await CoreManager.StopCore();
            else
                await CoreManager.StartCore();
            Global.RunOnUI(() =>
            {
                UpdateCoreButton();
                CoreButton.IsEnabled = true;
            });

            SubscribeTraffic();
            await LoadRuntimeInfoAsync();
        }

        private void UpdateCoreButton()
        {
            bool running = Global.clashController?.IsRunning == true;

            // 计时基准随内核状态维护：启动瞬间重置，未启动时显示 00:00:00 且不走
            if (running && !_coreRunning)
                _startTime = DateTime.Now;
            _coreRunning = running;

            CoreButton.Content = Localization.Instance[running ? "StopCore" : "StartCore"];
            CoreButton.Style = running ? null : _accentButtonStyle;

            UptimeText.Text = running ? FormatUptime(DateTime.Now - _startTime) : "00:00:00";

            // 状态药丸：两套配色都写在 XAML 里走资源，这里只切换显隐
            ConnectedPill.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            DisconnectedPill.Visibility = running ? Visibility.Collapsed : Visibility.Visible;

            // 内核未运行时切模式没有意义
            RuleModeButton.IsEnabled = running;
            GlobalModeButton.IsEnabled = running;
            DirectModeButton.IsEnabled = running;
        }

        /// <summary>把 "1.23 KB/s" 拆成数值与单位，分别写入两个 TextBlock 以实现字号分层。</summary>
        private static void SplitSpeed(string formatted, TextBlock value, TextBlock unit)
        {
            const string suffix = "/s";
            string text = formatted.EndsWith(suffix, StringComparison.Ordinal)
                ? formatted.Substring(0, formatted.Length - suffix.Length)
                : formatted;

            int split = text.LastIndexOf(' ');
            if (split > 0)
            {
                value.Text = text.Substring(0, split);
                unit.Text = text.Substring(split + 1) + suffix;
            }
            else
            {
                value.Text = text;
                unit.Text = suffix;
            }
        }

        /// <summary>
        /// 语言切换后刷新由代码写入的文案。
        /// 端口 / 局域网 / IPv6 依赖运行时状态，需要重新向内核取一次。
        /// </summary>
        public void RefreshLocalizedText()
        {
            UpdateCoreButton();
            RefreshModeSelection();
            _ = Task.Run(LoadRuntimeInfoAsync);
        }

        private static string FormatUptime(TimeSpan ts)
        {
            return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
        }
    }
}
