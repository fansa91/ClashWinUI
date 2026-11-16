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

            // TUN 与系统代理互斥：TUN 在内核内部接管本机流量，不再需要系统代理。
            // 两者同时开启时，浏览器仍会把请求发给 mixed-port —— 开了代理认证就会撞 407，
            // 而且系统代理的流量会绕过 TUN 的进程级分流。
            bool proxyWasOn = Global.Settings.SystemProxy;
            if (enable && proxyWasOn)
                SetSystemProxy(false);

            Global.Settings.TunMode = enable;
            Configuration.Save();

            var selected = Global.Settings.Profile.FirstOrDefault(p => p.IsSelected);
            if (selected != null)
                ConfigInjector.Apply(selected.FileName);

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
                    // 内核没起来就回滚，避免用户既没有 TUN、系统代理又被关掉而彻底断网
                    Global.Settings.TunMode = !enable;
                    Configuration.Save();
                    if (proxyWasOn && !Global.Settings.SystemProxy)
                        Global.RunOnUI(() => SetSystemProxy(true));
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

        /// <summary>
        /// 把客户端设置注入当前订阅文件；内核运行中时立即重载，让开关立刻生效。
        /// 返回是否成功写入并（在需要时）重载。
        /// </summary>
        public static bool ApplyProfileConfig()
        {
            var selected = Global.Settings.Profile.FirstOrDefault(p => p.IsSelected);
            if (selected == null)
                return false;

            ConfigInjector.Apply(selected.FileName);

            if (Global.clashClient == null || Global.clashController?.IsRunning != true)
                return true;

            try
            {
                Global.clashClient.ReloadClashConfig(Clash.SDK.Models.Enums.ConfigType.Path, false, selected.FileName).GetAwaiter().GetResult();
                Global.RunOnUI(() => (Global.MainWindow as MainWindow)?.InitEvent());
                return true;
            }
            catch (Exception e)
            {
                Logging.Error("重载配置失败：\n" + e.Message);
                return false;
            }
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
                        ConfigInjector.Apply(selected.FileName);
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
            {
                // reload 是整体替换，客户端设置必须在重载前重新注入
                ConfigInjector.Apply(selected.FileName);
                Global.clashClient.ReloadClashConfig(Clash.SDK.Models.Enums.ConfigType.Path, false, selected.FileName).GetAwaiter().GetResult();
            }
            Global.clashClient.GetClashTraffic();
            Global.clashClient.GetClashLog();
            Global.clashClient.GetClashConnection();
            Global.RunOnUI(() => (Global.MainWindow as MainWindow)?.InitEvent());
        }
    }
}