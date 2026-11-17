using Clash.SDK.Models.Enums;
using Clash.SDK.Models.Events;
using ClashDotNetFramework.Controllers;
using ClashDotNetFramework.Models.Converters;
using ClashDotNetFramework.Utils;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace ClashDotNetFramework.Pages
{
    public sealed partial class OverviewPage : Page
    {
        /// <summary>本地化字符串表，供 x:Bind 编译期绑定使用</summary>
        public Localization Loc => Localization.Instance;

        public ISeries[] Series { get; set; }

        /// <summary>Y 轴固定从 0 开始，避免流量图出现无意义的负数刻度；X 轴隐藏索引标签。</summary>
        public Axis[] YAxes { get; } = { new Axis { MinLimit = 0 } };

        public Axis[] XAxes { get; } = { new Axis { Labels = Array.Empty<string>(), ShowSeparatorLines = false } };

        private bool _hasTraffic;

        private readonly ObservableCollection<double> _upload = new() { 0.00 };
        private readonly ObservableCollection<double> _download = new() { 0.00 };
        private readonly DispatcherTimer _uptimeTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private DateTime _startTime = DateTime.Now;
        private bool _loading;

        public OverviewPage()
        {
            InitializeComponent();
            DataContext = Localization.Instance;

            Series = new ISeries[]
            {
                new LineSeries<double>
                {
                    Name = "Upload",
                    Values = _upload,
                    LineSmoothness = 1,
                    GeometrySize = 0,
                    Fill = null,
                    Stroke = new SolidColorPaint(SKColors.DeepSkyBlue) { StrokeThickness = 2 }
                },
                new LineSeries<double>
                {
                    Name = "Download",
                    Values = _download,
                    LineSmoothness = 1,
                    GeometrySize = 0,
                    Fill = null,
                    Stroke = new SolidColorPaint(SKColors.MediumSeaGreen) { StrokeThickness = 2 }
                }
            };

            // 运行时赋值而非 x:Bind：LiveCharts 的 Axis[] 在 x:Bind 生成代码里类型解析会失败
            TrafficChart.YAxes = YAxes;
            TrafficChart.XAxes = XAxes;

            _uptimeTimer.Tick += (s, e) =>
            {
                UptimeText.Text = FormatUptime(DateTime.Now - _startTime);
                UpdateCoreButton();
            };
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _startTime = DateTime.Now;
            _uptimeTimer.Start();

            _loading = true;
            SystemProxySwitch.IsOn = Global.Settings.SystemProxy;
            TunModeSwitch.IsOn = Global.Settings.TunMode;
            _loading = false;
            UpdateCoreButton();

            if (Global.clashClient != null)
            {
                Global.clashClient.TrafficReceivedEvt += OnTrafficUpdated;
                Global.clashClient.ConnectionUpdatedEvt += OnConnectionUpdated;
            }
            Task.Run(() => LoadRuntimeInfo());
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _uptimeTimer.Stop();
            if (Global.clashClient != null)
            {
                Global.clashClient.TrafficReceivedEvt -= OnTrafficUpdated;
                Global.clashClient.ConnectionUpdatedEvt -= OnConnectionUpdated;
            }
        }

        private void LoadRuntimeInfo()
        {
            try
            {
                var config = Global.clashClient.GetClashConfigs().GetAwaiter().GetResult();
                string mode = config.Mode switch
                {
                    ModeType.Direct => Localization.Instance["Direct"],
                    ModeType.Global => Localization.Instance["Global"],
                    _ => Localization.Instance["Rule"]
                };
                string yes = Localization.Instance["Yes"];
                string no = Localization.Instance["No"];
                string allowLan = config.AllowLan ? yes : no;
                string ipv6 = config.IPV6 ? yes : no;
                string version = Global.clashClient.GetClashVersion().GetAwaiter().GetResult().Version;

                Global.RunOnUI(() =>
                {
                    ModeText.Text = mode;
                    ModeBadgeText.Text = mode;
                    MixedPortText.Text = config.MixedPort.ToString();
                    AllowLanText.Text = allowLan;
                    Ipv6Text.Text = ipv6;
                    CoreVersionText.Text = version;
                });
            }
            catch
            {
            }
        }

        private void OnTrafficUpdated(object sender, TrafficEvtArgs e)
        {
            var traffic = e.Response;
            Global.RunOnUI(() =>
            {
                SplitSpeed(UnitConverter.FormatBytes(traffic.Up), UploadSpeedText, UploadUnitText);
                SplitSpeed(UnitConverter.FormatBytes(traffic.Down), DownloadSpeedText, DownloadUnitText);

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
                ConnectionCountText.Text = (response.Connections?.Count ?? 0).ToString();
                TotalUploadText.Text = UnitConverter.FormatBytes(response.UploadTotal);
                TotalDownloadText.Text = UnitConverter.FormatBytes(response.DownloadTotal);
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
        }

        private void UpdateCoreButton()
        {
            bool running = Global.clashController?.IsRunning == true;
            CoreButton.Content = Localization.Instance[running ? "StopCore" : "StartCore"];
            CoreStatusText.Text = Localization.Instance[running ? "Connected" : "Disconnected"];
            CoreStatusLight.Fill = new SolidColorBrush(running
                ? SemanticPalette.Success(Global.IsDarkTheme)
                : SemanticPalette.Critical(Global.IsDarkTheme));
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
        /// ModeText / AllowLanText / Ipv6Text 依赖运行时状态，需要重新向内核取一次。
        /// </summary>
        public void RefreshLocalizedText()
        {
            UpdateCoreButton();
            Task.Run(() => LoadRuntimeInfo());
        }

        private static string FormatUptime(TimeSpan ts)
        {
            return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
        }
    }
}