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
            });
        }
    }
}