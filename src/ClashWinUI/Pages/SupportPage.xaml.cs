using ClashDotNetFramework.Controllers;
using ClashDotNetFramework.Models.Responses;
using ClashDotNetFramework.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace ClashDotNetFramework.Pages
{
    public sealed partial class SupportPage : Page
    {
        /// <summary>本地化字符串表，供 x:Bind 编译期绑定使用</summary>
        public Localization Loc => Localization.Instance;

        private string _coreVersion = "Unknown";

        public SupportPage()
        {
            InitializeComponent();
            DataContext = Localization.Instance;
        }

        private void SupportPage_Loaded(object sender, RoutedEventArgs e)
        {
            FillDiagnostics();
            Task.Run(GetAdvertisement);
        }

        /// <summary>
        /// 填充诊断信息。所有取值为best-effort：内核未启动或取版本失败时显示 Unknown，
        /// 绝不让诊断页影响主功能。
        /// </summary>
        private void FillDiagnostics()
        {
            AppVersionText.Text = $"v{UpdateChecker.Version}";
            DotNetVersionText.Text = Environment.Version.ToString();
            OSVersionText.Text = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";

            _coreVersion = Loc["Unknown"];
            CoreVersionText.Text = _coreVersion;
            Task.Run(() =>
            {
                try
                {
                    var response = Global.clashClient?.GetClashVersion().GetAwaiter().GetResult();
                    if (response != null && !string.IsNullOrWhiteSpace(response.Version))
                    {
                        _coreVersion = response.Version;
                        Global.RunOnUI(() => CoreVersionText.Text = _coreVersion);
                    }
                }
                catch
                {
                }
            });
        }

        private void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            string info =
                $"{Loc["AppVersion"]}: v{UpdateChecker.Version}\n" +
                $"{Loc["CoreVersion"]}: {_coreVersion}\n" +
                $"{Loc["DotNetVersion"]}: {Environment.Version}\n" +
                $"{Loc["OSVersion"]}: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";
            var package = new DataPackage();
            package.SetText(info);
            Clipboard.SetContent(package);
            if (sender is Button button)
                button.Content = Loc["Copied"];
        }

        private void OpenConfigDir_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string dir = ClashDotNetFramework.Utils.Utils.GetClashConfigDir();
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{dir}\"",
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }

        private void ViewLogs_Click(object sender, RoutedEventArgs e)
        {
            (Global.MainWindow as MainWindow)?.NavigateToTag("Logs");
        }

        private void GetAdvertisement()
        {
            try
            {
                var req = WebUtil.CreateRequest("https://raw.githubusercontent.com/CoelWu/ads/main/ads.json");
                var result = WebUtil.DownloadString(req, out var rep);
                if (rep.StatusCode == HttpStatusCode.OK)
                {
                    var response = JsonConvert.DeserializeObject<AdvertisementResponse>(result);
                    Global.RunOnUI(() =>
                    {
                        if (response.Airports.Count > 0)
                        {
                            MajorAdvertisement.Source = new BitmapImage(new Uri(response.Airports[0].Image));
                            MajorAdvertisement.Tag = response.Airports[0].Url;
                            MajorAdvertisement.Visibility = Visibility.Visible;
                        }
                        if (response.Airports.Count > 1)
                        {
                            MinorAdvertisement.Source = new BitmapImage(new Uri(response.Airports[1].Image));
                            MinorAdvertisement.Tag = response.Airports[1].Url;
                            MinorAdvertisement.Visibility = Visibility.Visible;
                        }
                        AdvertisementPanel.Visibility = response.Airports.Count > 0
                            ? Visibility.Visible
                            : Visibility.Collapsed;
                    });
                }
            }
            catch
            {
            }
        }

        private void Advertisement_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is Image image && image.Tag is string url && !string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// 语言切换后刷新由代码写入的文案。复制按钮的"已复制"提示恢复为原文案，
        /// 内核版本在语言切换时重新取一次本地化 Unknown。
        /// </summary>
        public void RefreshLocalizedText()
        {
            if (string.IsNullOrWhiteSpace(_coreVersion) || _coreVersion == "Unknown")
                CoreVersionText.Text = Loc["Unknown"];
        }
    }
}
