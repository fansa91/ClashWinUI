using Clash.SDK.Models.Events;
using ClashDotNetFramework.Dialogs;
using ClashDotNetFramework.Models.Items;
using ClashDotNetFramework.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace ClashDotNetFramework.Pages
{
    public sealed partial class ConnectionsPage : Page
    {
        /// <summary>本地化字符串表，供 x:Bind 编译期绑定使用</summary>
        public Localization Loc => Localization.Instance;

        private readonly ObservableCollection<ConnectionItem> _connectionItemCollection = new();
        private readonly List<ConnectionItem> _allConnections = new();

        /// <summary>累计流量，缓存下来供语言切换时重新格式化文案</summary>
        private long _uploadTotal;
        private long _downloadTotal;

        public ConnectionsPage()
        {
            InitializeComponent();
            DataContext = Localization.Instance;
            ConnectionItemListView.ItemsSource = _connectionItemCollection;
            _connectionItemCollection.CollectionChanged += (s, e) => UpdateEmptyState();
        }

        /// <summary>读取 ComboBoxItem 上的 Tag，未选中时回退到 All。</summary>
        private static string GetTag(ComboBox box)
            => (box.SelectedItem as ComboBoxItem)?.Tag as string ?? "All";

        /// <summary>按搜索词 + 协议重建列表，不修改原始数据。</summary>
        private void ApplyFilter()
        {
            // XAML 初始化期间 SelectedIndex="0" 会同步触发 SelectionChanged，
            // 此时后面的控件可能还没生成，直接访问会得到空引用。
            if (ConnectionSearchBox == null || ConnectionProtocolFilter == null
                || ConnectionCountText == null || TotalTraffic == null
                || EmptyText == null || ClearEmptyFilterButton == null || ClearFilterButton == null)
                return;

            string keyword = ConnectionSearchBox.Text?.Trim() ?? string.Empty;
            string protocol = GetTag(ConnectionProtocolFilter);

            var filtered = _allConnections.Where(c =>
            {
                if (protocol != "All" && !string.Equals(c.Network, protocol, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (keyword.Length == 0)
                    return true;
                return (c.TargetHost ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || (c.Rule ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || (c.FinalNode ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase);
            }).ToList();

            _connectionItemCollection.Clear();
            foreach (var connection in filtered)
                _connectionItemCollection.Add(connection);

            ConnectionCountText.Text = string.Format(Loc["ConnectionCountFormat"], filtered.Count);
            UpdateTotalTrafficText();
            UpdateEmptyState();
        }

        /// <summary>无连接与筛选无结果分别给出提示，避免出现整页空白。</summary>
        private void UpdateEmptyState()
        {
            if (ConnectionSearchBox == null || ConnectionProtocolFilter == null
                || ConnectionCountText == null || TotalTraffic == null
                || EmptyText == null || ClearEmptyFilterButton == null || ClearFilterButton == null)
                return;

            bool filtering = !string.IsNullOrWhiteSpace(ConnectionSearchBox.Text)
                || GetTag(ConnectionProtocolFilter) != "All";

            EmptyStatePanel.Visibility = _connectionItemCollection.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            EmptyText.Text = _allConnections.Count == 0 ? Loc.NoConnections : Loc.NoMatch;
            ClearEmptyFilterButton.Visibility = filtering ? Visibility.Visible : Visibility.Collapsed;
            ClearFilterButton.Visibility = filtering ? Visibility.Visible : Visibility.Collapsed;
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
                    _uploadTotal = response.UploadTotal;
                    _downloadTotal = response.DownloadTotal;
                    var connections = new List<ConnectionItem>();
                    if (response.Connections != null)
                    {
                        foreach (var connection in response.Connections)
                        {
                            string targetHost = (!string.IsNullOrWhiteSpace(connection.MetaData.Host) ? connection.MetaData.Host : connection.MetaData.DestinationIP) + ":" + connection.MetaData.DestinationPort;
                            string upload = UnitConverter.FormatBytes(connection.Upload);
                            string download = UnitConverter.FormatBytes(connection.Download);
                            connections.Add(new ConnectionItem
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
                    _allConnections.Clear();
                    _allConnections.AddRange(connections);
                    ApplyFilter();
                }
                catch
                {
                }
            });
        }

        private async void CloseAllButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = Loc.CloseAll,
                Content = Loc["ConfirmCloseAll"],
                PrimaryButtonText = Loc.CloseAll,
                CloseButtonText = Loc["Cancel"],
                XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            try
            {
                await Global.clashClient.DisconnectAllConnections();
            }
            catch
            {
            }
        }

        private async void CloseConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string id || string.IsNullOrWhiteSpace(id))
                return;
            try
            {
                await Global.clashClient.DisconnectConnection(id);
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

        private void ConnectionSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
            => ApplyFilter();

        private void ConnectionProtocolFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => ApplyFilter();

        private void ClearFilterButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectionSearchBox.Text = string.Empty;
            ConnectionProtocolFilter.SelectedIndex = 0;
            ApplyFilter();
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
        /// 语言切换后刷新由代码写入的文案（总流量、计数与空状态）。
        /// </summary>
        public void RefreshLocalizedText()
        {
            UpdateTotalTrafficText();
            ConnectionCountText.Text = string.Format(Loc["ConnectionCountFormat"], _connectionItemCollection.Count);
            UpdateEmptyState();
        }
    }
}
