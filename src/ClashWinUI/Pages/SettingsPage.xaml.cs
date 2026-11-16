using Clash.SDK;
using Clash.SDK.Models.Response;
using ClashDotNetFramework.Controllers;
using ClashDotNetFramework.Models.Enums;
using ClashDotNetFramework.Utils;
using Microsoft.Win32;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using WindowsProxy;

namespace ClashDotNetFramework.Pages
{
    public sealed partial class SettingsPage : Page
    {
        /// <summary>本地化字符串表，供 x:Bind 编译期绑定使用</summary>
        public Localization Loc => Localization.Instance;

        private ClashConfigsResponse _clashConfigs;
        private bool _loading;

        public SettingsPage()
        {
            InitializeComponent();
            DataContext = Localization.Instance;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateChecker.NewVersionFound += UpdateChecker_NewVersionFound;
            LoadLocalOptions();
            // 不依赖内核配置能否取到：按钮文案必须反映真实运行状态
            UpdateCoreButton();
            Task.Run(() => GetClashConfigs().GetAwaiter().GetResult());
        }

        private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            UpdateChecker.NewVersionFound -= UpdateChecker_NewVersionFound;
        }

        /// <summary>
        /// 按内核实际运行状态设置"启动内核/停止内核"。
        /// 之前只在配置回填成功后才赋值，内核连不上时按钮会一直停在"启动内核"。
        /// </summary>
        private void UpdateCoreButton()
        {
            CoreSwitchButton.Content = Localization.Instance[
                Global.clashController?.IsRunning == true ? "StopCore" : "StartCore"];
        }

        /// <summary>
        /// 回填所有只依赖本地 settings.json 的控件。
        ///
        /// 这一层必须与内核完全解耦：内核未运行 / 连不上时，若回填写在 GetClashConfigs 的
        /// try 块里，整个 try 一抛异常就会连带把按钮文案、开关状态一起跳过，
        /// 页面会停留在"全空 + 错误的初始值"。这里只读本地状态，永远不会失败。
        /// </summary>
        private void LoadLocalOptions()
        {
            _loading = true;
            LanAuthToggle.IsOn = Global.Settings.LanAuthEnabled;
            LanUserBox.Text = Global.Settings.LanAuthUser;
            LanPassBox.Password = Global.Settings.LanAuthPassword;
            LanSkipBox.Text = Global.Settings.LanSkipAuth;
            LanAllowBox.Text = Global.Settings.LanAllowedIps;
            LanDenyBox.Text = Global.Settings.LanDisallowedIps;
            HostsBox.Text = Global.Settings.CustomHosts;
            TunnelsBox.Text = Global.Settings.CustomTunnels;
            DnsNameserverBox.Text = Global.Settings.DnsNameserver;
            DnsDefaultServerBox.Text = Global.Settings.DnsDefaultNameserver;
            DnsFallbackBox.Text = Global.Settings.DnsFallback;
            DnsPolicyBox.Text = Global.Settings.DnsNameserverPolicy;
            DnsFilterBox.Text = Global.Settings.DnsFakeIpFilter;
            SnippetBox.Text = Global.Settings.CustomSnippet;
            SelectCombo(EnhancedModeBox, string.IsNullOrEmpty(Global.Settings.DnsEnhancedMode)
                ? "keep"
                : Global.Settings.DnsEnhancedMode);

            // 以下开关同样只依赖本地设置，内核不可用时也必须显示真实状态
            SelectCombo(LogLevelBox, Global.Settings.LogLevel);
            SelectCombo(ModeBox, Global.Settings.Mode);
            SelectCombo(GeoIntervalBox, Global.Settings.GeoUpdateInterval.ToString());
            SnifferToggle.IsOn = Global.Settings.Sniffer;
            TcpConcurrentToggle.IsOn = Global.Settings.TcpConcurrent;
            UnifiedDelayToggle.IsOn = Global.Settings.UnifiedDelay;
            FindProcessModeToggle.IsOn = !string.Equals(Global.Settings.FindProcessMode, "off", StringComparison.OrdinalIgnoreCase);
            StoreSelectedToggle.IsOn = Global.Settings.StoreSelected;
            StoreFakeIpToggle.IsOn = Global.Settings.StoreFakeIp;
            GeoAutoUpdateToggle.IsOn = Global.Settings.GeoAutoUpdate;
            GeoIntervalBox.IsEnabled = Global.Settings.GeoAutoUpdate;
            StartWithWindows.IsOn = Global.Settings.StartAfterSystemBoot;
            SystemProxyToggle.IsOn = Global.Settings.SystemProxy;
            TunModeToggle.IsOn = Global.Settings.TunMode;
            IPV6Toggle.IsOn = Global.Settings.IPv6;
            AllowLANToggle.IsOn = Global.Settings.AllowLan;

            switch (Global.Settings.Theme)
            {
                case ThemeType.Classic: ClassicTheme.IsChecked = true; break;
                case ThemeType.Modern: ModernTheme.IsChecked = true; break;
                case ThemeType.Dark: DarkTheme.IsChecked = true; break;
            }
            switch (Global.Settings.Language)
            {
                case LanguageType.English: EnglishLanguage.IsChecked = true; break;
                case LanguageType.Chinese: ChineseLanguage.IsChecked = true; break;
                case LanguageType.Japanese: JapaneseLanguage.IsChecked = true; break;
            }
            _loading = false;
        }

        /// <summary>
        /// 展开/收起 DNS 高级区块。
        /// 5 个多行输入框加起来有 300 多像素，默认收起能让设置页一眼看完。
        /// </summary>
        private void DnsToggleButton_Click(object sender, RoutedEventArgs e)
        {
            bool expanded = DnsAdvancedPanel.Visibility == Visibility.Visible;
            DnsAdvancedPanel.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
            DnsToggleButton.Content = expanded
                ? Localization.Instance["Expand"]
                : Localization.Instance["Collapse"];
        }

        /// <summary>
        /// 语言切换后刷新由代码写入的文案（内核按钮与折叠按钮）。
        /// </summary>
        public void RefreshLocalizedText()
        {
            UpdateCoreButton();
            bool expanded = DnsAdvancedPanel.Visibility == Visibility.Visible;
            DnsToggleButton.Content = expanded
                ? Localization.Instance["Collapse"]
                : Localization.Instance["Expand"];
        }

        private void UpdateChecker_NewVersionFound(object sender, EventArgs e)
        {
            Global.RunOnUI(() =>
            {
                UpdateButton.Visibility = Visibility.Visible;
                UpdateButton.Tag = UpdateChecker.LatestVersionUrl;
            });
        }

        /// <summary>
        /// 只回填内核侧的数据（运行时配置与端口）。
        /// 开关与输入框的回填已在 LoadLocalOptions 中完成，不受这里的成败影响。
        /// </summary>
        private async Task GetClashConfigs()
        {
            try
            {
                _clashConfigs = await Global.clashClient.GetClashConfigs();
                Global.RunOnUI(() =>
                {
                    _loading = true;
                    VersionText.Text = $"v{UpdateChecker.Version}";
                    ControllerPortButton.Content = Convert.ToString(Global.ClashControllerPort);
                    MixedPortButton.Content = Convert.ToString(_clashConfigs.MixedPort);

                    // 以运行时配置为准回填, 保证设置页与内核实际状态一致
                    string logLevel = Convert.ToString(_clashConfigs.LogLevel);
                    if (!string.IsNullOrWhiteSpace(logLevel))
                        Global.Settings.LogLevel = logLevel.Trim().ToLowerInvariant();
                    Global.Settings.Mode = _clashConfigs.Mode.ToString().ToLowerInvariant();
                    Global.Settings.IPv6 = _clashConfigs.IPV6;

                    SelectCombo(LogLevelBox, Global.Settings.LogLevel);
                    SelectCombo(ModeBox, Global.Settings.Mode);
                    IPV6Toggle.IsOn = Global.Settings.IPv6;
                    AllowLANToggle.IsOn = _clashConfigs.AllowLan;
                    Global.Settings.AllowLan = _clashConfigs.AllowLan;
                    UpdateCoreButton();
                    _loading = false;
                });
            }
            catch
            {
                // 内核不可用时不影响已完成的本地回填，只是不显示运行时端口
                Global.RunOnUI(() =>
                {
                    _loading = false;
                    ControllerPortButton.Content = Convert.ToString(Global.ClashControllerPort);
                    MixedPortButton.Content = "—";
                });
            }
        }

        private bool ReconnectClash()
        {
            Global.clashClient = new ClashClient(Global.ClashControllerPort);
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
                var selected = Global.Settings.Profile.FirstOrDefault(p => p.IsSelected);
                if (selected != null)
                {
                    ConfigInjector.Apply(selected.FileName);
                    Global.clashClient.ReloadClashConfig(Clash.SDK.Models.Enums.ConfigType.Path, false, selected.FileName).GetAwaiter().GetResult();
                }
                Global.clashClient.GetClashTraffic();
                Global.clashClient.GetClashLog();
                Global.clashClient.GetClashConnection();
                Global.RunOnUI(() => (Global.MainWindow as MainWindow)?.InitEvent());
            }
            return connected;
        }

        private void CheckMMDBUpdate_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            button.Content = "Updating...";
            string tmpPath = Path.Combine(Utils.Utils.GetClashConfigDir(), "Country.mmdb.tmp");
            string mmdbPath = Path.Combine(Utils.Utils.GetClashConfigDir(), "Country.mmdb");
            Task.Run(() =>
            {
                try
                {
                    var req = WebUtil.CreateRequest(Global.Settings.MMDBUpdateUrl);
                    var result = WebUtil.DownloadString(req, out var rep);
                    File.WriteAllText(tmpPath, result);
                    Global.RunOnUI(() => button.Content = "Stopping Clash...");
                    Global.clashController.Stop();
                    if (File.Exists(mmdbPath))
                        File.Delete(mmdbPath);
                    File.Move(tmpPath, mmdbPath);
                    Global.RunOnUI(() => button.Content = "Starting Clash...");
                    Global.clashController.Start(Global.ClashControllerPort);
                    Global.RunOnUI(() => button.Content = "Connecting...");
                    bool status = ReconnectClash();
                    Global.RunOnUI(() => button.Content = status ? "Done" : "Timeout");
                }
                catch
                {
                    Global.RunOnUI(() => button.Content = "Error");
                }
            });
        }

        private async void ResetAllSettings_Click(object sender, RoutedEventArgs e)
        {
            // 文本类选项没有 Toggled 事件，先静默清空控件，最后统一写盘并注入一次
            _loading = true;
            LanAuthToggle.IsOn = false;
            LanUserBox.Text = string.Empty;
            LanPassBox.Password = string.Empty;
            LanSkipBox.Text = string.Empty;
            LanAllowBox.Text = string.Empty;
            LanDenyBox.Text = string.Empty;
            HostsBox.Text = string.Empty;
            TunnelsBox.Text = string.Empty;
            DnsNameserverBox.Text = string.Empty;
            DnsDefaultServerBox.Text = string.Empty;
            DnsFallbackBox.Text = string.Empty;
            DnsPolicyBox.Text = string.Empty;
            DnsFilterBox.Text = string.Empty;
            SnippetBox.Text = string.Empty;
            SelectCombo(EnhancedModeBox, "keep");
            _loading = false;

            Global.Settings.LanAuthEnabled = false;
            Global.Settings.LanAuthUser = string.Empty;
            Global.Settings.LanAuthPassword = string.Empty;
            Global.Settings.LanSkipAuth = string.Empty;
            Global.Settings.LanAllowedIps = string.Empty;
            Global.Settings.LanDisallowedIps = string.Empty;
            Global.Settings.CustomHosts = string.Empty;
            Global.Settings.CustomTunnels = string.Empty;
            Global.Settings.DnsEnhancedMode = string.Empty;
            Global.Settings.DnsNameserver = string.Empty;
            Global.Settings.DnsDefaultNameserver = string.Empty;
            Global.Settings.DnsFallback = string.Empty;
            Global.Settings.DnsNameserverPolicy = string.Empty;
            Global.Settings.DnsFakeIpFilter = string.Empty;
            Global.Settings.CustomSnippet = string.Empty;
            Global.Settings.AllowLan = false;

            AllowLANToggle.IsOn = false;
            IPV6Toggle.IsOn = false;
            StartWithWindows.IsOn = false;
            SystemProxyToggle.IsOn = false;
            TunModeToggle.IsOn = false;
            SnifferToggle.IsOn = false;
            TcpConcurrentToggle.IsOn = false;
            UnifiedDelayToggle.IsOn = false;
            FindProcessModeToggle.IsOn = false;
            StoreSelectedToggle.IsOn = false;
            StoreFakeIpToggle.IsOn = false;
            GeoAutoUpdateToggle.IsOn = false;

            await ApplyInjectionAsync();
        }

        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            string url = ((Button)sender).Tag?.ToString();
            if (!string.IsNullOrWhiteSpace(url))
                Launch(url);
        }

        private void ControllerPort_Click(object sender, RoutedEventArgs e)
        {
            CopyToClipboard($"http://127.0.0.1:{Global.ClashControllerPort}");
        }

        /// <summary>
        /// 混合端口是本地代理入口，点击复制该地址。
        /// 与控制端口保持同样的交互，避免一个可点一个不可点的割裂感。
        /// </summary>
        private void MixedPort_Click(object sender, RoutedEventArgs e)
        {
            int port = _clashConfigs?.MixedPort ?? Global.ClashMixedPort;
            if (port <= 0)
                return;
            CopyToClipboard($"127.0.0.1:{port}");
        }

        private static void CopyToClipboard(string text)
        {
            try
            {
                var package = new DataPackage();
                package.SetText(text);
                Clipboard.SetContent(package);
            }
            catch
            {
            }
        }

        private void StartWithWindows_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool on = ((ToggleSwitch)sender).IsOn;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                if (on)
                    key.SetValue("Clash .NET Framework", Process.GetCurrentProcess().MainModule.FileName);
                else
                    key.DeleteValue("Clash .NET Framework", false);
            }
            catch
            {
            }
            Global.Settings.StartAfterSystemBoot = on;
        }

        private async void IPV6_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool on = ((ToggleSwitch)sender).IsOn;
            Global.Settings.IPv6 = on;
            await ChangeConfig("ipv6", on);
            // dns.ipv6 与开关联动, 否则 TUN 下 DNS 查询仍走 IPv4 结果
            await ApplyInjectionAsync();
        }

        private async void AllowLAN_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool on = ((ToggleSwitch)sender).IsOn;
            Global.Settings.AllowLan = on;
            await ChangeConfig("allow-lan", on);
            // 落盘后重启即可恢复，内核未运行时设置页也能显示正确状态
            Configuration.Save();
        }

        private async void SystemProxyToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            CoreManager.SetSystemProxy(((ToggleSwitch)sender).IsOn);
        }

        private async void TunModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool on = ((ToggleSwitch)sender).IsOn;
            await CoreManager.SetTunMode(on);
            // 开启 TUN 会顺带关闭系统代理（或启动失败时回滚），这里同步界面，避免开关与实际不一致
            SyncLocalToggles();
        }

        /// <summary>把本地开关状态刷成 Global.Settings 的实际值，刷新期间屏蔽事件避免回写。</summary>
        private void SyncLocalToggles()
        {
            _loading = true;
            try
            {
                if (TunModeToggle.IsOn != Global.Settings.TunMode)
                    TunModeToggle.IsOn = Global.Settings.TunMode;
                if (SystemProxyToggle.IsOn != Global.Settings.SystemProxy)
                    SystemProxyToggle.IsOn = Global.Settings.SystemProxy;
            }
            finally
            {
                _loading = false;
            }
        }

        private async void CoreSwitchButton_Click(object sender, RoutedEventArgs e)
        {
            CoreSwitchButton.IsEnabled = false;
            if (Global.clashController?.IsRunning == true)
                await CoreManager.StopCore();
            else
                await CoreManager.StartCore();
            Global.RunOnUI(() =>
            {
                UpdateCoreButton();
                CoreSwitchButton.IsEnabled = true;
            });
        }

        #region 网络增强

        private CancellationTokenSource _injectCts;

        /// <summary>
        /// 保存设置并注入当前订阅文件；内核运行中时重载使其立即生效。
        /// 连续拨动多个开关会在短暂延迟后合并成一次写入与重载。
        /// </summary>
        private async Task ApplyInjectionAsync()
        {
            Configuration.Save();

            _injectCts?.Cancel();
            var cts = new CancellationTokenSource();
            _injectCts = cts;
            try
            {
                await Task.Delay(300, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await Task.Run(() => CoreManager.ApplyProfileConfig());
        }

        private async void SnifferToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            Global.Settings.Sniffer = ((ToggleSwitch)sender).IsOn;
            await ApplyInjectionAsync();
        }

        private async void TcpConcurrentToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            Global.Settings.TcpConcurrent = ((ToggleSwitch)sender).IsOn;
            await ApplyInjectionAsync();
        }

        private async void UnifiedDelayToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            Global.Settings.UnifiedDelay = ((ToggleSwitch)sender).IsOn;
            await ApplyInjectionAsync();
        }

        private async void FindProcessModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            Global.Settings.FindProcessMode = ((ToggleSwitch)sender).IsOn ? "strict" : "off";
            await ApplyInjectionAsync();
        }

        private async void StoreSelectedToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            Global.Settings.StoreSelected = ((ToggleSwitch)sender).IsOn;
            await ApplyInjectionAsync();
        }

        private async void StoreFakeIpToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            Global.Settings.StoreFakeIp = ((ToggleSwitch)sender).IsOn;
            await ApplyInjectionAsync();
        }

        private async void GeoAutoUpdateToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool on = ((ToggleSwitch)sender).IsOn;
            Global.Settings.GeoAutoUpdate = on;
            GeoIntervalBox.IsEnabled = on;
            await ApplyInjectionAsync();
        }

        private async void GeoIntervalBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (int.TryParse(SelectedTag(GeoIntervalBox), out int hours))
            {
                Global.Settings.GeoUpdateInterval = hours;
                await ApplyInjectionAsync();
            }
        }

        /// <summary>日志等级属于运行时字段, PATCH 立即生效, 同时写入订阅以便重启后沿用。</summary>
        private async void LogLevelBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            string level = SelectedTag(LogLevelBox);
            if (string.IsNullOrEmpty(level) || level == Global.Settings.LogLevel)
                return;

            Global.Settings.LogLevel = level;
            await ChangeConfig("log-level", level);
            Configuration.Save();
            InjectToProfile();
        }

        /// <summary>代理模式同样走 PATCH, 写入订阅后重启仍保持用户选择的模式。</summary>
        private async void ModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            string mode = SelectedTag(ModeBox);
            if (string.IsNullOrEmpty(mode) || mode == Global.Settings.Mode)
                return;

            Global.Settings.Mode = mode;
            await ChangeConfig("mode", mode);
            Configuration.Save();
            InjectToProfile();
            Global.Refresh = true;
        }

        private static void InjectToProfile()
        {
            var selected = Global.Settings.Profile.FirstOrDefault(p => p.IsSelected);
            if (selected != null)
                ConfigInjector.Apply(selected.FileName);
        }

        private static string SelectedTag(ComboBox box)
        {
            if (box.SelectedItem is ComboBoxItem item)
                return item.Tag?.ToString() ?? item.Content?.ToString();
            return null;
        }

        private static void SelectCombo(ComboBox box, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            foreach (var item in box.Items)
            {
                if (item is not ComboBoxItem comboItem)
                    continue;

                string text = comboItem.Tag?.ToString() ?? comboItem.Content?.ToString();
                if (string.Equals(text, value, StringComparison.OrdinalIgnoreCase))
                {
                    box.SelectedItem = comboItem;
                    return;
                }
            }
        }

        #endregion

        #region 局域网访问控制 / 映射转发 / DNS 高级 / 自定义片段

        /// <summary>
        /// 文本类选项在失焦时保存并注入，避免每敲一个字符就写一次配置、重载一次内核。
        /// </summary>
        private async void TextOption_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loading) return;

            string tag = (sender as FrameworkElement)?.Tag?.ToString();
            string value = sender is PasswordBox passwordBox
                ? passwordBox.Password
                : (sender as TextBox)?.Text ?? string.Empty;

            switch (tag)
            {
                case "lan-user": Global.Settings.LanAuthUser = value.Trim(); break;
                case "lan-pass": Global.Settings.LanAuthPassword = value; break;
                case "lan-skip": Global.Settings.LanSkipAuth = value; break;
                case "lan-allow": Global.Settings.LanAllowedIps = value; break;
                case "lan-deny": Global.Settings.LanDisallowedIps = value; break;
                case "hosts": Global.Settings.CustomHosts = value; break;
                case "tunnels": Global.Settings.CustomTunnels = value; break;
                case "dns-nameserver": Global.Settings.DnsNameserver = value; break;
                case "dns-default": Global.Settings.DnsDefaultNameserver = value; break;
                case "dns-fallback": Global.Settings.DnsFallback = value; break;
                case "dns-policy": Global.Settings.DnsNameserverPolicy = value; break;
                case "dns-filter": Global.Settings.DnsFakeIpFilter = value; break;
                case "snippet": Global.Settings.CustomSnippet = value; break;
                default: return;
            }

            // 修改凭据后如果已经不全，开关随之回退，避免界面显示开启而配置里没有认证
            if (Global.Settings.LanAuthEnabled && !HasLanCredential())
            {
                Global.Settings.LanAuthEnabled = false;
                LanAuthToggle.IsOn = false;
            }

            await ApplyInjectionAsync();
        }

        private async void LanAuthToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;

            bool on = ((ToggleSwitch)sender).IsOn;
            if (on && !HasLanCredential())
            {
                // 没有凭据就写 authentication 会把代理端口锁死，直接回退并提示
                _loading = true;
                ((ToggleSwitch)sender).IsOn = false;
                _loading = false;
                await DialogHelper.ShowMessageAsync("Clash .NET Framework", Loc.LanAuthNeedCredential);
                return;
            }

            Global.Settings.LanAuthEnabled = on;
            await ApplyInjectionAsync();
        }

        private async void EnhancedModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;

            string tag = SelectedTag(EnhancedModeBox);
            string mode = tag == "keep" ? string.Empty : tag ?? string.Empty;
            if (string.Equals(mode, Global.Settings.DnsEnhancedMode, StringComparison.OrdinalIgnoreCase))
                return;

            Global.Settings.DnsEnhancedMode = mode;
            await ApplyInjectionAsync();
        }

        private bool HasLanCredential()
        {
            return !string.IsNullOrWhiteSpace(LanUserBox.Text)
                && !string.IsNullOrEmpty(LanPassBox.Password);
        }

        #endregion

        private static async Task ChangeConfig(string key, object value)
        {
            try
            {
                var dict = new Dictionary<string, dynamic> { { key, value } };
                await Global.clashClient.ChangeClashConfigs(dict);
            }
            catch
            {
            }
        }

        private void UWPLoopback_Click(object sender, RoutedEventArgs e)
        {
            Launch(Path.Combine(Global.ClashDotNetFrameworkDir, "bin", "EnableLoopback.exe"));
        }

        private void ClassicTheme_Click(object sender, RoutedEventArgs e) => SetTheme(ThemeType.Classic);
        private void ModernTheme_Click(object sender, RoutedEventArgs e) => SetTheme(ThemeType.Modern);
        private void DarkTheme_Click(object sender, RoutedEventArgs e) => SetTheme(ThemeType.Dark);

        private void SetTheme(ThemeType theme)
        {
            if (_loading || Global.Settings.Theme == theme) return;
            (Application.Current as App)?.ChangeTheme(theme);
        }

        private void EnglishLanguage_Click(object sender, RoutedEventArgs e) => SetLanguage(LanguageType.English);
        private void ChineseLanguage_Click(object sender, RoutedEventArgs e) => SetLanguage(LanguageType.Chinese);
        private void JapaneseLanguage_Click(object sender, RoutedEventArgs e) => SetLanguage(LanguageType.Japanese);

        private void SetLanguage(LanguageType language)
        {
            if (_loading || Global.Settings.Language == language) return;
            (Application.Current as App)?.ChangeLanguage(language);
        }

        private static void Launch(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
            }
            catch
            {
            }
        }
    }
}