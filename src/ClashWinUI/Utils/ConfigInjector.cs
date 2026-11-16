using ClashDotNetFramework.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace ClashDotNetFramework.Utils
{
    /// <summary>
    /// 客户端设置 → 订阅配置的注入层。
    ///
    /// 背景：内核启动后会用订阅文件调用 PUT /configs 整体替换配置（不合并），
    /// 因此只有写进“当前订阅文件”的设置才会真正生效并持久保留。
    /// 官方 PATCH /configs 仅支持 mode / log-level / allow-lan / ipv6 / tun 等运行时字段，
    /// sniffer、profile、tcp-concurrent 等只能出现在配置文件里。
    ///
    /// 这里统一在 reload 之前把客户端开关注入订阅文件，仍然保持行级编辑：
    /// 用户原有的注释、锚点、rule-provider 写法一律不动，只增改目标键。
    ///
    /// 参考官方完整配置模板：MetaCubeX/mihomo 仓库 docs/config.yaml
    /// </summary>
    public static class ConfigInjector
    {
        /// <summary>
        /// 官方 sniffer 段（取官方快捷配置的字段，Windows/TUN 下也适用）。
        /// parse-pure-ip 让纯 IP 流量也能被嗅探出域名，TUN 下分流才准确；
        /// skip-domain 排除小米云与 Apple 推送，避免嗅探结果影响这些服务的连接。
        /// </summary>
        private static readonly string[] SnifferBlock =
        {
            "sniffer:",
            "  enable: true",
            "  force-dns-mapping: true",
            "  parse-pure-ip: true",
            "  sniff:",
            "    HTTP:",
            "      ports: [80, 8080-8880]",
            "      override-destination: true",
            "    TLS:",
            "      ports: [443, 8443]",
            "    QUIC:",
            "      ports: [443, 8443]",
            "  skip-domain:",
            "    - \"Mijia Cloud\"",
            "    - \"+.push.apple.com\""
        };

        /// <summary>
        /// 把当前客户端设置注入指定订阅文件。可重复调用（幂等），内容不变时不写盘。
        /// </summary>
        public static void Apply(string profilePath)
        {
            YamlText.Update(profilePath, ApplyTo);
        }

        /// <summary>
        /// 在已读入内存的行集合上应用全部设置，供一次写入完成多个键的修改。
        /// </summary>
        internal static void ApplyTo(List<string> lines)
        {
            var settings = Global.Settings;
            if (settings == null)
                return;

            // tun 段同时负责在需要时补出 dns 段，因此放在最前面
            TunHelper.ApplyTunTo(lines);

            ApplyProfileStorage(lines, settings);
            ApplySniffer(lines, settings);
            ApplyRuntimeScalars(lines, settings);
            ApplyLanAccess(lines, settings);
            ApplyHosts(lines, settings);
            ApplyTunnels(lines, settings);
            ApplyDnsAdvanced(lines, settings);

            // IPv6 联动：dns 段存在时才写（TUN 关闭且订阅没有 dns 段时无需凭空创建）
            if (YamlText.FindTopLevelKey(lines, "dns") >= 0)
                YamlText.SetNestedScalar(lines, "dns", "ipv6", YamlText.Bool(settings.IPv6));

            // 自定义片段最后执行：高级用户可以用它覆盖上面任意一项（tun/dns/listeners/providers 等）
            if (!string.IsNullOrWhiteSpace(settings.CustomSnippet))
                YamlText.ApplyFragment(lines, settings.CustomSnippet);
        }

        /// <summary>profile 段：持久化已选节点与 fake-ip 映射，重启后不再丢失。</summary>
        private static void ApplyProfileStorage(List<string> lines, Setting settings)
        {
            if (YamlText.FindTopLevelKey(lines, "profile") < 0)
            {
                YamlText.AppendBlock(lines, new[]
                {
                    "profile:",
                    "  store-selected: " + YamlText.Bool(settings.StoreSelected),
                    "  store-fake-ip: " + YamlText.Bool(settings.StoreFakeIp)
                });
                return;
            }

            YamlText.SetNestedScalar(lines, "profile", "store-selected", YamlText.Bool(settings.StoreSelected));
            YamlText.SetNestedScalar(lines, "profile", "store-fake-ip", YamlText.Bool(settings.StoreFakeIp));
        }

        private static void ApplySniffer(List<string> lines, Setting settings)
        {
            int index = YamlText.FindTopLevelKey(lines, "sniffer");
            if (index < 0)
            {
                // 关闭时不必写一个 enable: false 的空块，mihomo 默认就是关闭
                if (settings.Sniffer)
                    YamlText.AppendBlock(lines, SnifferBlock);
                return;
            }

            // 已有 sniffer 段只切换 enable，保留用户自己的 ports / skip-domain
            YamlText.SetEnable(lines, "sniffer", settings.Sniffer);
        }

        private static void ApplyRuntimeScalars(List<string> lines, Setting settings)
        {
            YamlText.SetScalar(lines, "tcp-concurrent", YamlText.Bool(settings.TcpConcurrent));
            YamlText.SetScalar(lines, "unified-delay", YamlText.Bool(settings.UnifiedDelay));
            YamlText.SetScalar(lines, "find-process-mode", NormalizeFindProcessMode(settings.FindProcessMode));
            YamlText.SetScalar(lines, "geo-auto-update", YamlText.Bool(settings.GeoAutoUpdate));
            if (settings.GeoAutoUpdate)
                YamlText.SetScalar(lines, "geo-update-interval", NormalizeGeoInterval(settings.GeoUpdateInterval).ToString());
            YamlText.SetScalar(lines, "log-level", NormalizeLogLevel(settings.LogLevel));
            YamlText.SetScalar(lines, "mode", NormalizeMode(settings.Mode));
            // ipv6 开关与设置页一致, 重启后不会退回默认关闭
            YamlText.SetScalar(lines, "ipv6", YamlText.Bool(settings.IPv6));
        }

        #region 局域网访问控制 (P2)

        /// <summary>
        /// 代理入站认证与局域网 ACL。
        ///
        /// authentication 作用于代理端口（HTTP/SOCKS），和 RESTful API 的 secret 是两套机制，
        /// 所以开启后客户端连接内核不受影响；但 Windows 系统代理无法携带凭据，开启前设置页会提示。
        /// 关闭认证或用户名密码不全时，移除这两个键（它们由本客户端独占，订阅不会自带）。
        /// </summary>
        private static void ApplyLanAccess(List<string> lines, Setting settings)
        {
            string user = (settings.LanAuthUser ?? string.Empty).Trim();
            string password = settings.LanAuthPassword ?? string.Empty;

            if (settings.LanAuthEnabled && user.Length > 0 && password.Length > 0)
            {
                YamlText.SetListBlock(lines, "authentication", new[] { user + ":" + password });

                // 本机回环始终免认证：Windows 系统代理只会写 127.0.0.1:port，无法携带凭据，
                // 一旦回环地址落在认证范围内，系统代理就整体失效（实测返回 407）。
                // 这里必须“追加”而不是“仅在为空时补”，否则用户只要填了任意网段就会把本机漏掉。
                var skip = SplitItems(settings.LanSkipAuth).Where(IsIpOrCidr).ToList();
                AppendLoopback(skip);
                YamlText.SetListBlock(lines, "skip-auth-prefixes", skip);
            }
            else
            {
                YamlText.RemoveBlock(lines, "authentication");
                YamlText.RemoveBlock(lines, "skip-auth-prefixes");
            }

            var allowed = SplitItems(settings.LanAllowedIps).Where(IsIpOrCidr).ToList();
            // 白名单一旦非空就只放行列出的网段，同样必须放行回环，否则本机连接会被直接断开
            // （比 407 更糟：连认证框都不会出现，代理看起来“彻底坏了”）。
            if (allowed.Count > 0)
                AppendLoopback(allowed);
            YamlText.SetListBlock(lines, "lan-allowed-ips", allowed);

            // 黑名单里误填回环会让本机自我封锁，剔除掉精确的回环项
            var denied = SplitItems(settings.LanDisallowedIps)
                .Where(IsIpOrCidr)
                .Where(item => !IsLoopbackEntry(item))
                .ToList();
            YamlText.SetListBlock(lines, "lan-disallowed-ips", denied);
        }

        /// <summary>本机回环网段；SkipAuth 与 lan-allowed-ips 都必须放行，否则系统代理会失效。</summary>
        private static readonly string[] LoopbackPrefixes = { "127.0.0.1/32", "::1/128" };

        private static void AppendLoopback(List<string> prefixes)
        {
            foreach (string prefix in LoopbackPrefixes)
            {
                if (!prefixes.Contains(prefix, StringComparer.OrdinalIgnoreCase))
                    prefixes.Add(prefix);
            }
        }

        /// <summary>是否为指向本机回环的条目（用于从黑名单里剔除）。</summary>
        private static bool IsLoopbackEntry(string value)
        {
            string ip = value.Trim();
            int slash = ip.IndexOf('/');
            if (slash >= 0)
                ip = ip.Substring(0, slash);

            return IPAddress.TryParse(ip, out IPAddress address) && IPAddress.IsLoopback(address);
        }

        #endregion

        #region 域名映射与端口转发 (P2)

        /// <summary>
        /// 自定义 hosts。采用「托管键」策略：只移除上次由本客户端写入的域名，
        /// 订阅自带的 hosts 条目会原样保留，不会被客户端编辑覆盖掉。
        /// </summary>
        private static void ApplyHosts(List<string> lines, Setting settings)
        {
            var managed = new HashSet<string>(
                settings.HostsManaged ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            var entries = new List<KeyValuePair<string, string>>();
            foreach (var existing in YamlText.ReadMapEntries(lines, "hosts"))
            {
                if (!managed.Contains(existing.Key))
                    entries.Add(existing);
            }

            var current = new List<string>();
            foreach (string raw in SplitLines(settings.CustomHosts))
            {
                if (!TryParseHostsLine(raw, out string domain, out string target))
                    continue;

                current.Add(domain);
                entries.RemoveAll(entry => string.Equals(entry.Key, domain, StringComparison.OrdinalIgnoreCase));
                entries.Add(new KeyValuePair<string, string>(domain, target));
            }

            settings.HostsManaged = current;
            YamlText.SetMapBlock(lines, "hosts", entries);
        }

        /// <summary>端口转发 tunnels，同样只增删本客户端托管的条目。</summary>
        private static void ApplyTunnels(List<string> lines, Setting settings)
        {
            var managed = new HashSet<string>(
                settings.TunnelsManaged ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            var items = YamlText.ReadListItems(lines, "tunnels")
                .Where(item => !managed.Contains(item))
                .ToList();

            var current = new List<string>();
            foreach (string raw in SplitLines(settings.CustomTunnels))
            {
                string item = NormalizeTunnel(raw);
                if (item == null)
                    continue;

                current.Add(item);
                items.RemoveAll(existing => string.Equals(existing, item, StringComparison.OrdinalIgnoreCase));
                items.Add(item);
            }

            settings.TunnelsManaged = current;
            YamlText.SetListBlock(lines, "tunnels", items);
        }

        #endregion

        #region DNS 高级 (P3)

        /// <summary>
        /// DNS 高级设置。语义是「填了才接管，留空沿用订阅」：
        /// 上次由客户端写过、本次留空的字段会被移除，恢复订阅原来的取值。
        /// </summary>
        private static void ApplyDnsAdvanced(List<string> lines, Setting settings)
        {
            var previous = new HashSet<string>(
                settings.DnsManagedFields ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var managed = new List<string>();

            string enhanced = (settings.DnsEnhancedMode ?? string.Empty).Trim().ToLowerInvariant();
            if (enhanced == "redir-host" || enhanced == "fake-ip")
            {
                EnsureDnsBlock(lines);
                BackupNested(lines, settings, "enhanced-mode", previous);
                YamlText.SetNestedScalar(lines, "dns", "enhanced-mode", enhanced);
                managed.Add("enhanced-mode");
            }
            else if (previous.Contains("enhanced-mode"))
            {
                RestoreNested(lines, settings, "enhanced-mode");
            }

            ApplyDnsList(lines, settings, "nameserver", settings.DnsNameserver, previous, managed);
            ApplyDnsList(lines, settings, "default-nameserver", settings.DnsDefaultNameserver, previous, managed);
            ApplyDnsList(lines, settings, "fallback", settings.DnsFallback, previous, managed);
            ApplyDnsList(lines, settings, "fake-ip-filter", settings.DnsFakeIpFilter, previous, managed);

            var policy = new List<KeyValuePair<string, string>>();
            foreach (string raw in SplitLines(settings.DnsNameserverPolicy))
            {
                if (TryParsePolicyLine(raw, out string domain, out string server))
                    policy.Add(new KeyValuePair<string, string>(domain, server));
            }

            if (policy.Count > 0)
            {
                EnsureDnsBlock(lines);
                BackupNested(lines, settings, "nameserver-policy", previous);
                YamlText.SetNestedMap(lines, "dns", "nameserver-policy", policy);
                managed.Add("nameserver-policy");
            }
            else if (previous.Contains("nameserver-policy"))
            {
                RestoreNested(lines, settings, "nameserver-policy");
            }

            settings.DnsManagedFields = managed;
        }

        private static void ApplyDnsList(List<string> lines, Setting settings, string key, string value,
            HashSet<string> previous, List<string> managed)
        {
            var items = SplitLines(value).ToList();
            if (items.Count > 0)
            {
                EnsureDnsBlock(lines);
                BackupNested(lines, settings, key, previous);
                YamlText.SetNestedList(lines, "dns", key, items);
                managed.Add(key);
            }
            else if (previous.Contains(key))
            {
                RestoreNested(lines, settings, key);
            }
        }

        /// <summary>首次接管某个子键前，把订阅里的原值存下来，供用户清空后回退。</summary>
        private static void BackupNested(List<string> lines, Setting settings, string key, HashSet<string> previous)
        {
            if (previous.Contains(key))
                return;

            var block = YamlText.ExtractNestedBlock(lines, "dns", key);
            if (block == null || block.Count == 0)
                return;

            if (settings.DnsBackup == null)
                settings.DnsBackup = new Dictionary<string, List<string>>();
            settings.DnsBackup[key] = block;
        }

        /// <summary>清空某项时：有备份就恢复订阅原值，没备份（订阅本来没有这一项）则删除。</summary>
        private static void RestoreNested(List<string> lines, Setting settings, string key)
        {
            if (settings.DnsBackup != null
                && settings.DnsBackup.TryGetValue(key, out var block)
                && block != null && block.Count > 0)
            {
                YamlText.ReplaceNestedRaw(lines, "dns", key, block);
                settings.DnsBackup.Remove(key);
                return;
            }

            YamlText.RemoveNestedKey(lines, "dns", key);
        }

        /// <summary>用户填了 DNS 设置但订阅没有 dns 段时，补一个最小可用段。</summary>
        private static void EnsureDnsBlock(List<string> lines)
        {
            if (YamlText.FindTopLevelKey(lines, "dns") >= 0)
                return;
            YamlText.AppendBlock(lines, new[] { "dns:", "  enable: true" });
        }

        #endregion

        #region 取值解析

        private static IEnumerable<string> SplitLines(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Array.Empty<string>();

            return value.Replace("\r\n", "\n")
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith("#"));
        }

        /// <summary>把逗号/分号/换行分隔的输入拆成去重列表（用于 CIDR 列表）。</summary>
        private static List<string> SplitItems(string value)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(value))
                return result;

            foreach (string part in value.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string item = part.Trim();
                if (item.Length == 0 || item.StartsWith("#") || result.Contains(item))
                    continue;
                result.Add(item);
            }
            return result;
        }

        /// <summary>单个 IP 或 CIDR 校验；非法项直接丢弃，避免写成内核无法解析的值。</summary>
        internal static bool IsIpOrCidr(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string ip = value.Trim();
            int slash = ip.IndexOf('/');
            if (slash >= 0)
            {
                if (!int.TryParse(ip.Substring(slash + 1), out int prefix) || prefix < 0 || prefix > 128)
                    return false;
                ip = ip.Substring(0, slash);
            }
            return IPAddress.TryParse(ip, out _);
        }

        /// <summary>hosts 行：域名与 IP 用空格（或 domain: 形式）分隔。</summary>
        internal static bool TryParseHostsLine(string raw, out string domain, out string target)
        {
            domain = null;
            target = null;

            string line = (raw ?? string.Empty).Trim();
            if (line.Length == 0 || line.StartsWith("#"))
                return false;

            string left;
            string right;
            int separator = line.IndexOfAny(new[] { ' ', '\t' });
            if (separator > 0)
            {
                left = line.Substring(0, separator);
                right = line.Substring(separator + 1);
            }
            else
            {
                int colon = line.LastIndexOf(':');
                if (colon <= 0)
                    return false;
                left = line.Substring(0, colon);
                right = line.Substring(colon + 1);
            }

            left = YamlText.Unquote(left.Trim()).TrimEnd(':').Trim();
            right = YamlText.Unquote(right.Trim()).Trim();
            if (left.Length == 0 || right.Length == 0 || !IPAddress.TryParse(right, out _))
                return false;

            domain = left;
            target = right;
            return true;
        }

        /// <summary>tunnels 行：网络,监听地址,目标地址。</summary>
        internal static string NormalizeTunnel(string raw)
        {
            string line = (raw ?? string.Empty).Trim();
            if (line.Length == 0 || line.StartsWith("#"))
                return null;

            string[] parts = line.Split(',');
            if (parts.Length != 3)
                return null;

            string network = parts[0].Trim().ToLowerInvariant();
            string listen = parts[1].Trim();
            string target = parts[2].Trim();
            if (network != "tcp" && network != "udp")
                return null;
            if (listen.Length == 0 || target.Length == 0)
                return null;

            return network + "," + listen + "," + target;
        }

        /// <summary>nameserver-policy 行：域名与 DNS 用空白分隔。</summary>
        internal static bool TryParsePolicyLine(string raw, out string domain, out string server)
        {
            domain = null;
            server = null;

            string line = (raw ?? string.Empty).Trim();
            if (line.Length == 0 || line.StartsWith("#"))
                return false;

            int separator = line.IndexOfAny(new[] { ' ', '\t' });
            if (separator <= 0)
                return false;

            domain = YamlText.Unquote(line.Substring(0, separator).Trim()).TrimEnd(':').Trim();
            server = YamlText.Unquote(line.Substring(separator + 1).Trim());
            return domain.Length > 0 && server.Length > 0;
        }

        #endregion

        #region 取值校验

        internal static string NormalizeFindProcessMode(string value)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "off":
                case "strict":
                case "always":
                    return value.Trim().ToLowerInvariant();
                default:
                    return "strict";
            }
        }

        internal static string NormalizeLogLevel(string value)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "silent":
                case "error":
                case "warning":
                case "info":
                case "debug":
                    return value.Trim().ToLowerInvariant();
                default:
                    return "info";
            }
        }

        internal static string NormalizeMode(string value)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "global":
                case "direct":
                case "rule":
                    return value.Trim().ToLowerInvariant();
                default:
                    return "rule";
            }
        }

        internal static int NormalizeGeoInterval(int hours)
        {
            if (hours < 1)
                return 24;
            return hours > 720 ? 720 : hours;
        }

        #endregion
    }
}
