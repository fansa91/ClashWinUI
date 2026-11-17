using Clash.SDK.Models.Share;
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
    public sealed partial class RulesPage : Page
    {
        /// <summary>本地化字符串表，供 x:Bind 编译期绑定使用</summary>
        public Localization Loc => Localization.Instance;

        private readonly ObservableCollection<ClashRuleData> _ruleItemCollection = new();
        private readonly List<ClashRuleData> _allRules = new();

        public RulesPage()
        {
            InitializeComponent();
            DataContext = Localization.Instance;
            RuleItemListView.ItemsSource = _ruleItemCollection;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            Task.Run(async () =>
            {
                try
                {
                    var response = await Global.clashClient.GetClashRules();
                    var rules = response.Rules.ToList();
                    Global.RunOnUI(() =>
                    {
                        _allRules.Clear();
                        _allRules.AddRange(rules);
                        ApplyFilter();
                    });
                }
                catch
                {
                    Global.RunOnUI(UpdateEmptyState);
                }
            });
        }

        /// <summary>读取 ComboBoxItem 上的 Tag，未选中时回退到 All。</summary>
        private static string GetTag(ComboBox box)
            => (box.SelectedItem as ComboBoxItem)?.Tag as string ?? "All";

        private static string ClassifyType(string type)
        {
            string value = (type ?? string.Empty).ToUpperInvariant();
            if (value.StartsWith("DOMAIN"))
                return "Domain";
            if (value.StartsWith("IP") || value.StartsWith("CIDR"))
                return "Ip";
            if (value.StartsWith("GEO"))
                return "Geo";
            return "Other";
        }

        private static string ClassifyPolicy(string proxy)
        {
            string value = (proxy ?? string.Empty).Trim();
            if (value.Equals("DIRECT", StringComparison.OrdinalIgnoreCase))
                return "Direct";
            if (value.Equals("REJECT", StringComparison.OrdinalIgnoreCase))
                return "Reject";
            return "Proxy";
        }

        /// <summary>按搜索词 + 类型 + 策略重建列表，不修改原始数据。</summary>
        private void ApplyFilter()
        {
            // XAML 初始化期间 SelectedIndex="0" 会同步触发 SelectionChanged，
            // 此时后面的控件可能还没生成，直接访问会得到空引用。
            if (RuleSearchBox == null || TypeFilterBox == null || PolicyFilterBox == null
                || RuleCountText == null || EmptyText == null
                || ClearEmptyFilterButton == null || ClearFilterButton == null)
                return;

            string keyword = RuleSearchBox.Text?.Trim() ?? string.Empty;
            string type = GetTag(TypeFilterBox);
            string policy = GetTag(PolicyFilterBox);

            var filtered = _allRules.Where(rule =>
            {
                if (type != "All" && ClassifyType(rule.Type.ToString()) != type)
                    return false;
                if (policy != "All" && ClassifyPolicy(rule.Proxy) != policy)
                    return false;
                if (keyword.Length == 0)
                    return true;
                return (rule.PayLoad ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || rule.Type.ToString().Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || (rule.Proxy ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase);
            }).ToList();

            _ruleItemCollection.Clear();
            foreach (var rule in filtered)
                _ruleItemCollection.Add(rule);

            RuleCountText.Text = string.Format(Loc["RuleCountFormat"], filtered.Count);
            UpdateEmptyState();
        }

        /// <summary>规则为空与筛选无结果分别给出提示，避免出现整页空白。</summary>
        private void UpdateEmptyState()
        {
            if (RuleSearchBox == null || TypeFilterBox == null || PolicyFilterBox == null
                || RuleCountText == null || EmptyText == null
                || ClearEmptyFilterButton == null || ClearFilterButton == null)
                return;

            bool filtering = !string.IsNullOrWhiteSpace(RuleSearchBox.Text)
                || GetTag(TypeFilterBox) != "All"
                || GetTag(PolicyFilterBox) != "All";

            EmptyStatePanel.Visibility = _ruleItemCollection.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            EmptyText.Text = _allRules.Count == 0 ? Loc.NoRules : Loc.NoMatch;
            ClearEmptyFilterButton.Visibility = filtering ? Visibility.Visible : Visibility.Collapsed;
            ClearFilterButton.Visibility = filtering ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RuleSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
            => ApplyFilter();

        private void FilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => ApplyFilter();

        private void ClearFilterButton_Click(object sender, RoutedEventArgs e)
        {
            RuleSearchBox.Text = string.Empty;
            TypeFilterBox.SelectedIndex = 0;
            PolicyFilterBox.SelectedIndex = 0;
            ApplyFilter();
        }

        /// <summary>主题切换后重新绑定规则项，使语义色标签按新主题重新计算。</summary>
        public void RefreshSemanticColors()
        {
            var items = _ruleItemCollection.ToList();
            _ruleItemCollection.Clear();
            foreach (var item in items)
                _ruleItemCollection.Add(item);
        }

        /// <summary>语言切换后刷新代码写入的文案（计数与空状态）。</summary>
        public void RefreshLocalizedText()
        {
            RuleCountText.Text = string.Format(Loc["RuleCountFormat"], _ruleItemCollection.Count);
            UpdateEmptyState();
        }
    }
}
