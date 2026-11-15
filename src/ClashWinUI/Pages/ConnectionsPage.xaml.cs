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
        private readonly ObservableCollection<ConnectionItem> _connectionItemCollection = new();

        public ConnectionsPage()
        {
            InitializeComponent();
            DataContext = Localization.Instance;
            ConnectionItemListView.ItemsSource = _connectionItemCollection;
        }

        private void ConnectionsPage_Loaded(object sender, RoutedEventArgs e)
        {
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
                    TotalTraffic.Text = $"{Localization.Instance["Total"]}: ↑{UnitConverter.FormatBytes(response.UploadTotal)} ↓{UnitConverter.FormatBytes(response.DownloadTotal)}";
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
    }
}