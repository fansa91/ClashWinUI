using ClashDotNetFramework.Models;
using ClashDotNetFramework.Models.Items;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClashDotNetFramework.Utils
{
    public static class Configuration
    {
        public static string DATA_DIR = Path.Combine(Utils.GetClashConfigDir(), "data");

        public static readonly string SETTINGS_JSON = Path.Combine(DATA_DIR, "settings.json");

        public static void Load()
        {
            if (Directory.Exists(DATA_DIR) && File.Exists(SETTINGS_JSON))
            {
                try
                {
                    var settingJObject = (JObject)JsonConvert.DeserializeObject(File.ReadAllText(SETTINGS_JSON));
                    Global.Settings = settingJObject?.ToObject<Setting>() ?? new Setting();
                    Global.Settings.Profile.Clear();

                    if (settingJObject?["Profile"] != null)
                    {
                        foreach (JObject server in settingJObject["Profile"])
                        {
                            var profileResult = server.ToObject<ProfileItem>();
                            if (profileResult != null)
                                Global.Settings.Profile.Add(profileResult);
                        }
                    }

                    CheckAll();
                    Save();
                }
                catch
                {
                }
            }
            else
            {
                CheckAll();
                Save();
            }
        }

        public static void Save()
        {
            if (!Directory.Exists(DATA_DIR))
            {
                Directory.CreateDirectory(DATA_DIR);
            }

             File.WriteAllText(SETTINGS_JSON, JsonConvert.SerializeObject(Global.Settings, Formatting.Indented, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
        }

        #region Check Methods

        private static void CheckAll()
        {
            CheckProfile();
            CheckMMDB();
            CheckTunStack();
            CheckInjection();
            CheckNetworkOptions();
        }

        /// <summary>
        /// P2/P3 设置项的空值归一化。settings.json 可能被手工编辑成 null，
        /// 后续按行分割会直接抛异常，这里统一收敛成空字符串。
        /// 内容本身（CIDR、hosts、tunnels）在 ConfigInjector 生成配置时再逐条校验。
        /// </summary>
        private static void CheckNetworkOptions()
        {
            var settings = Global.Settings;
            settings.LanAuthUser = Clean(settings.LanAuthUser);
            settings.LanAuthPassword = Clean(settings.LanAuthPassword);
            settings.LanSkipAuth = Clean(settings.LanSkipAuth);
            settings.LanAllowedIps = Clean(settings.LanAllowedIps);
            settings.LanDisallowedIps = Clean(settings.LanDisallowedIps);
            settings.CustomHosts = Clean(settings.CustomHosts);
            settings.CustomTunnels = Clean(settings.CustomTunnels);
            settings.DnsEnhancedMode = Clean(settings.DnsEnhancedMode).ToLowerInvariant();
            settings.DnsNameserver = Clean(settings.DnsNameserver);
            settings.DnsDefaultNameserver = Clean(settings.DnsDefaultNameserver);
            settings.DnsFallback = Clean(settings.DnsFallback);
            settings.DnsNameserverPolicy = Clean(settings.DnsNameserverPolicy);
            settings.DnsFakeIpFilter = Clean(settings.DnsFakeIpFilter);
            settings.CustomSnippet = settings.CustomSnippet ?? string.Empty;

            if (settings.DnsEnhancedMode != "redir-host" && settings.DnsEnhancedMode != "fake-ip")
                settings.DnsEnhancedMode = string.Empty;

            settings.HostsManaged ??= new List<string>();
            settings.TunnelsManaged ??= new List<string>();
            settings.DnsManagedFields ??= new List<string>();
            settings.DnsBackup ??= new Dictionary<string, List<string>>();
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace("\r\n", "\n").Trim();
        }

        /// <summary>
        /// 旧版本默认使用 mixed 协议栈, 在开启 Windows 防火墙时无法使用。
        /// 老 settings.json 里已持久化的值会覆盖新默认值, 此处迁移到官方推荐的 mips。
        /// </summary>
        private static void CheckTunStack()
        {
            if (string.Equals(Global.Settings.TunStack, "mixed", StringComparison.OrdinalIgnoreCase))
            {
                Global.Settings.TunStack = "mips";
            }
        }

        /// <summary>
        /// 注入到订阅配置的取值校验：settings.json 可能被手工改过，
        /// 非法值在这里收敛成官方支持的值，避免写进配置文件后内核启动失败。
        /// </summary>
        private static void CheckInjection()
        {
            var settings = Global.Settings;
            settings.FindProcessMode = ConfigInjector.NormalizeFindProcessMode(settings.FindProcessMode);
            settings.LogLevel = ConfigInjector.NormalizeLogLevel(settings.LogLevel);
            settings.Mode = ConfigInjector.NormalizeMode(settings.Mode);
            settings.GeoUpdateInterval = ConfigInjector.NormalizeGeoInterval(settings.GeoUpdateInterval);
        }

        private static void CheckProfile()
        {
            // 如果用户没有配置文件, 新建一个
            if (Global.Settings.Profile.Count == 0)
            {
                string path = ConfigHelper.GenerateClashConfig();
                ProfileItem profileItem = new ProfileItem
                {
                    Name = "config.yaml",
                    FileName = path,
                    IsSelected = true
                };
                Global.Settings.Profile.Add(profileItem);
            }
            // 如果用户所有配置文件都没有选中, 选中最后一个
            if (Global.Settings.Profile.FirstOrDefault(p => p.IsSelected == true) == null)
            {
                Global.Settings.Profile.Last().IsSelected = true;
            }
        }

        private static void CheckMMDB()
        {
            // MMDB文件位置
            string path = Path.Combine(Utils.GetClashConfigDir(), "Country.mmdb");
            // 没有MMDB文件就复制一个过去
            if (!File.Exists(path))
            {
                string sourcePath = Path.Combine(Global.ClashDotNetFrameworkDir, "bin\\Country.mmdb");
                if (File.Exists(sourcePath))
                    File.Copy(sourcePath, path);
            }
        }

        #endregion
    }
}
