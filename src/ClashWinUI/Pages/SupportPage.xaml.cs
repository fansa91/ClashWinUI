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
using System.Threading.Tasks;

namespace ClashDotNetFramework.Pages
{
    public sealed partial class SupportPage : Page
    {
        /// <summary>本地化字符串表，供 x:Bind 编译期绑定使用</summary>
        public Localization Loc => Localization.Instance;

        public SupportPage()
        {
            InitializeComponent();
            DataContext = Localization.Instance;
        }

        private void SupportPage_Loaded(object sender, RoutedEventArgs e)
        {
            Task.Run(GetAdvertisement);
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
                        // 只有确实有内容时才显示"广告"这一整块
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
        /// 语言切换后刷新由代码写入的文案。本页文案全部由 x:Bind 管理，这里保留空实现以统一接口。
        /// </summary>
        public void RefreshLocalizedText()
        {
        }
    }
}