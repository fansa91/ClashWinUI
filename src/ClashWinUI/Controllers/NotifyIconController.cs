using Clash.SDK.Models.Enums;
using ClashDotNetFramework.Utils;
using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using WindowsProxy;

namespace ClashDotNetFramework.Controllers
{
    public class NotifyIconController
    {
        private TaskbarIcon _icon;
        private MenuFlyout _menu;
        private RadioMenuFlyoutItem _directItem;
        private RadioMenuFlyoutItem _ruleItem;
        private RadioMenuFlyoutItem _globalItem;
        private ToggleMenuFlyoutItem _systemProxyItem;
        private ToggleMenuFlyoutItem _tunModeItem;

        public void Start()
        {
            BuildMenu();

            _icon = new TaskbarIcon
            {
                ToolTipText = "ClashWinUI",
                ContextFlyout = _menu,
                LeftClickCommand = new RelayCommand(ShowMainWindow),
                MenuActivation = H.NotifyIcon.Core.PopupActivationMode.RightClick
            };
            SetIcon();
            _icon.ForceCreate();
        }

        public void Stop()
        {
            if (_icon == null)
                return;
            _icon.Dispose();
            _icon = null;
        }

        public void SetIcon()
        {
            if (_icon == null)
                return;
            string file = (Global.Settings.TunMode || Global.Settings.SystemProxy) ? "Proxy.ico" : "Normal.ico";
            string path = Path.Combine(Global.ClashDotNetFrameworkDir, "Assets", file);
            try
            {
                if (File.Exists(path))
                    _icon.Icon = new System.Drawing.Icon(path);
            }
            catch
            {
            }
        }

        private void BuildMenu()
        {
            _menu = new MenuFlyout();
            _menu.Opening += async (s, e) => await InitStatus();

            var dashboard = new MenuFlyoutItem { Text = "Dashboard" };
            dashboard.Click += (s, e) => ShowMainWindow();
            _menu.Items.Add(dashboard);
            _menu.Items.Add(new MenuFlyoutSeparator());

            var proxyMode = new MenuFlyoutSubItem { Text = "Proxy Mode" };
            _directItem = new RadioMenuFlyoutItem { Text = "Direct", GroupName = "ProxyMode" };
            _ruleItem = new RadioMenuFlyoutItem { Text = "Rule", GroupName = "ProxyMode" };
            _globalItem = new RadioMenuFlyoutItem { Text = "Global", GroupName = "ProxyMode" };
            _directItem.Click += (s, e) => ChangeMode("Direct");
            _ruleItem.Click += (s, e) => ChangeMode("Rule");
            _globalItem.Click += (s, e) => ChangeMode("Global");
            proxyMode.Items.Add(_directItem);
            proxyMode.Items.Add(_ruleItem);
            proxyMode.Items.Add(_globalItem);
            _menu.Items.Add(proxyMode);

            _systemProxyItem = new ToggleMenuFlyoutItem { Text = "System Proxy" };
            _systemProxyItem.Click += (s, e) => OnSystemProxy_Click();
            _menu.Items.Add(_systemProxyItem);

            _tunModeItem = new ToggleMenuFlyoutItem { Text = "TUN Mode" };
            _tunModeItem.Click += (s, e) => OnTunMode_Click();
            _menu.Items.Add(_tunModeItem);
            _menu.Items.Add(new MenuFlyoutSeparator());

            var openTerminal = new MenuFlyoutSubItem { Text = "Open Terminal With Proxy Set Up" };
            AddClickItem(openTerminal, "Cmd", () => OpenProcessWithProxySetUp("cmd.exe"));
            AddClickItem(openTerminal, "PowerShell", () => OpenProcessWithProxySetUp("powershell.exe"));
            AddClickItem(openTerminal, "Windows Terminal", () => OpenProcessWithProxySetUp("wt.exe"));
            AddClickItem(openTerminal, "Fluent Terminal", () => OpenProcessWithProxySetUp("flute.exe"));
            _menu.Items.Add(openTerminal);

            var copyCommand = new MenuFlyoutSubItem { Text = "Copy Proxy Setting Commands" };
            AddClickItem(copyCommand, "Cmd", () => CopyToClipboard($"set http_proxy=http://127.0.0.1:{Global.ClashMixedPort} & set https_proxy=http://127.0.0.1:{Global.ClashMixedPort}"));
            AddClickItem(copyCommand, "PowerShell", () => CopyToClipboard($"$Env:http_proxy=\"http://127.0.0.1:{Global.ClashMixedPort}\";$Env:https_proxy=\"http://127.0.0.1:{Global.ClashMixedPort}\""));
            _menu.Items.Add(copyCommand);

            var telegram = new MenuFlyoutSubItem { Text = "Set Telegram Proxy" };
            AddClickItem(telegram, "Set Socks5 Proxy To Telegram", () => LaunchUri($"tg://socks?server=127.0.0.1&port={Global.ClashMixedPort}"));
            _menu.Items.Add(telegram);
            _menu.Items.Add(new MenuFlyoutSeparator());

            var loopback = new MenuFlyoutItem { Text = "Enable UWP Loopback" };
            loopback.Click += (s, e) => Safe(() => Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Global.ClashDotNetFrameworkDir, "bin", "EnableLoopback.exe"),
                UseShellExecute = true
            }));
            _menu.Items.Add(loopback);
            _menu.Items.Add(new MenuFlyoutSeparator());

            var quit = new MenuFlyoutItem { Text = "Quit" };
            quit.Click += (s, e) => Application.Current.Exit();
            _menu.Items.Add(quit);
        }

        private static void AddClickItem(MenuFlyoutSubItem parent, string text, Action action)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += (s, e) => action();
            parent.Items.Add(item);
        }

        private static void Safe(Action action)
        {
            try { action(); } catch { }
        }

        private void ShowMainWindow()
        {
            var window = Global.MainWindow;
            if (window == null)
                return;
            window.AppWindow?.Show();
            window.Activate();
        }

        private async Task InitStatus()
        {
            try
            {
                var config = await Global.clashClient.GetClashConfigs();
                _directItem.IsChecked = config.Mode == ModeType.Direct;
                _ruleItem.IsChecked = config.Mode == ModeType.Rule;
                _globalItem.IsChecked = config.Mode == ModeType.Global;
            }
            catch
            {
            }
            _systemProxyItem.IsChecked = Global.Settings.SystemProxy;
            _tunModeItem.IsChecked = Global.Settings.TunMode;
        }

        private async void ChangeMode(string mode)
        {
            try
            {
                var dict = new System.Collections.Generic.Dictionary<string, dynamic> { { "mode", mode } };
                await Global.clashClient.ChangeClashConfigs(dict);
                // 记住选择, 重启或重载配置后仍是这个模式
                Global.Settings.Mode = mode;
                Configuration.Save();
                var selected = Global.Settings.Profile.FirstOrDefault(p => p.IsSelected);
                if (selected != null)
                    ConfigInjector.Apply(selected.FileName);
                Global.Refresh = true;
            }
            catch
            {
            }
        }

        private void OnSystemProxy_Click()
        {
            CoreManager.SetSystemProxy(!Global.Settings.SystemProxy);
            _systemProxyItem.IsChecked = Global.Settings.SystemProxy;
        }

        private async void OnTunMode_Click()
        {
            _tunModeItem.IsChecked = !Global.Settings.TunMode;
            await CoreManager.SetTunMode(!Global.Settings.TunMode);
            _tunModeItem.IsChecked = Global.Settings.TunMode;
            SetIcon();
        }

        private void OpenProcessWithProxySetUp(string name)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = name,
                    UseShellExecute = false,
                    WorkingDirectory = Utils.Utils.GetUserDir()
                };
                startInfo.EnvironmentVariables["http_proxy"] = $"http://127.0.0.1:{Global.ClashMixedPort}";
                startInfo.EnvironmentVariables["https_proxy"] = $"http://127.0.0.1:{Global.ClashMixedPort}";
                Process.Start(startInfo);
            }
            catch
            {
            }
        }

        private static void CopyToClipboard(string text)
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        }

        private static void LaunchUri(string uri)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = uri, UseShellExecute = true });
            }
            catch
            {
            }
        }
    }
}