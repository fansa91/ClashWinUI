using Clash.SDK.Extensions;
using Clash.SDK.Models.Enums;
using ClashDotNetFramework.Models.Items;
using ClashDotNetFramework.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace ClashDotNetFramework.Pages
{
    public sealed partial class ProxiesPage : Page
    {
        private ModeType _mode = ModeType.Rule;

        /// <summary>内核返回的策略组（不受搜索影响）</summary>
        private readonly List<ProxyGroupItem> _allGroups = new();

        /// <summary>左侧展示的策略组（受搜索影响）</summary>
        private readonly ObservableCollection<ProxyGroupItem> _groups = new();

        /// <summary>右侧展示的当前组成员（受筛选影响）</summary>
        private readonly ObservableCollection<ProxyItem> _members = new();

        private readonly DispatcherTimer _filterTimer;
        private ProxyGroupItem _selectedGroup;
        private bool _suppressGroupSelection;
        private bool _suppressMemberSelection;
        private bool _modeSynced;
        private string _selectedModeTag = "Rule";
        private string _filter = string.Empty;
        private string _nodeFilter = "All";
        private bool _sortByLatency;
        private int _visibleGroupCount;
        private int _visibleMemberCount;

        public Localization Loc => Localization.Instance;

        public ProxiesPage()
        {
            InitializeComponent();

            GroupListView.ItemsSource = _groups;
            ProxyGridView.ItemsSource = _members;

            _filterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _filterTimer.Tick += (s, e) =>
            {
                _filterTimer.Stop();
                ApplyFilter();
            };

            RefreshFilterButtonSelection();
        }

        /// <summary>上次应用的布局状态，避免同一宽度下重复切换。</summary>
        private string _layoutState;

        /// <summary>
        /// 双栏在窄内容区会挤成两列各 300px，切换单栏堆叠。
        /// 用内容区实测宽度判断：导航折叠会让内容区突然宽出约 180px。
        /// </summary>
        private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double width = e.NewSize.Width;
            if (width <= 0)
                return;

            string state = width >= 760 ? "WideProxies" : "NarrowProxies";
            if (state == _layoutState)
                return;

            _layoutState = state;
            VisualStateManager.GoToState(this, state, false);
        }

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

        private void RefreshModeButtonSelection()
        {
            if (RuleModeButton == null || GlobalModeButton == null || DirectModeButton == null)
                return;
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

        private async Task ChangeClashMode(ModeType mode)
        {
            try
            {
                var dict = new Dictionary<string, dynamic> { { "mode", mode.ToString() } };
                await Global.clashClient.ChangeClashConfigs(dict);
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

        /// <summary>
        /// 按官方 proxy-groups 语义加载：type.IsPolicyGroup() 的条目才是策略组，
        /// 每个组的 all 列表是组成员（可能是节点，也可能是子策略组）。
        /// </summary>
        private async Task GetProxies()
        {
            try
            {
                var proxies = await Global.clashClient.GetClashProxies();
                var loaded = new List<ProxyGroupItem>();

                if (proxies?.Proxies != null)
                {
                    foreach (var groupData in proxies.Proxies.Where(p => p.Type.IsPolicyGroup()))
                    {
                        var group = new ProxyGroupItem
                        {
                            Name = groupData.Name,
                            Type = groupData.Type,
                            Now = groupData.Now ?? ""
                        };

                        if (groupData.All != null)
                        {
                            foreach (var proxyName in groupData.All)
                            {
                                var matched = proxies.Proxies.FirstOrDefault(p => p.Name == proxyName);
                                if (matched == null)
                                    continue;

                                group.Members.Add(new ProxyItem
                                {
                                    Name = proxyName,
                                    Type = matched.Type.IsPolicyGroup()
                                        ? $"{matched.Type} · {matched.Now}"
                                        : matched.Type.ToString(),
                                    Group = group.Name,
                                    Latency = matched.History != null && matched.History.Count > 0
                                        ? matched.History.First().Delay
                                        : -2,
                                    IsSelected = matched.Name == group.Now
                                });
                            }
                        }

                        group.RefreshHealth();
                        group.RefreshLocalized();
                        group.RefreshColors();
                        loaded.Add(group);
                    }
                }

                Global.RunOnUI(() =>
                {
                    string previousName = _selectedGroup?.Name;

                    _allGroups.Clear();
                    _allGroups.AddRange(loaded);

                    _selectedGroup = null;
                    if (!string.IsNullOrEmpty(previousName))
                        _selectedGroup = _allGroups.FirstOrDefault(g => g.Name == previousName);
                    _selectedGroup ??= _allGroups.FirstOrDefault();

                    ApplyFilter();
                    RefreshSemanticColors();
                });
            }
            catch
            {
                Global.RunOnUI(() =>
                {
                    _selectedGroup = null;
                    _allGroups.Clear();
                    ApplyFilter();
                });
            }
        }

        /// <summary>重建左侧组列表与右侧成员列表。</summary>
        private void ApplyFilter()
        {
            // XAML 初始化期间可能早于控件生成，必须全部做空值保护
            if (GroupCountText == null || GroupBadgeText == null || EmptyStatePanel == null)
                return;
            if (SelectedGroupNameText == null || SelectedGroupSummaryText == null)
                return;
            if (SelectedGroupTypeBadge == null || SelectedGroupTypeText == null)
                return;

            _groups.Clear();
            foreach (var group in _allGroups)
            {
                bool groupMatches = string.IsNullOrEmpty(_filter)
                    || group.Name?.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0;
                if (groupMatches)
                    _groups.Add(group);
            }

            _visibleGroupCount = _groups.Count;
            GroupCountText.Text = string.Format(Loc.GroupCountFormat, _visibleGroupCount);
            GroupBadgeText.Text = _visibleGroupCount.ToString();

            // 保持选中组在过滤后仍可见
            if (_selectedGroup != null && !_groups.Contains(_selectedGroup))
            {
                _suppressGroupSelection = true;
                try { GroupListView.SelectedItem = null; }
                finally { _suppressGroupSelection = false; }
            }

            RefreshMemberList();
            EmptyStatePanel.Visibility = _allGroups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>按当前选中组与筛选条件重建成员列表。</summary>
        private void RefreshMemberList()
        {
            if (ProxyGridView == null || SelectedGroupNameText == null)
                return;

            _members.Clear();

            if (_selectedGroup == null)
            {
                SelectedGroupNameText.Text = "-";
                SelectedGroupSummaryText.Text = string.Empty;
                SelectedGroupTypeText.Text = string.Empty;
                SelectedGroupTypeBadge.Background = null;
                _visibleMemberCount = 0;
                return;
            }

            SelectedGroupNameText.Text = _selectedGroup.Name;
            SelectedGroupSummaryText.Text = _selectedGroup.Summary;
            SelectedGroupTypeText.Text = _selectedGroup.TypeLabel;
            SelectedGroupTypeBadge.Background = _selectedGroup.TypeBackground;
            SelectedGroupTypeText.Foreground = _selectedGroup.TypeBrush;

            var matches = _selectedGroup.Members.Where(MatchesFilter);
            if (_sortByLatency)
                matches = matches.OrderBy(p => p.Latency < 0 ? long.MaxValue : p.Latency);

            foreach (var member in matches)
                _members.Add(member);

            _visibleMemberCount = _members.Count;
        }

        private bool MatchesFilter(ProxyItem proxy)
        {
            switch (_nodeFilter)
            {
                case "Available" when proxy.Latency <= 0:
                    return false;
                case "Untested" when proxy.Latency != -2:
                    return false;
            }

            if (string.IsNullOrEmpty(_filter))
                return true;
            return proxy.Name?.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void GroupListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressGroupSelection)
                return;
            if (GroupListView?.SelectedItem is not ProxyGroupItem group)
                return;

            _selectedGroup = group;
            _suppressMemberSelection = true;
            try { ProxyGridView.SelectedItem = null; }
            finally { _suppressMemberSelection = false; }

            RefreshMemberList();
        }

        private async void ProxyGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressMemberSelection)
                return;
            if (ProxyGridView?.SelectedItem is not ProxyItem item)
                return;
            if (_selectedGroup == null)
                return;

            if (!_selectedGroup.IsSelectable)
            {
                // url-test / fallback / load-balance / relay 由内核自动选择，不允许手动切换
                _suppressMemberSelection = true;
                try { ProxyGridView.SelectedItem = null; }
                finally { _suppressMemberSelection = false; }
                return;
            }

            try
            {
                _suppressMemberSelection = true;
                foreach (var p in _selectedGroup.Members.Where(p => p.Group == item.Group))
                    p.IsSelected = false;
                item.IsSelected = true;

                _selectedGroup.Now = item.Name;
                await Global.clashClient.SwitchClashProxy(item.Group, item.Name);
                SelectedGroupSummaryText.Text = _selectedGroup.Summary;
            }
            catch
            {
            }
            finally
            {
                _suppressMemberSelection = false;
            }
        }

        private void FilterButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string tag)
                return;
            _nodeFilter = tag;
            RefreshFilterButtonSelection();
            RefreshMemberList();
        }

        private void RefreshFilterButtonSelection()
        {
            if (FilterAllButton == null || FilterAvailableButton == null
                || FilterUntestedButton == null || SortLatencyButton == null)
                return;
            SetButtonSelected(FilterAllButton, _nodeFilter == "All");
            SetButtonSelected(FilterAvailableButton, _nodeFilter == "Available");
            SetButtonSelected(FilterUntestedButton, _nodeFilter == "Untested");
            SetButtonSelected(SortLatencyButton, _sortByLatency);
        }

        private void SortLatencyButton_Click(object sender, RoutedEventArgs e)
        {
            _sortByLatency = !_sortByLatency;
            RefreshFilterButtonSelection();
            RefreshMemberList();
        }

        private void GoToProfiles_Click(object sender, RoutedEventArgs e)
        {
            (Global.MainWindow as MainWindow)?.NavigateToTag("Profiles");
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
            var item = _selectedGroup?.Members.FirstOrDefault(p => p.Name == name);
            if (item == null)
                return;

            try
            {
                var result = await Global.clashClient.GetClashProxyDelay(name);
                item.Latency = result.DelayLong <= 0 ? -1 : result.DelayLong;
                _selectedGroup.RefreshHealth();
                SelectedGroupSummaryText.Text = _selectedGroup.Summary;
            }
            catch
            {
                item.Latency = -1;
                _selectedGroup.RefreshHealth();
                if (SelectedGroupSummaryText != null)
                    SelectedGroupSummaryText.Text = _selectedGroup.Summary;
            }
        }

        /// <summary>主题切换后重新计算语义色（策略组类型徽章 + 延迟徽章）。</summary>
        public void RefreshSemanticColors()
        {
            foreach (var group in _allGroups)
            {
                group.RefreshColors();
                foreach (var proxy in group.Members)
                    proxy.NotifyLatencyChange();
            }
            if (_selectedGroup != null && SelectedGroupTypeBadge != null)
            {
                SelectedGroupTypeBadge.Background = _selectedGroup.TypeBackground;
                SelectedGroupTypeText.Foreground = _selectedGroup.TypeBrush;
            }

            // 分段按钮与筛选按钮的选中底色是代码写入的 Brush 实例，
            // 换主题后不会自动重解析，必须重新取一次当前调色板的强调色。
            RefreshModeButtonSelection();
            RefreshFilterButtonSelection();
        }

        /// <summary>语言切换后刷新代码写入的文案。</summary>
        public void RefreshLocalizedText()
        {
            if (GroupCountText != null)
                GroupCountText.Text = string.Format(Loc.GroupCountFormat, _visibleGroupCount);
            foreach (var group in _allGroups)
                group.RefreshLocalized();
            if (_selectedGroup != null && SelectedGroupSummaryText != null)
            {
                SelectedGroupSummaryText.Text = _selectedGroup.Summary;
                SelectedGroupTypeText.Text = _selectedGroup.TypeLabel;
            }
        }
    }
}
