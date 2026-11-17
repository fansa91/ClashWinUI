using ClashDotNetFramework.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClashDotNetFramework.Utils
{
    public class PageCollection
    {
        public OverviewPage overviewPage;

        public ProxiesPage proxiesPage;

        public ProfilesPage profilesPage;

        public RulesPage rulesPage;

        public ConnectionsPage connectionsPage;

        public LogsPage logsPage;

        public SettingsPage settingsPage;

        public SupportPage supportPage;

        public void Init()
        {
            overviewPage = new OverviewPage();
            proxiesPage = new ProxiesPage();
            profilesPage = new ProfilesPage();
            rulesPage = new RulesPage();
            connectionsPage = new ConnectionsPage();
            logsPage = new LogsPage();
            settingsPage = new SettingsPage();
            supportPage = new SupportPage();

            // 阶段 5：给所有页面里的卡片挂悬停轻抬动效。
            // 本项目 XAML 编译器不加载本程序集，样式里不能引用 Motion，只能在代码侧统一挂接；
            // 页面此刻已由 InitializeComponent 构建完毕，卡片（含派生样式）都在树里。
            foreach (var page in new Page[]
            {
                overviewPage, proxiesPage, profilesPage, rulesPage,
                connectionsPage, logsPage, settingsPage, supportPage
            })
            {
                Motion.AttachHoverToCards(page);
            }
        }

        /// <summary>
        /// 主题切换后通知各页面重新计算语义色
        /// </summary>
        public void RefreshSemanticColors()
        {
            overviewPage?.RefreshSemanticColors();
            proxiesPage?.RefreshSemanticColors();
            rulesPage?.RefreshSemanticColors();
            connectionsPage?.RefreshSemanticColors();
            logsPage?.RefreshSemanticColors();
        }

        /// <summary>
        /// 语言切换后通知各页面刷新「由代码写入」的文案。
        /// x:Bind(OneWay) 绑定的文案会随 Localization.PropertyChanged 自动更新，
        /// 但代码里直接赋值的 Text / Content 不在 x:Bind 管理范围内，必须手动刷新。
        /// 页面是常驻单例（通过 PageFrame.Content 切换），因此集中在这里分发比逐页订阅事件更可靠。
        /// </summary>
        public void RefreshLocalizedText()
        {
            overviewPage?.RefreshLocalizedText();
            proxiesPage?.RefreshLocalizedText();
            profilesPage?.RefreshLocalizedText();
            rulesPage?.RefreshLocalizedText();
            connectionsPage?.RefreshLocalizedText();
            logsPage?.RefreshLocalizedText();
            settingsPage?.RefreshLocalizedText();
            supportPage?.RefreshLocalizedText();
        }

        /// <summary>
        /// 窗口最小化 / 隐藏到托盘时暂停带持续数据流的页面的界面刷新（阶段 5）。
        /// 代理 / 规则 / 配置页没有持续推送，无需处理。
        /// </summary>
        public void SetUiPaused(bool paused)
        {
            overviewPage?.SetUiPaused(paused);
            connectionsPage?.SetUiPaused(paused);
            logsPage?.SetUiPaused(paused);
        }
    }
}
