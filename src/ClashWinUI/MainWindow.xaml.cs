using Clash.SDK;
using Clash.SDK.Models.Events;
using ClashDotNetFramework.Controllers;
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

        public MainWindow()
        {
            InitializeComponent();
            RootGrid.DataContext = Localization.Instance;

            Title = $"Clash .NET Framework v{UpdateChecker.Version}";
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 750));

            Global.Pages.Init();
            InitClash();

            NavView.SelectedItem = NavView.MenuItems[0];

            Task.Run(() => UpdateChecker.Check(false));

            Closed += MainWindow_Closed;
        }

        public void ApplyTheme(ThemeType theme)
        {
            RootGrid.RequestedTheme = theme == ThemeType.Dark ? ElementTheme.Dark : ElementTheme.Light;
        }

        public void InitEvent()
        {
            Global.clashClient.TrafficReceivedEvt -= OnTrafficUpdated;
            Global.clashClient.TrafficReceivedEvt += OnTrafficUpdated;
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is not NavigationViewItem item)
                return;

            Page page = item.Tag?.ToString() switch
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
                        TunHelper.ApplyTun(selected.FileName);
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
            Global.RunOnUI(() =>
            {
                StatusLight.Fill = new SolidColorBrush(connected ? Color.FromArgb(255, 65, 184, 131) : Color.FromArgb(255, 255, 0, 0));
                StatusText.Text = Localization.Instance[connected ? "Connected" : "Disconnected"];
            });
        }

        private void InitVersion()
        {
            try
            {
                var response = Global.clashClient.GetClashVersion().GetAwaiter().GetResult();
                Global.RunOnUI(() => CoreVersionText.Text = $"{Localization.Instance["CoreVersion"]}: {response.Version}");
            }
            catch
            {
            }
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