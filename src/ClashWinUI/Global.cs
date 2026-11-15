using Clash.SDK;
using ClashDotNetFramework.Controllers;
using ClashDotNetFramework.Models;
using ClashDotNetFramework.Utils;
using Microsoft.UI.Dispatching;
using System;
using Windows.UI;

namespace ClashDotNetFramework
{
    public class Global
    {
        public const string EOF = "\r\n";

        public static readonly string ClashDotNetFrameworkDir = AppDomain.CurrentDomain.BaseDirectory;

        public static Color ProxyColor = Color.FromArgb((byte)Math.Round(0.9 * 255), 179, 179, 179);

        public static int ClashControllerPort = 0;

        public static int ClashMixedPort = 0;

        public static ClashClient clashClient = null;

        public static ClashController clashController = null;

        public static bool Refresh = true;

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