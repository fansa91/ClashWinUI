using Clash.SDK.Models.Share;
using ClashDotNetFramework.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace ClashDotNetFramework.Pages
{
    public sealed partial class RulesPage : Page
    {
        /// <summary>本地化字符串表，供 x:Bind 编译期绑定使用</summary>
        public Localization Loc => Localization.Instance;

        private readonly ObservableCollection<ClashRuleData> _ruleItemCollection = new();

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
                Global.RunOnUI(() => _ruleItemCollection.Clear());
                try
                {
                    var response = await Global.clashClient.GetClashRules();
                    foreach (var rule in response.Rules)
                        Global.RunOnUI(() => _ruleItemCollection.Add(rule));
                }
                catch
                {
                }
                Global.RunOnUI(UpdateEmptyState);
            });
        }

        /// <summary>规则为空时给出提示，避免出现整页空白</summary>
        private void UpdateEmptyState()
        {
            EmptyStatePanel.Visibility = _ruleItemCollection.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>
        /// 语言切换后刷新由代码写入的文案。本页文案全部由 x:Bind 管理，这里保留空实现以统一接口。
        /// </summary>
        public void RefreshLocalizedText()
        {
        }
    }
}