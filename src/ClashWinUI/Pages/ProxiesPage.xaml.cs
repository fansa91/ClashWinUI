using Clash.SDK.Extensions;
using Clash.SDK.Models.Enums;
using ClashDotNetFramework.Models.Items;
using ClashDotNetFramework.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
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

        /// <summary>从内核取得的全量分组数据（不受搜索条件影响）</summary>
        private readonly List<ProxyGroup> _allGroups = new();

        /// <summary>当前展示的分组（受搜索条件过滤）</summary>
        private readonly ObservableCollection<ProxyGroup> _groups = new();

        private readonly CollectionViewSource _collectionView;
        private readonly DispatcherTimer _filterTimer;
        private bool _suppressSelection;
        private bool _modeSynced;
        private string _selectedModeTag = "Rule";
        private string _filter = string.Empty;

        /// <summary>当前可见的节点数，缓存下来供语言切换时重新格式化文案</summary>
        private int _visibleNodeCount;

        /// <summary>
        /// 本地化字符串表，供 x:Bind 使用
        /// </summary>
        public Localization Loc => Localization.Instance;

        public ProxiesPage()
        {
            InitializeComponent();

            _collectionView = new CollectionViewSource { Source = _groups, IsSourceGrouped = true };
            ProxyGridView.ItemsSource = _collectionView.View;

            _filterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _filterTimer.Tick += (s, e) =>
            {
                _filterTimer.Stop();
                ApplyFilter();
            };
        }

        /// <summary>
        /// 模式切换按钮点击处理：仅点击的按钮保持选中，其余取消；在初始同步完成前不触发模式切换。
        /// </summary>
        private void ModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            _selectedModeTag = button.Tag as string ?? string.Empty;
            RefreshModeButtonSelection();

            if (!_modeSynced)
                return;

            switch (_selectedModeTag)
            {
                case "Direct":
                    SwitchMode(ModeType.Direct);
                    break;
                case "Global":
                    SwitchMode(ModeType.Global);
                    break;
                default:
                    SwitchMode(ModeType.Rule);
                    break;
            }
        }

        /// <summary>
        /// 刷新三个模式按钮的选中视觉状态（Button 无 IsChecked，需手动管理）。
        /// </summary>
        private void RefreshModeButtonSelection()
        {
            SetButtonSelected(RuleModeButton, _selectedModeTag == "Rule");
            SetButtonSelected(GlobalModeButton, _selectedModeTag == "Global");
            SetButtonSelected(DirectModeButton, _selectedModeTag == "Direct");
        }

        private static void SetButtonSelected(Button button, bool selected)
        {
            button.Background = selected
                ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
                : null;
        }

        private IEnumerable<ProxyItem> AllProxies => _allGroups.SelectMany(g => g);

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (Global.Refresh)
            {
                Global.Refresh = false;
                Task.Run(async () => await LoadAsync());
            }
            else
            {
                ApplyFilter();
                RefreshSemanticColors();
            }
        }

        private async Task LoadAsync()
        {
            await GetModeType();
            await GetProxies();
        }

        private async Task ChangeClashMode(ModeType mode)
        {
            try
            {
                var dict = new Dictionary<string, dynamic> { { "mode", mode.ToString() } };
                await Global.clashClient.ChangeClashConfigs(dict);
                // 记住选择, 重启或重载配置后仍是这个模式
                Global.Settings.Mode = mode.ToString().ToLowerInvariant();
                Configuration.Save();
                var selected = Global.Settings.Profile.FirstOrDefault(p => p.IsSelected);
                if (selected != null)
                    ConfigInjector.Apply(selected.FileName);
            }
            catch
            {
            }
        }

        private void SwitchMode(ModeType mode)
        {
            _mode = mode;
            Task.Run(async () =>
            {
                await ChangeClashMode(mode);
                await GetProxies();
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
                    _selectedModeTag = config.Mode == ModeType.Rule
                        ? "Rule"
                        : config.Mode == ModeType.Global
                            ? "Global"
                            : "Direct";
                    RefreshModeButtonSelection();
                    _modeSynced = true;
                });
            }
            catch
            {
                Global.RunOnUI(() =>
                {
                    RefreshModeButtonSelection();
                    _modeSynced = true;
                });
            }
        }

        private async Task GetProxies()
        {
            try
            {
                var proxies = await Global.clashClient.GetClashProxies();

                var loaded = new List<ProxyGroup>();
                if (_mode != ModeType.Direct)
                {
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
                        loaded.Add(proxyGroup);
                    }
                }

                Global.RunOnUI(() =>
                {
                    _allGroups.Clear();
                    _allGroups.AddRange(loaded);
                    ApplyFilter();
                });
            }
            catch
            {
                // 内核未就绪或订阅里没有代理组时也要刷新界面，否则会停留在一个没有空状态提示的空白页
                Global.RunOnUI(() =>
                {
                    _allGroups.Clear();
                    ApplyFilter();
                });
            }
        }

        /// <summary>
        /// 依据搜索关键字重新构建展示用的分组（元素实例复用，选中与延迟状态保持同步）
        /// </summary>
        private void ApplyFilter()
        {
            _groups.Clear();
            foreach (var group in _allGroups)
            {
                var filtered = new ProxyGroup { Name = group.Name };
                foreach (var proxy in group)
                {
                    if (MatchesFilter(proxy))
                        filtered.Add(proxy);
                }
                if (filtered.Count > 0)
                    _groups.Add(filtered);
            }

            var total = _groups.Sum(g => g.Count);
            _visibleNodeCount = total;
            NodeCountText.Text = string.Format(Loc.NodesCount, total);
            EmptyStatePanel.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private bool MatchesFilter(ProxyItem proxy)
        {
            if (string.IsNullOrEmpty(_filter))
                return true;
            return proxy.Name?.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void NodeSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
                return;

            _filter = sender.Text?.Trim() ?? string.Empty;
            _filterTimer.Stop();
            _filterTimer.Start();
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

        private async void ProxyGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelection)
                return;
            if (ProxyGridView.SelectedItem is not ProxyItem item)
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

        /// <summary>
        /// 主题切换后重新计算语义色（延迟徽章）
        /// </summary>
        public void RefreshSemanticColors()
        {
            foreach (var proxy in AllProxies)
                proxy.NotifyLatencyChange();
        }

        /// <summary>
        /// 语言切换后刷新由代码写入的文案（节点计数含格式化参数，x:Bind 不覆盖）。
        /// </summary>
        public void RefreshLocalizedText()
        {
            NodeCountText.Text = string.Format(Loc.NodesCount, _visibleNodeCount);
        }
    }
}
