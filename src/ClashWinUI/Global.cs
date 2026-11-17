using Clash.SDK;
using ClashDotNetFramework.Controllers;
using ClashDotNetFramework.Models;
using ClashDotNetFramework.Utils;
using Microsoft.UI.Dispatching;
using System;

namespace ClashDotNetFramework
{
    public class Global
    {
        public const string EOF = "\r\n";

        public static readonly string ClashDotNetFrameworkDir = AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>
        /// 当前是否为深色主题，供无法直接访问 ThemeResource 的代码 (转换器等) 判断配色
        /// </summary>
        public static bool IsDarkTheme { get; set; } = false;

        public static int ClashControllerPort = 0;

        public static int ClashMixedPort = 0;

        public static ClashClient clashClient = null;

        public static ClashController clashController = null;

        public static bool Refresh = true;

        /// <summary>
        /// 窗口最小化或隐藏时为 true：此时仅暂停界面刷新（图表 / 列表 / 文字），
        /// 内核、WebSocket 与数据采集照常运行；窗口恢复后自动继续。
        /// </summary>
        public static bool UiPaused = false;

        public static NotifyIconController iconController = new NotifyIconController();

        public static Setting Settings = new Setting();

        public static PageCollection Pages = new PageCollection();

        public static DispatcherQueue AppDispatcher { get; set; }

        public static Microsoft.UI.Xaml.Window MainWindow { get; set; }

        public static void RunOnUI(Action action)
        {
            if (AppDispatcher == null)
            {
                action();
                return;
            }
            AppDispatcher.TryEnqueue(() => action());
        }
    }
}