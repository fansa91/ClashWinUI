using ClashDotNetFramework.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClashDotNetFramework.Controllers
{
    public class ClashController : Guard
    { 
        public ClashController()
        {
            RedirectStd = true;
        }

        public override string MainFile { get; protected set; } = "mihomo.exe";

        public override string Name { get; } = "mihomo";

        private string BuildArguments(int port)
        {
            // mihomo 默认配置目录与 Clash 不同, 必须显式指定 -d
            string configDir = Utils.Utils.GetClashConfigDir();
            return $"-ext-ctl 127.0.0.1:{port} -d \"{configDir}\"";
        }

        public void Start()
        {
            int port = PortHelper.GetAvailablePort(PortType.TCP);
            Global.ClashControllerPort = port;
            // TUN 模式需要管理员权限
            Elevated = Global.Settings.TunMode;
            StartInstanceAuto(BuildArguments(port));
        }

        public void Start(int port)
        {
            // 端口未知（应用启动后从未启动过内核，Global.ClashControllerPort == 0）时退回自动选端口，
            // 避免用 0 端口拉起内核导致连接不上
            if (port <= 0)
            {
                Start();
                return;
            }

            PortHelper.CheckPort(Convert.ToUInt16(port), PortType.TCP);
            Global.ClashControllerPort = port;
            Elevated = Global.Settings.TunMode;
            StartInstanceAuto(BuildArguments(port));
        }

        public override void Stop()
        {
            StopInstance();
        }
    }
}
