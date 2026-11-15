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
        /// TUN 协议栈 (system/gvisor/mixed)
        /// </summary>
        public string TunStack = "mixed";

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
    }
}
