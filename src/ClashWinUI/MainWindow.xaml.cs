using Clash.SDK;
using Clash.SDK.Models.Events;
using ClashDotNetFramework.Controllers;
using ClashDotNetFramework.Models.Converters;
using ClashDotNetFramework.Models.Enums;
using ClashDotNetFramework.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;
using WindowsProxy;

namespace ClashDotNetFramework
{
    public sealed partial class MainWindow : Window
    {
        private readonly DispatcherTimer _connectionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.5) };
        private DateTime _beginStamp = DateTime.MinValue;
        private bool _suppressSelection;
        private bool _isConnected = true;
        private string _coreVersion;

        /// <summary>
        /// 本地化字符串表，供 x:Bind 使用
        /// </summary>
        public Localization Loc => Localization.Instance;

        public MainWindow()
        {
            InitializeComponent();

            // Windows 11 原生材质 + 沉浸式标题栏
            SystemBackdrop = new MicaBackdrop();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(NavView);

            Title = $"Clash .NET Framework v{UpdateChecker.Version}";
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 800));
            // 窗口再窄会让设置页的标签列被压成 0 宽（只剩无说明的开关），这里兜住最小可用尺寸
            WindowSizing.ApplyMinimumSize(this, 940, 600);

            Global.Pages.Init();
            InitClash();

            NavView.SelectedItem = NavView.MenuItems[0];

            Task.Run(() => UpdateChecker.Check(false));

            Closed += MainWindow_Closed;
        }

        public void ApplyTheme(ThemeType theme)
        {
            bool dark = theme == ThemeType.Dark;
            Global.IsDarkTheme = dark;
            RootGrid.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
            Global.Pages?.RefreshSemanticColors();
        }

        public void InitEvent()
        {
            Global.clashClient.TrafficReceivedEvt -= OnTrafficUpdated;
            Global.clashClient.TrafficReceivedEvt += OnTrafficUpdated;
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (_suppressSelection)
                return;
            if (args.SelectedItem is not NavigationViewItem item)
                return;

            string tag = item.Tag as string;
            if (string.IsNullOrEmpty(tag))
                return;

            ApplySelection(tag);
            NavigateTo(tag);
        }

        /// <summary>
        /// 底部（PaneFooter）中的导航项点击处理
        /// </summary>
        private void FooterNavItem_Click(object sender, RoutedEventArgs e)
        {
            string tag = (sender as FrameworkElement)?.Tag as string;
            if (string.IsNullOrEmpty(tag))
                return;

            ApplySelection(tag);
            NavigateTo(tag);
        }

        private void ApplySelection(string tag)
        {
            _suppressSelection = true;
            try
            {
                foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
                    item.IsSelected = string.Equals(item.Tag as string, tag, StringComparison.Ordinal);

                SettingsFooterHighlight.Visibility = string.Equals(tag, "Settings", StringComparison.Ordinal)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            finally
            {
                _suppressSelection = false;
            }
        }

        private void NavigateTo(string tag)
        {
            Page page = tag switch
            {
                "Overview" => Global.Pages.overviewPage,
                "Proxies" => Global.Pages.proxiesPage,
                "Profiles" => Global.Pages.profilesPage,
                "Rules" => Global.Pages.rulesPage,
                "Logs" => Global.Pages.logsPage,
                "Connections" => Global.Pages.connectionsPage,
                "Settings" => Global.Pages.settingsPage,
                "Support" => Global.Pages.supportPage,
                _ => null
            };
            if (page != null)
                PageFrame.Content = page;
        }

        private void InitClash()
        {
            Global.clashClient = new ClashClient(Global.ClashControllerPort);
            Task.Run(() =>
            {
                bool connected = false;
                int tryTimes = 0;
                while (!connected && tryTimes < 7)
                {
                    connected = Global.clashClient.GetClashStatus().GetAwaiter().GetResult();
                    if (connected)
                        break;
                    tryTimes++;
                    Task.Delay(1000);
                }
                if (connected)
                {
                    GetClashMixedPort();
                    var selected = Global.Settings.Profile.FirstOrDefault(p => p.IsSelected);
                    if (selected != null)
                    {
                        // 内核 reload 会整体替换配置, 客户端设置需先注入订阅文件
                        ConfigInjector.Apply(selected.FileName);
                        Global.clashClient.ReloadClashConfig(Clash.SDK.Models.Enums.ConfigType.Path, false, selected.FileName).GetAwaiter().GetResult();
                    }
                    InitTimer();
                    InitStatus();
                    InitVersion();
                    Global.clashClient.GetClashTraffic();
                    Global.clashClient.GetClashLog();
                    Global.clashClient.GetClashConnection();
                    Global.clashClient.TrafficReceivedEvt += OnTrafficUpdated;
                    InitProxy();
                }
                else
                {
                    InitStatus(false);
                }
            });
        }

        private void InitProxy()
        {
            Task.Run(() =>
            {
                if (Global.Settings.SystemProxy)
                {
                    using var service = new ProxyService
                    {
                        Server = $"127.0.0.1:{Global.ClashMixedPort}",
                        Bypass = string.Join(@";", ProxyService.LanIp)
                    };
                    service.Global();
                }
                Global.iconController.SetIcon();
            });
        }

        private void GetClashMixedPort()
        {
            var resp = Global.clashClient.GetClashConfigs().GetAwaiter().GetResult();
            Global.ClashMixedPort = resp.MixedPort;
        }

        private void InitTimer()
        {
            _beginStamp = DateTime.Now;
            Global.RunOnUI(() =>
            {
                _connectionTimer.Tick += ConnectionTimer_Tick;
                _connectionTimer.Start();
            });
        }

        private void InitStatus(bool connected = true)
        {
            _isConnected = connected;
            Global.RunOnUI(() =>
            {
                StatusLight.Fill = new SolidColorBrush(connected
                    ? SemanticPalette.Success(Global.IsDarkTheme)
                    : SemanticPalette.Critical(Global.IsDarkTheme));
                StatusText.Text = Localization.Instance[connected ? "Connected" : "Disconnected"];
            });
        }

        private void InitVersion()
        {
            try
            {
                var response = Global.clashClient.GetClashVersion().GetAwaiter().GetResult();
                _coreVersion = response.Version;
                Global.RunOnUI(() => CoreVersionText.Text = $"{Localization.Instance["CoreVersion"]}: {_coreVersion}");
            }
            catch
            {
            }
        }

        /// <summary>
        /// 侧栏的状态与版本是代码写入的（非 x:Bind），切换语言后需要主动重写一次，
        /// 否则会出现"侧栏中文 + 内容区英文"的混合界面。
        /// </summary>
        public void RefreshLocalizedText()
        {
            InitStatus(_isConnected);
            if (!string.IsNullOrEmpty(_coreVersion))
                CoreVersionText.Text = $"{Localization.Instance["CoreVersion"]}: {_coreVersion}";
        }

        private void ConnectionTimer_Tick(object sender, object e)
        {
            if (_beginStamp == DateTime.MinValue)
                return;
            var ts = DateTime.Now - _beginStamp;
            ConnectionTime.Text = $"{ts.Hours:00} : {ts.Minutes:00} : {ts.Seconds:00}";
        }

        private void OnTrafficUpdated(object sender, TrafficEvtArgs e)
        {
            var traffic = e.Response;
            Global.RunOnUI(() =>
            {
                UploadSpeed.Text = $"{UnitConverter.FormatBytes(traffic.Up)}/s";
                DownloadSpeed.Text = $"{UnitConverter.FormatBytes(traffic.Down)}/s";
            });
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            try
            {
                Global.iconController.Stop();
                if (Global.Settings.SystemProxy)
                {
                    using var service = new ProxyService();
                    service.Direct();
                }
                Global.clashController?.Stop();
                Configuration.Save();
            }
            catch
            {
            }
        }
    }
}
