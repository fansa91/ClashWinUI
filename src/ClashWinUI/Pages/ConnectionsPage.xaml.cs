using Clash.SDK.Models.Events;
using ClashDotNetFramework.Dialogs;
using ClashDotNetFramework.Models.Items;
using ClashDotNetFramework.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace ClashDotNetFramework.Pages
{
    public sealed partial class ConnectionsPage : Page
    {
        /// <summary>本地化字符串表，供 x:Bind 编译期绑定使用</summary>
        public Localization Loc => Localization.Instance;

        private readonly ObservableCollection<ConnectionItem> _connectionItemCollection = new();

        /// <summary>累计流量，缓存下来供语言切换时重新格式化文案</summary>
        private long _uploadTotal;
        private long _downloadTotal;

        public ConnectionsPage()
        {
            InitializeComponent();
            DataContext = Localization.Instance;
            ConnectionItemListView.ItemsSource = _connectionItemCollection;
            // 集合变化时同步空状态提示
            _connectionItemCollection.CollectionChanged += (s, e) => UpdateEmptyState();
        }

        /// <summary>连接为空时给出提示，避免出现整页空白</summary>
        private void UpdateEmptyState()
        {
            EmptyStatePanel.Visibility = _connectionItemCollection.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>累计流量文案由代码拼接，语言切换后需要重新生成</summary>
        private void UpdateTotalTrafficText()
        {
            TotalTraffic.Text = $"{Localization.Instance["Total"]}: {Localization.Instance["Upload"]} {UnitConverter.FormatBytes(_uploadTotal)}  {Localization.Instance["Download"]} {UnitConverter.FormatBytes(_downloadTotal)}";
        }

        private void ConnectionsPage_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateEmptyState();
            if (Global.clashClient != null)
                Global.clashClient.ConnectionUpdatedEvt += OnConnectionUpdated;
        }

        private void ConnectionsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (Global.clashClient != null)
                Global.clashClient.ConnectionUpdatedEvt -= OnConnectionUpdated;
        }

        private void OnConnectionUpdated(object sender, ConnectionEvtArgs e)
        {
            var response = e.Response;
            Global.RunOnUI(() =>
            {
                try
                {
                    // 箭头字形在某些字体下不渲染，改用文字标签更易读
                    _uploadTotal = response.UploadTotal;
                    _downloadTotal = response.DownloadTotal;
                    UpdateTotalTrafficText();
                    _connectionItemCollection.Clear();
                    if (response.Connections == null)
                        return;
                    foreach (var connection in response.Connections)
                    {
                        string targetHost = (!string.IsNullOrWhiteSpace(connection.MetaData.Host) ? connection.MetaData.Host : connection.MetaData.DestinationIP) + ":" + connection.MetaData.DestinationPort;
                        string upload = UnitConverter.FormatBytes(connection.Upload);
                        string download = UnitConverter.FormatBytes(connection.Download);
                        _connectionItemCollection.Add(new ConnectionItem
                        {
                            UUID = connection.Id,
                            Network = connection.MetaData.Network.ToUpper(),
                            Type = connection.MetaData.Type,
                            Host = connection.MetaData.Host,
                            Source = $"{connection.MetaData.SourceIP}:{connection.MetaData.SourcePort}",
                            Destination = $"{connection.MetaData.DestinationIP}:{connection.MetaData.DestinationPort}",
                            TargetHost = targetHost,
                            Chains = connection.Chains,
                            FinalNode = connection.Chains?.FirstOrDefault(),
                            Rule = connection.Rule,
                            Time = TimeConverter.ParseTime(Convert.ToDateTime(connection.Start)),
                            Upload = upload,
                            Download = download,
                            Speed = $"↑{upload}/s ↓{download}/s"
                        });
                    }
                }
                catch
                {
                }
            });
        }

        private async void CloseAllButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await Global.clashClient.DisconnectAllConnections();
            }
            catch
            {
            }
        }

        private async void ConnectionItemListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is ConnectionItem item)
                await ConnectionInfoDialog.ShowAsync(item);
        }

        /// <summary>
        /// 主题切换后重新绑定连接项，使各绑定根据新主题重新计算语义色。
        /// ConnectionItem 未实现 INotifyPropertyChanged，需清空并重新添加以触发重新绑定。
        /// </summary>
        public void RefreshSemanticColors()
        {
            var items = _connectionItemCollection.ToList();
            _connectionItemCollection.Clear();
            foreach (var item in items)
                _connectionItemCollection.Add(item);
        }

        /// <summary>
        /// 语言切换后刷新由代码写入的文案（总流量一行的标签是拼接出来的）。
        /// </summary>
        public void RefreshLocalizedText()
        {
            UpdateTotalTrafficText();
        }
    }
}