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
    }
}