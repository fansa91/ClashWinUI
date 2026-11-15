using Clash.SDK;
using ClashDotNetFramework.Utils;
using System;
using System.Linq;
using System.Threading.Tasks;
using WindowsProxy;

namespace ClashDotNetFramework.Controllers
{
    /// <summary>
    /// 统一管理内核重启与重连, 供 TUN 模式切换等场景使用
    /// </summary>
    public static class CoreManager
    {
        public static async Task SetTunMode(bool enable)
        {
            if (Global.Settings.TunMode == enable)
                return;

            Global.Settings.TunMode = enable;
            Configuration.Save();

            var selected = Global.Settings.Profile.FirstOrDefault(p => p.IsSelected);
            if (selected != null)
                TunHelper.ApplyTun(selected.FileName);

            await Task.Run(() =>
            {
                try
                {
                    Global.clashController?.Stop();
                    // 依据 TunMode 决定是否提权启动 (TUN 需要管理员权限)
                    Global.clashController.Start(Global.ClashControllerPort);
                    Reconnect();
                }
                catch (Exception e)
                {
                    Logging.Error("切换 TUN 模式失败：\n" + e.Message);
                }
            });
        }

        public static void SetSystemProxy(bool enable)
        {
            if (enable)
            {
                using var service = new ProxyService
                {
                    Server = $"127.0.0.1:{Global.ClashMixedPort}",
                    Bypass = string.Join(@";", ProxyService.LanIp)
                };
                service.Global();
            }
            else
            {
                using var service = new ProxyService();
                service.Direct();
            }
            Global.Settings.SystemProxy = enable;
            Configuration.Save();
            Global.iconController.SetIcon();
        }

        public static async Task StartCore()
        {
            if (Global.clashController?.IsRunning == true)
                return;

            await Task.Run(() =>
            {
                try
                {
                    var selected = Global.Settings.Profile.FirstOrDefault(p => p.IsSelected);
                    if (selected != null)
                        TunHelper.ApplyTun(selected.FileName);
                    Global.clashController?.Start(Global.ClashControllerPort);
                    Reconnect();
                }
                catch (Exception e)
                {
                    Logging.Error("启动内核失败：\n" + e.Message);
                }
            });
        }

        public static async Task StopCore()
        {
            await Task.Run(() =>
            {
                try
                {
                    Global.clashController?.Stop();
                }
                catch (Exception e)
                {
                    Logging.Error("停止内核失败：\n" + e.Message);
                }
            });
        }

        private static void Reconnect()
        {
            Global.clashClient = new ClashClient(Global.ClashControllerPort);
            bool connected = false;
            int tryTimes = 0;
            while (!connected && tryTimes < 7)
            {
                connected = Global.clashClient.GetClashStatus().GetAwaiter().GetResult();
                if (connected)
                    break;
                tryTimes++;
                Task.Delay(1000).Wait();
            }
            if (!connected)
                return;

            var selected = Global.Settings.Profile.FirstOrDefault(p => p.IsSelected);
            if (selected != null)
                Global.clashClient.ReloadClashConfig(Clash.SDK.Models.Enums.ConfigType.Path, false, selected.FileName).GetAwaiter().GetResult();
            Global.clashClient.GetClashTraffic();
            Global.clashClient.GetClashLog();
            Global.clashClient.GetClashConnection();
            Global.RunOnUI(() => (Global.MainWindow as MainWindow)?.InitEvent());
        }
    }
}