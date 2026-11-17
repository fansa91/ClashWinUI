using Clash.SDK;
using Clash.SDK.Models.Events;
using ClashDotNetFramework.Controllers;
using ClashDotNetFramework.Models.Converters;
using ClashDotNetFramework.Models.Enums;
using ClashDotNetFramework.Utils;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
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

        /// <summary>当前是否处于“窗口不可见”状态（最小化 / 隐藏到托盘），此时暂停界面刷新</summary>
        private bool _uiPaused;

        /// <summary>
        /// 失去激活时的一次性延迟检查：最小化会先触发 Deactivated，
        /// 而 OverlappedPresenter.State 可能略滞后，延一拍再读才可靠。
        /// </summary>
        private DispatcherQueueTimer _deferredPauseCheck;

        /// <summary>
        /// 本地化字符串表，供 x:Bind 使用
        /// </summary>
        public Localization Loc => Localization.Instance;

        public MainWindow()
        {
            InitializeComponent();

            // Windows 11 原生材质 + 沉浸式标题栏；Mica 不可用时退到亚克力，
            // 两者都不支持（如部分 Win10 版本）则保留系统默认底色。
            try
            {
                if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
                    SystemBackdrop = new MicaBackdrop();
                else if (Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported())
                    SystemBackdrop = new DesktopAcrylicBackdrop();
            }
            catch
            {
            }

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(NavView);

            Title = $"Clash .NET Framework v{UpdateChecker.Version}";
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 800));
            // 窗口再窄会让设置页的标签列被压成 0 宽（只剩无说明的开关），这里兜住最小可用尺寸
            WindowSizing.ApplyMinimumSize(this, 940, 600);

            Global.Pages.Init();
            InitClash();
            InitWindowMonitor();

            // 菜单里夹着分组标题（NavigationViewItemHeader，不是 NavigationViewItem），
            // 不能按索引取第一项，否则会选中一个标题、且不触发页面导航。
            NavView.SelectedItem = NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault();

            Task.Run(() => UpdateChecker.Check(false));

            Closed += MainWindow_Closed;
        }

        public void ApplyTheme(ThemeType theme)
        {
            // 三套主题 = 三份调色板字典（Themes/Palette.*.xaml），整份替换；
            // 深浅明暗另外通过 RequestedTheme 交给 WinUI 内置控件。
            // 两者叠加后：经典 / 现代是浅色底 + 不同强调色，深色是深色底 + 青绿强调。
            ThemeManager.Apply(theme);

            bool dark = theme == ThemeType.Dark;
            Global.IsDarkTheme = dark;
            RootGrid.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;

            // 标题栏按钮不跟随 RequestedTheme（它绘制在窗口边框层），颜色需要手动同步
            ApplyTitleBarTheme();

            // 语义色由代码算出来的控件（延迟徽章、协议标签、图表画笔）不走 ThemeResource，
            // 需要主动通知重算。
            Global.Pages?.RefreshSemanticColors();
        }

        /// <summary>
        /// 让系统绘制的窗口按钮与 Mica 背景、主题文字色协调。
        /// 逐个属性 try：不同 Windows 版本支持度不一致，某一个不支持不应影响其余。
        /// </summary>
        private void ApplyTitleBarTheme()
        {
            try
            {
                var titleBar = AppWindow.TitleBar;
                if (titleBar == null)
                    return;

                Color primary = ThemeManager.GetColor("TextPrimaryBrush");
                Color transparent = Color.FromArgb(0x00, 0x00, 0x00, 0x00);
                Color hover = Color.FromArgb(0x1A, primary.R, primary.G, primary.B);
                Color pressed = Color.FromArgb(0x33, primary.R, primary.G, primary.B);
                Color inactive = Color.FromArgb(0x99, primary.R, primary.G, primary.B);

                void TrySet(Action action)
                {
                    try { action(); } catch { }
                }

                TrySet(() => titleBar.ButtonBackgroundColor = transparent);
                TrySet(() => titleBar.ButtonInactiveBackgroundColor = transparent);
                TrySet(() => titleBar.ButtonForegroundColor = primary);
                TrySet(() => titleBar.ButtonInactiveForegroundColor = inactive);
                TrySet(() => titleBar.ButtonHoverBackgroundColor = hover);
                TrySet(() => titleBar.ButtonHoverForegroundColor = primary);
                TrySet(() => titleBar.ButtonPressedBackgroundColor = pressed);
                TrySet(() => titleBar.ButtonPressedForegroundColor = primary);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TitleBar] 配色同步失败: {ex.Message}");
            }
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

        /// <summary>
        /// 供各页面跳转到指定导航项（例如代理页空状态跳到配置页），同时同步侧栏高亮。
        /// </summary>
        public void NavigateToTag(string tag)
        {
            ApplySelection(tag);
            NavigateTo(tag);
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
            {
                bool changed = !ReferenceEquals(PageFrame.Content, page);
                PageFrame.Content = page;

                // 页面切换过渡：淡入 + 轻微上浮（200ms，WinUI 自带 Composition）。
                // 同一页重复点击不重播，避免选中态刷新时整页闪动。
                if (changed)
                    Motion.PlayEntrance(page);
            }
        }

        /// <summary>
        /// 窗口可见性监测（阶段 5）：最小化或隐藏到托盘时暂停图表 / 列表 / 文字的界面刷新，
        /// 数据链路（内核、WebSocket）照常运行，窗口恢复后自动继续。
        /// 检测信号全部来自 WinUI / Windows App SDK 自带 API：
        ///   Window.Activated —— 失去激活（最小化先走这一步）/ 重新激活；
        ///   OverlappedPresenter.State —— 直接读窗口状态（无 StateChanged 事件，故用延迟检查）；
        ///   AppWindow.Changed + IsVisible —— 覆盖 AppWindow.Hide()/Show() 的托盘隐藏路径。
        /// </summary>
        private void InitWindowMonitor()
        {
            try
            {
                _deferredPauseCheck = DispatcherQueue.GetForCurrentThread().CreateTimer();
                _deferredPauseCheck.IsRepeating = false;
                _deferredPauseCheck.Interval = TimeSpan.FromMilliseconds(250);
                _deferredPauseCheck.Tick += (s, e) => EvaluateUiPause();

                AppWindow.Changed += OnAppWindowChanged;
            }
            catch
            {
                _deferredPauseCheck = null;
            }

            Activated += OnWindowActivated;
        }

        private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                // 最小化时先失激活再变状态；延迟一拍读 State，避免读到还原态漏判
                if (_deferredPauseCheck != null)
                {
                    _deferredPauseCheck.Stop();
                    _deferredPauseCheck.Start();
                }
            }
            else
            {
                _deferredPauseCheck?.Stop();
                EvaluateUiPause();
            }
        }

        private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
            => EvaluateUiPause();

        /// <summary>按当前窗口状态决定是否暂停界面刷新；状态未变时调用是空操作</summary>
        private void EvaluateUiPause()
        {
            bool minimized = false;
            try
            {
                minimized = AppWindow.Presenter is OverlappedPresenter presenter
                    && presenter.State == OverlappedPresenterState.Minimized;
            }
            catch
            {
            }

            bool hidden = false;
            try
            {
                hidden = !AppWindow.IsVisible;
            }
            catch
            {
            }

            SetUiPaused(minimized || hidden);
        }

        private void SetUiPaused(bool paused)
        {
            if (_uiPaused == paused)
                return;

            _uiPaused = paused;
            Global.UiPaused = paused;

            // 侧栏运行时长：暂停期间停止计时器，恢复后继续（时间基准不变，不重置）
            if (paused)
                _connectionTimer.Stop();
            else
                _connectionTimer.Start();

            Global.Pages?.SetUiPaused(paused);
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
            // 窗口最小化 / 隐藏时不刷新侧栏文字；下一个事件周期恢复显示
            if (Global.UiPaused)
                return;

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
