using Clash.SDK.Extensions;
using Clash.SDK.Models.Enums;
using ClashDotNetFramework.Models.Items;
using ClashDotNetFramework.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace ClashDotNetFramework.Pages
{
    public class ProxyGroup : ObservableCollection<ProxyItem>
    {
        public string Name { get; set; }
    }

    public sealed partial class ProxiesPage : Page
    {
        private ModeType _mode = ModeType.Rule;
        private readonly ObservableCollection<ProxyGroup> _groups = new();
        private bool _suppressSelection;

        public ProxiesPage()
        {
            InitializeComponent();
            DataContext = Localization.Instance;

            var cvs = new CollectionViewSource { Source = _groups, IsSourceGrouped = true };
            ProxyListView.ItemsSource = cvs.View;
        }

        private IEnumerable<ProxyItem> AllProxies => _groups.SelectMany(g => g);

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (Global.Refresh)
            {
                Task.Run(async () => await GetModeType());
                Task.Run(async () =>
                {
                    ClearProxies();
                    await GetProxies();
                });
                Global.Refresh = false;
            }
            else
            {
                ColorUtil.GenerateRandomColor();
                foreach (var proxy in AllProxies)
                    proxy.NotifyBackgroundChange();
            }
        }

        private async Task ChangeClashMode(ModeType mode)
        {
            try
            {
                var dict = new Dictionary<string, dynamic> { { "mode", mode.ToString() } };
                await Global.clashClient.ChangeClashConfigs(dict);
            }
            catch
            {
            }
        }

        private void DirectButton_Checked(object sender, RoutedEventArgs e) => SwitchMode(ModeType.Direct);
        private void RuleButton_Checked(object sender, RoutedEventArgs e) => SwitchMode(ModeType.Rule);
        private void GlobalButton_Checked(object sender, RoutedEventArgs e) => SwitchMode(ModeType.Global);

        private void SwitchMode(ModeType mode)
        {
            Task.Run(() =>
            {
                _mode = mode;
                ClearProxies();
                ChangeClashMode(mode).GetAwaiter().GetResult();
                GetProxies().GetAwaiter().GetResult();
            });
        }

        private async Task GetModeType()
        {
            try
            {
                var config = await Global.clashClient.GetClashConfigs();
                _mode = config.Mode;
                Global.RunOnUI(() =>
                {
                    switch (config.Mode)
                    {
                        case ModeType.Direct: DirectButton.IsChecked = true; break;
                        case ModeType.Rule: RuleButton.IsChecked = true; break;
                        case ModeType.Global: GlobalButton.IsChecked = true; break;
                    }
                });
            }
            catch
            {
            }
        }

        private void ClearProxies(bool regenerateColor = true)
        {
            Global.RunOnUI(() => _groups.Clear());
            if (regenerateColor)
                ColorUtil.GenerateRandomColor();
        }

        private async Task GetProxies()
        {
            try
            {
                var proxies = await Global.clashClient.GetClashProxies();
                if (_mode == ModeType.Direct)
                    return;

                IEnumerable<string> groupNames = _mode == ModeType.Rule
                    ? proxies.Proxies.Where(p => p.Type.IsPolicyGroup() && p.Name != "GLOBAL").Select(p => p.Name)
                    : new[] { "GLOBAL" };

                foreach (var groupName in groupNames)
                {
                    var group = proxies.Proxies.FirstOrDefault(p => p.Name == groupName);
                    if (group?.All == null)
                        continue;

                    var proxyGroup = new ProxyGroup { Name = group.Name };
                    foreach (var proxyName in group.All)
                    {
                        var matched = proxies.Proxies.FirstOrDefault(p => p.Name == proxyName);
                        if (matched == null)
                            continue;
                        proxyGroup.Add(new ProxyItem
                        {
                            Name = proxyName,
                            Type = matched.Type.IsPolicyGroup() ? $"{matched.Type} - {matched.Now}" : matched.Type.ToString(),
                            Group = group.Name,
                            Latency = matched.History.Count > 0 ? matched.History.First().Delay : -2,
                            IsSelected = matched.Name == group.Now
                        });
                    }
                    Global.RunOnUI(() => _groups.Add(proxyGroup));
                }
            }
            catch
            {
            }
        }

        private async void TestButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string name)
                return;
            var item = AllProxies.FirstOrDefault(p => p.Name == name);
            if (item == null)
                return;
            try
            {
                var result = await Global.clashClient.GetClashProxyDelay(name);
                item.Latency = result.DelayLong <= 0 ? -1 : result.DelayLong;
            }
            catch
            {
                item.Latency = -1;
            }
        }

        private async void ProxyListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelection)
                return;
            if (ProxyListView.SelectedItem is not ProxyItem item)
                return;
            try
            {
                _suppressSelection = true;
                foreach (var p in AllProxies.Where(p => p.Group == item.Group))
                    p.IsSelected = false;
                item.IsSelected = true;
                await Global.clashClient.SwitchClashProxy(item.Group, item.Name);
            }
            catch
            {
            }
            finally
            {
                _suppressSelection = false;
            }
        }
    }
}