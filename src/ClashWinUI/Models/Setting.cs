using ClashDotNetFramework.Models.Enums;
using ClashDotNetFramework.Models.Items;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClashDotNetFramework.Models
{
    public class Setting
    {
        /// <summary>
        /// 配置列表
        /// </summary>
        public ObservableCollection<ProfileItem> Profile = new ObservableCollection<ProfileItem>();

        #region General

        /// <summary>
        /// 语言类型
        /// </summary>
        public LanguageType Language = LanguageType.Chinese;

        /// <summary>
        /// 主题类型
        /// </summary>
        public ThemeType Theme = ThemeType.Modern;

        /// <summary>
        /// 开机启动
        /// </summary>
        public bool StartAfterSystemBoot = false;

        /// <summary>
        /// 系统代理
        /// </summary>
        public bool SystemProxy = false;

        /// <summary>
        /// TUN 模式 (mihomo 内置)
        /// </summary>
        public bool TunMode = false;

        /// <summary>
        /// TUN 协议栈 (mihomo 官方: mips/system/gvisor/mixed)
        /// 官方默认并推荐 mips；开启 Windows 防火墙时 system / mixed 不可用
        /// </summary>
        public string TunStack = "mips";

        /// <summary>
        /// Http请求超时时间
        /// </summary>
        public int RequestTimeout = 20000;

        /// <summary>
        /// 最多记录多少条日志
        /// </summary>
        public int MaximumLog = 25;

        /// <summary>
        /// MMDB更新Url
        /// </summary>
        public string MMDBUpdateUrl = "https://github.com/alecthw/mmdb_china_ip_list/releases/download/20210315/china_ip_list.mmdb";

        #endregion

        #region Proxies

        /// <summary>
        /// 延迟测试Url
        /// </summary>
        public string LatencyTestUrl = "http://www.gstatic.com/generate_204";

        /// <summary>
        /// 延迟测试超时时间 (ms)
        /// </summary>
        public int LatencyTestTimeout = 3000;

        #endregion

        #region 网络增强 (注入到订阅配置, 见 ConfigInjector)

        /// <summary>
        /// 域名嗅探: 从流量中还原目标域名, TUN 模式下分流更准确
        /// </summary>
        public bool Sniffer = true;

        /// <summary>
        /// TCP 并发: 同时连接域名的所有 IP, 采用最快完成握手的那条
        /// </summary>
        public bool TcpConcurrent = true;

        /// <summary>
        /// 统一延迟: 用同一次握手计算各阶段耗时, 延迟数值更接近真实体验
        /// </summary>
        public bool UnifiedDelay = true;

        /// <summary>
        /// 进程匹配模式 (off/strict/always), strict 对性能影响最小
        /// </summary>
        public string FindProcessMode = "strict";

        /// <summary>
        /// 持久化已选节点 (profile.store-selected)
        /// </summary>
        public bool StoreSelected = true;

        /// <summary>
        /// 持久化 fake-ip 映射 (profile.store-fake-ip)
        /// </summary>
        public bool StoreFakeIp = true;

        /// <summary>
        /// 规则库/GeoIP 自动更新 (geo-auto-update)
        /// </summary>
        public bool GeoAutoUpdate = false;

        /// <summary>
        /// 规则库更新间隔 (小时, geo-update-interval)
        /// </summary>
        public int GeoUpdateInterval = 24;

        /// <summary>
        /// 代理模式 (rule/global/direct), 重启后沿用
        /// </summary>
        public string Mode = "rule";

        /// <summary>
        /// 日志等级 (silent/error/warning/info/debug)
        /// </summary>
        public string LogLevel = "info";

        /// <summary>
        /// IPv6 开关, 同时写入 ipv6 与 dns.ipv6
        /// </summary>
        public bool IPv6 = false;

        /// <summary>
        /// 允许局域网设备使用代理 (allow-lan)。
        /// 这是一个运行时字段, 单独持久化一份, 这样内核未运行时设置页仍能显示真实状态。
        /// </summary>
        public bool AllowLan = false;

        #endregion

        #region 局域网访问控制 (P2: 注入 authentication / lan-*)

        /// <summary>
        /// 开启代理入站认证 (authentication)。只作用于代理端口,
        /// 与 RESTful API 的 secret 是两套机制, 不会影响客户端连接内核。
        /// </summary>
        public bool LanAuthEnabled = false;

        /// <summary>入站认证用户名</summary>
        public string LanAuthUser = "";

        /// <summary>入站认证密码（以明文保存在 settings.json）</summary>
        public string LanAuthPassword = "";

        /// <summary>跳过认证的来源网段 (skip-auth-prefixes), 逗号或换行分隔</summary>
        public string LanSkipAuth = "";

        /// <summary>允许使用代理的来源网段 (lan-allowed-ips), 留空表示不限制</summary>
        public string LanAllowedIps = "";

        /// <summary>禁止使用代理的来源网段 (lan-disallowed-ips), 优先级高于允许列表</summary>
        public string LanDisallowedIps = "";

        #endregion

        #region 域名映射与端口转发 (P2: 注入 hosts / tunnels)

        /// <summary>自定义 hosts, 每行「域名 IP」</summary>
        public string CustomHosts = "";

        /// <summary>端口转发 tunnels, 每行「网络,监听地址,目标地址」，如 tcp,127.0.0.1:6553,127.0.0.1:80</summary>
        public string CustomTunnels = "";

        /// <summary>上次由客户端写入 hosts 的域名，用于区分订阅自带条目，删除时不会误伤</summary>
        public List<string> HostsManaged = new List<string>();

        /// <summary>上次由客户端写入 tunnels 的条目</summary>
        public List<string> TunnelsManaged = new List<string>();

        #endregion

        #region DNS 高级 (P3: 注入 dns 段)

        /// <summary>增强模式 (redir-host / fake-ip)，留空表示不修改订阅中的设置</summary>
        public string DnsEnhancedMode = "";

        /// <summary>主 DNS 服务器，每行一个</summary>
        public string DnsNameserver = "";

        /// <summary>用于解析 DNS 服务器域名本身的服务器，每行一个</summary>
        public string DnsDefaultNameserver = "";

        /// <summary>fallback DNS 服务器，每行一个</summary>
        public string DnsFallback = "";

        /// <summary>按域名指定 DNS，每行「域名 服务器」</summary>
        public string DnsNameserverPolicy = "";

        /// <summary>fake-ip 过滤列表，每行一个</summary>
        public string DnsFakeIpFilter = "";

        /// <summary>上次由客户端接管的 dns 子键，留空时按它回退到订阅原值</summary>
        public List<string> DnsManagedFields = new List<string>();

        /// <summary>接管前的 dns 子键原文，用于用户清空后恢复订阅设置</summary>
        public Dictionary<string, List<string>> DnsBackup = new Dictionary<string, List<string>>();

        #endregion

        #region 自定义配置片段 (P3)

        /// <summary>
        /// 任意 YAML 顶层块（listeners / proxy-providers / sub-rules / tls 等），
        /// 按顶层键替换订阅中的同名块，不存在则追加。
        /// </summary>
        public string CustomSnippet = "";

        #endregion
    }
}
