using System.Collections.Generic;
using System.IO;
using YamlDotNet.Serialization;

namespace ClashDotNetFramework.Utils
{
    public static class TunHelper
    {
        /// <summary>
        /// 根据当前 TUN 设置, 向配置文件注入或关闭 mihomo 内置 tun 段
        /// </summary>
        public static void ApplyTun(string profilePath)
        {
            if (string.IsNullOrEmpty(profilePath) || !File.Exists(profilePath))
                return;

            try
            {
                var deserializer = new DeserializerBuilder().Build();
                var root = deserializer.Deserialize<Dictionary<object, object>>(File.ReadAllText(profilePath))
                           ?? new Dictionary<object, object>();

                if (Global.Settings.TunMode)
                {
                    root["tun"] = new Dictionary<object, object>
                    {
                        ["enable"] = true,
                        ["stack"] = Global.Settings.TunStack,
                        ["dns-hijack"] = new List<object> { "any:53" },
                        ["auto-route"] = true,
                        ["auto-detect-interface"] = true
                    };

                    // TUN 需要 DNS 配合, 配置中没有 dns 段时补一个最小可用配置
                    if (!root.ContainsKey("dns"))
                    {
                        root["dns"] = new Dictionary<object, object>
                        {
                            ["enable"] = true,
                            ["listen"] = "0.0.0.0:1053",
                            ["enhanced-mode"] = "fake-ip",
                            ["fake-ip-range"] = "198.18.0.1/16",
                            ["nameserver"] = new List<object> { "223.5.5.5", "119.29.29.29" }
                        };
                    }
                }
                else if (root.TryGetValue("tun", out var tunObj) && tunObj is Dictionary<object, object> tun)
                {
                    tun["enable"] = false;
                }

                var serializer = new SerializerBuilder().Build();
                File.WriteAllText(profilePath, serializer.Serialize(root));
            }
            catch
            {
            }
        }
    }
}