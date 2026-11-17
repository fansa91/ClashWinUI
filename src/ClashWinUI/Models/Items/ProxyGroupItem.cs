using Clash.SDK.Extensions;
using Clash.SDK.Models.Enums;
using ClashDotNetFramework.Models.Converters;
using ClashDotNetFramework.Utils;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.UI;

namespace ClashDotNetFramework.Models.Items
{
    /// <summary>
    /// mihomo 官方 proxy-groups 的视图模型。
    ///
    /// 官方把代理组织成两层：策略组（select / url-test / fallback / load-balance / relay）
    /// 和组内成员（节点或其他策略组）。只有 select 类型允许用户手动切换，其余类型由
    /// 内核按延迟 / 顺序 / 算法自动选择 —— 这一点由 SDK 的 IsPolicyGroupSelectable 反映。
    /// </summary>
    public class ProxyGroupItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void Notify([CallerMemberName] string name = "")
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private string _now = "";
        private long _bestLatency = -2;
        private string _typeLabel = "";
        private string _typeDescription = "";
        private string _summary = "";
        private SolidColorBrush _typeBrush = new SolidColorBrush(Color.FromArgb(0x00, 0x00, 0x00, 0x00));
        private SolidColorBrush _typeBackground = new SolidColorBrush(Color.FromArgb(0x00, 0x00, 0x00, 0x00));

        /// <summary>策略组名称（对应官方 proxy-groups[].name）</summary>
        public string Name { get; set; }

        /// <summary>官方策略组类型</summary>
        public ProxyType Type { get; set; }

        /// <summary>是否允许用户手动切换成员（仅 select / Selector）</summary>
        public bool IsSelectable => Type.IsPolicyGroupSelectable();

        /// <summary>组内成员，元素可能是节点，也可能是子策略组</summary>
        public ObservableCollection<ProxyItem> Members { get; } = new ObservableCollection<ProxyItem>();

        public string TypeLabel => _typeLabel;

        public string TypeDescription => _typeDescription;

        /// <summary>列表摘要：当前节点 · 成员数 · 最佳延迟</summary>
        public string Summary => _summary;

        /// <summary>当前生效的成员名称（对应官方 now）</summary>
        public string Now
        {
            get => _now;
            set
            {
                if (_now != value)
                {
                    _now = value ?? "";
                    Notify();
                    RefreshSummary();
                }
            }
        }

        /// <summary>组内最低延迟（用于摘要，未测到时为 -2）</summary>
        public long BestLatency
        {
            get => _bestLatency;
            private set
            {
                if (_bestLatency != value)
                {
                    _bestLatency = value;
                    Notify();
                    RefreshSummary();
                }
            }
        }

        public SolidColorBrush TypeBrush => _typeBrush;

        public SolidColorBrush TypeBackground => _typeBackground;

        /// <summary>用于本地化刷新：重新生成类型标签与描述。</summary>
        public void RefreshLocalized()
        {
            var loc = Localization.Instance;
            switch (Type)
            {
                case ProxyType.Selector:
                    _typeLabel = loc.PolicyGroupSelect;
                    _typeDescription = loc.PolicyGroupSelectDesc;
                    break;
                case ProxyType.URLTest:
                    _typeLabel = loc.PolicyGroupUrlTest;
                    _typeDescription = loc.PolicyGroupUrlTestDesc;
                    break;
                case ProxyType.FallBack:
                    _typeLabel = loc.PolicyGroupFallback;
                    _typeDescription = loc.PolicyGroupFallbackDesc;
                    break;
                case ProxyType.LoadBalance:
                    _typeLabel = loc.PolicyGroupLoadBalance;
                    _typeDescription = loc.PolicyGroupLoadBalanceDesc;
                    break;
                case ProxyType.Relay:
                default:
                    _typeLabel = loc.PolicyGroupRelay;
                    _typeDescription = loc.PolicyGroupRelayDesc;
                    break;
            }

            Notify(nameof(TypeLabel));
            Notify(nameof(TypeDescription));
            RefreshSummary();
        }

        /// <summary>成员延迟变化后重算最佳延迟与摘要。</summary>
        public void RefreshHealth()
        {
            long best = -2;
            foreach (var member in Members)
            {
                if (member.Latency <= 0)
                    continue;
                if (best == -2 || member.Latency < best)
                    best = member.Latency;
            }
            BestLatency = best;
        }

        /// <summary>主题切换后重算类型语义色。</summary>
        public void RefreshColors()
        {
            bool dark = Global.IsDarkTheme;
            Color foreground;
            Color background;

            switch (Type)
            {
                case ProxyType.Selector:
                    foreground = SemanticPalette.Brand(dark);
                    background = ThemeTokens.BrandSubtle;
                    break;
                case ProxyType.URLTest:
                    foreground = SemanticPalette.Success(dark);
                    background = SemanticPalette.SuccessBackground(dark);
                    break;
                case ProxyType.FallBack:
                    foreground = SemanticPalette.Caution(dark);
                    background = SemanticPalette.CautionBackground(dark);
                    break;
                case ProxyType.LoadBalance:
                    foreground = SemanticPalette.Info(dark);
                    background = SemanticPalette.InfoBackground(dark);
                    break;
                case ProxyType.Relay:
                default:
                    foreground = SemanticPalette.Neutral(dark);
                    background = SemanticPalette.NeutralBackground(dark);
                    break;
            }

            _typeBrush = new SolidColorBrush(foreground);
            _typeBackground = new SolidColorBrush(background);
            Notify(nameof(TypeBrush));
            Notify(nameof(TypeBackground));
        }

        private void RefreshSummary()
        {
            var loc = Localization.Instance;
            string latency = BestLatency == -2 ? loc.Untested
                : BestLatency <= 0 ? loc.Timeout
                : BestLatency + " ms";
            _summary = string.Format(loc.GroupSummary,
                string.IsNullOrEmpty(_now) ? "-" : _now, Members.Count, latency);
            Notify(nameof(Summary));
        }

        /// <summary>成员数量变化后刷新摘要。</summary>
        public void NotifyMembersChanged()
        {
            Notify(nameof(Members));
            RefreshSummary();
        }
    }
}
