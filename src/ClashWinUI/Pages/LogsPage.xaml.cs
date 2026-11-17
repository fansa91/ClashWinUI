using Clash.SDK.Models.Enums;
using Clash.SDK.Models.Events;
using ClashDotNetFramework.Models.Items;
using ClashDotNetFramework.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace ClashDotNetFramework.Pages
{
    public sealed partial class LogsPage : Page
    {
        /// <summary>本地化字符串表，供 x:Bind 编译期绑定使用</summary>
        public Localization Loc => Localization.Instance;

        private bool _isLogEnabled = true;
        private readonly ObservableCollection<LogItem> _logItemCollection = new();
        private readonly List<LogItem> _allLogs = new();

        public LogsPage()
        {
            InitializeComponent();
            DataContext = Localization.Instance;
            LogItemListView.ItemsSource = _logItemCollection;
            _logItemCollection.CollectionChanged += (s, e) => UpdateEmptyState();
        }

        /// <summary>读取 ComboBoxItem 上的 Tag，未选中时回退到 All。</summary>
        private static string GetTag(ComboBox box)
            => (box.SelectedItem as ComboBoxItem)?.Tag as string ?? "All";

        /// <summary>按搜索词 + 等级重建列表，不修改原始数据。</summary>
        private void ApplyFilter()
        {
            // XAML 初始化期间 SelectedIndex="0" 会同步触发 SelectionChanged，
            // 此时后面的控件可能还没生成，直接访问会得到空引用。
            if (LogSearchBox == null || LogLevelFilter == null || AutoScrollCheck == null
                || LogCountText == null || LogItemListView == null
                || EmptyText == null || ClearEmptyFilterButton == null || ClearFilterButton == null)
                return;

            string keyword = LogSearchBox.Text?.Trim() ?? string.Empty;
            string level = GetTag(LogLevelFilter);

            var filtered = _allLogs.Where(log =>
            {
                if (level != "All" && !string.Equals(log.Level, level, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (keyword.Length == 0)
                    return true;
                return (log.PayLoad ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || (log.Level ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase);
            }).ToList();

            _logItemCollection.Clear();
            foreach (var log in filtered)
                _logItemCollection.Add(log);

            LogCountText.Text = string.Format(Loc["LogCountFormat"], filtered.Count);
            if (AutoScrollCheck.IsChecked == true && _logItemCollection.Count > 0)
                LogItemListView.ScrollIntoView(_logItemCollection[_logItemCollection.Count - 1]);
            UpdateEmptyState();
        }

        /// <summary>无日志与筛选无结果分别给出提示，避免出现整页空白。</summary>
        private void UpdateEmptyState()
        {
            if (LogSearchBox == null || LogLevelFilter == null)
                return;

            bool filtering = !string.IsNullOrWhiteSpace(LogSearchBox.Text)
                || GetTag(LogLevelFilter) != "All";

            EmptyStatePanel.Visibility = _logItemCollection.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            EmptyText.Text = _allLogs.Count == 0 ? Loc.NoLogs : Loc.NoMatch;
            ClearEmptyFilterButton.Visibility = filtering ? Visibility.Visible : Visibility.Collapsed;
            ClearFilterButton.Visibility = filtering ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LogsPage_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateEmptyState();
            UpdateStatusButton();
            LogCountText.Text = string.Format(Loc["LogCountFormat"], _allLogs.Count);
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

        /// <summary>
        /// 窗口最小化 / 隐藏时暂停列表刷新（阶段 5）：
        /// 本页无计时器，OnLogReceived 里的 Global.UiPaused 守卫负责跳过追加与重建；
        /// 页面自带的“暂停”按钮逻辑不受影响。
        /// </summary>
        public void SetUiPaused(bool paused)
        {
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

        /// <summary>暂停 / 继续按钮的文案由代码写入，切换语言时需要同步。</summary>
        private void UpdateStatusButton()
        {
            StatusButton.Content = _isLogEnabled ? Loc.Pause : Loc.Resume;
        }

        private void OnLogReceived(object sender, LoggingEvtArgs e)
        {
            var log = e.Response;
            Global.RunOnUI(() =>
            {
                // 最小化 / 隐藏时不再追加与重建列表；恢复后由下一条日志带起刷新
                if (Global.UiPaused)
                    return;

                _allLogs.Add(new LogItem
                {
                    Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Level = log.Type.ToString(),
                    PayLoad = log.PayLoad
                });
                int limit = Math.Max(Global.Settings.MaximumLog, 1);
                if (_allLogs.Count > limit)
                    _allLogs.RemoveRange(0, _allLogs.Count - limit);
                ApplyFilter();
            });
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _allLogs.Clear();
            ApplyFilter();
        }

        private void StatusButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isLogEnabled)
                Global.clashClient.LoggingReceivedEvt -= OnLogReceived;
            else
                Global.clashClient.LoggingReceivedEvt += OnLogReceived;
            _isLogEnabled = !_isLogEnabled;
            UpdateStatusButton();
        }

        private void LogSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
            => ApplyFilter();

        private void LogLevelFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => ApplyFilter();

        private void ClearFilterButton_Click(object sender, RoutedEventArgs e)
        {
            LogSearchBox.Text = string.Empty;
            LogLevelFilter.SelectedIndex = 0;
            ApplyFilter();
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileSavePicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(Global.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                picker.SuggestedFileName = $"clash-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
                picker.DefaultFileExtension = ".txt";
                picker.FileTypeChoices.Add(Loc.Export, new List<string> { ".txt" });

                var file = await picker.PickSaveFileAsync();
                if (file == null)
                    return;
                string text = string.Join(Environment.NewLine,
                    _allLogs.Select(log => $"{log.Time} [{log.Level}] {log.PayLoad}"));
                await FileIO.WriteTextAsync(file, text);
            }
            catch
            {
            }
        }

        /// <summary>
        /// 主题切换后重新绑定日志项，使等级徽章按新主题重新计算语义色。
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
        /// 语言切换后刷新由代码写入的文案（按钮、模式、计数与空状态）。
        /// </summary>
        public void RefreshLocalizedText()
        {
            UpdateStatusButton();
            UpdateModeText(_mode);
            LogCountText.Text = string.Format(Loc["LogCountFormat"], _allLogs.Count);
            UpdateEmptyState();
        }
    }
}
