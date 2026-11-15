using Clash.SDK;
using Clash.SDK.Models.Response;
using ClashDotNetFramework.Controllers;
using ClashDotNetFramework.Models.Enums;
using ClashDotNetFramework.Utils;
using Microsoft.Win32;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using WindowsProxy;

namespace ClashDotNetFramework.Pages
{
    public sealed partial class SettingsPage : Page
    {
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
            Task.Run(() => GetClashConfigs().GetAwaiter().GetResult());
        }

        private void UpdateChecker_NewVersionFound(object sender, EventArgs e)
        {
            Global.RunOnUI(() =>
            {
                UpdateButton.Visibility = Visibility.Visible;
                UpdateButton.Tag = UpdateChecker.LatestVersionUrl;
            });
        }

        private async Task GetClashConfigs()
        {
            try
            {
                _clashConfigs = await Global.clashClient.GetClashConfigs();
                Global.RunOnUI(() =>
                {
                    _loading = true;
                    Version.Text = $"v{UpdateChecker.Version}";
                    ControllerPort.Content = Convert.ToString(Global.ClashControllerPort);
                    MixedPort.Text = Convert.ToString(_clashConfigs.MixedPort);
                    LogLevel.Text = Convert.ToString(_clashConfigs.LogLevel);
                    AllowLAN.IsOn = _clashConfigs.AllowLan;
                    IPV6.IsOn = _clashConfigs.IPV6;
                    StartWithWindows.IsOn = Global.Settings.StartAfterSystemBoot;
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
                });
            }
            catch
            {
                _loading = false;
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
                    TunHelper.ApplyTun(selected.FileName);
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

        private void ResetAllSettings_Click(object sender, RoutedEventArgs e)
        {
            AllowLAN.IsOn = false;
            IPV6.IsOn = false;
            StartWithWindows.IsOn = false;
        }

        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            string url = ((Button)sender).Tag?.ToString();
            if (!string.IsNullOrWhiteSpace(url))
                Launch(url);
        }

        private void ControllerPort_Click(object sender, RoutedEventArgs e)
        {
            var package = new DataPackage();
            package.SetText($"http://127.0.0.1:{Global.ClashControllerPort}");
            Clipboard.SetContent(package);
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
            await ChangeConfig("ipv6", ((ToggleSwitch)sender).IsOn);
        }

        private async void AllowLAN_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            await ChangeConfig("allow-lan", ((ToggleSwitch)sender).IsOn);
        }

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