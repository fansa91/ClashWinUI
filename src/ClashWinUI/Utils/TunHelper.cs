using System.Collections.Generic;

namespace ClashDotNetFramework.Utils
{
    /// <summary>
    /// 按 mihomo 官方格式维护配置里的 tun / dns 段。
    ///
    /// 参考：
    ///   https://wiki.metacubex.one/config/inbound/tun/   （TUN 字段说明）
    ///   https://wiki.metacubex.one/example/conf/          （官方快捷配置，tun + dns 组合）
    ///
    /// 只做行级文本替换，不再用 YAML 反序列化后重写整个订阅文件：
    /// 订阅里常见的注释、YAML 锚点、rule-provider、多行字符串在整份重写时会被破坏。
    /// 具体编辑原语在 <see cref="YamlText"/>，这里只负责 tun / dns 的语义。
    ///
    /// Windows 相关官方约束：
    ///   - stack 默认/推荐 mips；开启系统防火墙时 system 与 mixed 协议栈无法使用（需在 Windows 安全中心放行内核）
    ///   - 启用 auto-route 时 Windows 的 dns-mode 默认即为 hijack，而 hijack 必须配合 strict-route
    ///   - strict-route 通过防火墙规则阻止 Windows 多宿主 DNS 解析造成的泄露
    ///   - dns-hijack 需同时覆盖 UDP/53 与 TCP/53；auto-redirect 仅支持 Linux，不写入
    ///   - 运行时还需要管理员权限（ClashController 已按 TunMode 提权）以及与内核同目录的 wintun.dll
    /// </summary>
    public static class TunHelper
    {
        /// <summary>
        /// 官方快捷配置中的 dns 段（Windows 收敛版）。
        /// TUN 下 DNS 查询由 dns-hijack 导入内部 DNS 模块，因此不监听端口、不写 listen。
        /// fake-ip-filter 排除局域网域名与 Windows 网络连通性检测域名，避免返回 fake-ip。
        /// </summary>
        private static readonly string[] DnsBlock =
        {
            "dns:",
            "  enable: true",
            "  enhanced-mode: fake-ip",
            "  fake-ip-range: 198.18.0.1/16",
            "  fake-ip-filter:",
            "    - \"*.lan\"",
            "    - \"*.local\"",
            "    - \"+.msftconnecttest.com\"",
            "    - \"+.msftncsi.com\"",
            "  default-nameserver:",
            "    - 223.5.5.5",
            "    - 119.29.29.29",
            "  nameserver:",
            "    - https://doh.pub/dns-query",
            "    - https://dns.alidns.com/dns-query"
        };

        /// <summary>
        /// 根据当前 TUN 设置，向配置文件写入或关闭 mihomo 官方格式的 tun 段。
        /// 该方法可重复调用（幂等）：内容没有变化时不写回文件。
        /// </summary>
        public static void ApplyTun(string profilePath)
        {
            YamlText.Update(profilePath, ApplyTunTo);
        }

        /// <summary>
        /// 在已读入内存的行集合上应用 tun / dns 修改，供 <see cref="ConfigInjector"/> 合并写入。
        /// </summary>
        internal static void ApplyTunTo(List<string> lines)
        {
            if (Global.Settings == null)
                return;

            int tunIndex = YamlText.FindTopLevelKey(lines, "tun");

            if (Global.Settings.TunMode)
            {
                bool needDnsBlock = YamlText.FindTopLevelKey(lines, "dns") < 0;
                var block = BuildTunBlock(NormalizeStack(Global.Settings.TunStack));

                if (tunIndex < 0)
                {
                    YamlText.AppendBlock(lines, block);
                    if (needDnsBlock)
                        YamlText.AppendBlock(lines, DnsBlock);
                }
                else
                {
                    // 没有 dns 段时，紧跟 tun 段补上官方 dns 块，结构同官方快捷配置
                    if (needDnsBlock)
                        block.AddRange(DnsBlock);

                    // mtu 是 Windows 上也生效的字段（如 PPPoE 环境需要调小），重建官方格式时保留用户取值
                    string mtu = FindNestedLine(lines, tunIndex, "mtu");
                    if (mtu != null)
                        block.Add(mtu);

                    YamlText.ReplaceOrAppendBlock(lines, "tun", block);
                }

                // 用户自己的 dns 段保持原样，只保证它是启用的（TUN 依赖内部 DNS）
                if (!needDnsBlock)
                    YamlText.SetEnable(lines, "dns", true);
            }
            else
            {
                if (tunIndex < 0)
                    YamlText.AppendBlock(lines, new[] { "tun:", "  enable: false" });
                else
                    YamlText.SetEnable(lines, "tun", false);
            }
        }

        /// <summary>
        /// 取块内某个子键的原始行（保留缩进与注释），用于在重建块时保留用户取值。
        /// </summary>
        private static string FindNestedLine(List<string> lines, int blockStart, string key)
        {
            int end = YamlText.FindBlockEnd(lines, blockStart);
            string prefix = key + ":";
            for (int i = blockStart + 1; i < end; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("#"))
                    continue;
                if (trimmed.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                    return "  " + trimmed;
            }
            return null;
        }

        /// <summary>
        /// 官方格式 tun 段。未显式写 dns-mode：Windows 在 auto-route 下默认就是 hijack，
        /// 与官方快捷配置保持一致；hijack 所需的 strict-route 在此显式开启。
        /// </summary>
        private static List<string> BuildTunBlock(string stack)
        {
            return new List<string>
            {
                "tun:",
                "  enable: true",
                $"  stack: {stack}",
                "  dns-hijack:",
                "    - \"any:53\"",
                "    - \"tcp://any:53\"",
                "  auto-route: true",
                "  auto-detect-interface: true",
                "  strict-route: true"
            };
        }

        /// <summary>只接受官方支持的协议栈，非法值回落到官方默认 mips。</summary>
        private static string NormalizeStack(string stack)
        {
            string value = stack?.Trim().ToLowerInvariant();
            switch (value)
            {
                case "system":
                case "gvisor":
                case "mixed":
                case "mips":
                    return value;
                default:
                    return "mips";
            }
        }
    }
}
