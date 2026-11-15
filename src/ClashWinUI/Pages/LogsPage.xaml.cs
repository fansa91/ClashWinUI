using Clash.SDK.Models.Enums;
using Clash.SDK.Models.Events;
using ClashDotNetFramework.Models.Items;
using ClashDotNetFramework.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace ClashDotNetFramework.Pages
{
    public sealed partial class LogsPage : Page
    {
        private bool _isLogEnabled = true;
        private readonly ObservableCollection<LogItem> _logItemCollection = new();

        public LogsPage()
        {
            InitializeComponent();
            DataContext = Localization.Instance;
            LogItemListView.ItemsSource = _logItemCollection;
        }

        private void LogsPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (Global.clashClient != null)
                Global.clashClient.LoggingReceivedEvt += OnLogReceived;
            Task.Run(async () =>
            {
                try
                {
                    var config = await Global.clashClient.GetClashConfigs();
                    string mode = config.Mode switch
                    {
                        ModeType.Direct => Localization.Instance["Direct"],
                        ModeType.Global => Localization.Instance["Global"],
                        _ => Localization.Instance["Rule"]
                    };
                    Global.RunOnUI(() => CurrentMode.Text = $"{Localization.Instance["ProxyMode"]}: {mode}");
                }
                catch
                {
                }
            });
        }

        private void LogsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (Global.clashClient != null)
                Global.clashClient.LoggingReceivedEvt -= OnLogReceived;
        }

        private void OnLogReceived(object sender, LoggingEvtArgs e)
        {
            var log = e.Response;
            Global.RunOnUI(() =>
            {
                if (_logItemCollection.Count > Global.Settings.MaximumLog)
                    _logItemCollection.Clear();
                _logItemCollection.Add(new LogItem
                {
                    Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Level = log.Type.ToString(),
                    PayLoad = log.PayLoad
                });
            });
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _logItemCollection.Clear();
        }

        private void StatusButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isLogEnabled)
            {
                Global.clashClient.LoggingReceivedEvt -= OnLogReceived;
                StatusButton.Content = Localization.Instance["Start"];
            }
            else
            {
                Global.clashClient.LoggingReceivedEvt += OnLogReceived;
                StatusButton.Content = Localization.Instance["Stop"];
            }
            _isLogEnabled = !_isLogEnabled;
        }
    }
}