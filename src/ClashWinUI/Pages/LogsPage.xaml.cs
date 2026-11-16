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
        /// <summary>本地化字符串表，供 x:Bind 编译期绑定使用</summary>
        public Localization Loc => Localization.Instance;

        private bool _isLogEnabled = true;
        private readonly ObservableCollection<LogItem> _logItemCollection = new();

        public LogsPage()
        {
            InitializeComponent();
            DataContext = Localization.Instance;
            LogItemListView.ItemsSource = _logItemCollection;
            // 集合变化时同步空状态提示（新增/清空都会走到这里）
            _logItemCollection.CollectionChanged += (s, e) => UpdateEmptyState();
        }

        /// <summary>日志为空时给出提示，避免出现整页空白</summary>
        private void UpdateEmptyState()
        {
            EmptyStatePanel.Visibility = _logItemCollection.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void LogsPage_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateEmptyState();
            UpdateStatusButton();
            if (Global.clashClient != null)
                Global.clashClient.LoggingReceivedEvt += OnLogReceived;
            Task.Run(async () =>
            {
                try
                {
                    var config = await Global.clashClient.GetClashConfigs();
                    Global.RunOnUI(() => UpdateModeText(config.Mode));
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

        /// <summary>当前代理模式，缓存下来供语言切换时重新格式化文案</summary>
        private ModeType _mode = ModeType.Rule;

        private void UpdateModeText(ModeType mode)
        {
            _mode = mode;
            string name = mode switch
            {
                ModeType.Direct => Localization.Instance["Direct"],
                ModeType.Global => Localization.Instance["Global"],
                _ => Localization.Instance["Rule"]
            };
            CurrentMode.Text = $"{Localization.Instance["ProxyMode"]}: {name}";
        }

        /// <summary>开始/停止按钮的文案由代码写入，切换语言时需要同步。</summary>
        private void UpdateStatusButton()
        {
            StatusButton.Content = Localization.Instance[_isLogEnabled ? "Stop" : "Start"];
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
            }
            else
            {
                Global.clashClient.LoggingReceivedEvt += OnLogReceived;
            }
            _isLogEnabled = !_isLogEnabled;
            UpdateStatusButton();
        }

        /// <summary>
        /// 主题切换后重新绑定日志项，使 LevelToColorConverter 根据新主题重新计算前景色。
        /// LogItem 未实现 INotifyPropertyChanged，需清空并重新添加以触发重新绑定。
        /// </summary>
        public void RefreshSemanticColors()
        {
            var items = _logItemCollection.ToList();
            _logItemCollection.Clear();
            foreach (var item in items)
                _logItemCollection.Add(item);
        }

        /// <summary>
        /// 语言切换后刷新由代码写入的文案（按钮与模式一行）。
        /// </summary>
        public void RefreshLocalizedText()
        {
            UpdateStatusButton();
            UpdateModeText(_mode);
        }
    }
}