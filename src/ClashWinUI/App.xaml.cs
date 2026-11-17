using ClashDotNetFramework.Controllers;
using ClashDotNetFramework.Models.Converters;
using ClashDotNetFramework.Models.Enums;
using ClashDotNetFramework.Utils;
using Garbage_Collector;
using Microsoft.UI.Xaml;
using System;
using WindowsProxy;

namespace ClashDotNetFramework
{
    public partial class App : Application
    {
        private Window _window;
        private readonly GarbageCollector _garbageCollector = new GarbageCollector();

        public App()
        {
            // 只做 XAML 初始化：WindowsAppSDK 非打包自包含模式下，App 构造阶段访问
            // Application.Resources 会抛 COMException 0x8000FFFF，进程以 0xC000027B 退出。
            // 转换器与 Loc 资源统一在 OnLaunched 中、创建任何页面之前注册。
            InitializeComponent();
        }

        private void RegisterResources()
        {
            // 以代码方式注册全部转换器资源（避免 XAML 编译器解析同程序集类型导致 WMC0001/WMC1509）
            // Loc 供 DataTemplate 内无法使用 x:Bind 的场景以 {Binding ... Source={StaticResource Loc}} 访问
            Resources["Loc"] = Localization.Instance;
            Resources["BoolToVisibility"] = new BoolToVisibilityConverter();
            Resources["InverseBoolToVisibility"] = new InverseBoolToVisibilityConverter();
            Resources["InverseBool"] = new InverseBoolConverter();
            Resources["StringToVisibility"] = new StringToVisibilityConverter();
            Resources["LatencyToText"] = new LatencyToTextConverter();
            Resources["LatencyToBrush"] = new LatencyToBrushConverter();
            Resources["LatencyToBackground"] = new LatencyToBackgroundConverter();
            Resources["LatencyToColor"] = new LatencyToColorConverter();
            Resources["LevelToColor"] = new LevelToColorConverter();
            Resources["LevelToBackground"] = new LevelToBackgroundConverter();
            Resources["ProfileTypeToString"] = new ProfileTypeToStringConverter();
            Resources["RatioToPercent"] = new RatioToPercentConverter();
            Resources["NodesCount"] = new NodesCountConverter();
            Resources["SemanticTag"] = new SemanticTagConverter();
            Resources["RelativeTime"] = new RelativeTimeConverter();
        }

        public void ChangeLanguage(LanguageType language)
        {
            bool changed = Global.Settings.Language != language;
            Global.Settings.Language = language;
            Localization.Instance.SetLanguage(language);
            // x:Bind 绑定的文案会随 Localization 自动刷新，
            // 但代码里写入的文案（按钮/拼接文本）不受 x:Bind 管理，需要集中通知各页面。
            (_window as MainWindow)?.RefreshLocalizedText();
            Global.Pages?.RefreshLocalizedText();
            // 语言是持久化选项：不落盘会导致重启后回到默认语言
            if (changed)
                Configuration.Save();
        }

        public void ChangeTheme(ThemeType theme)
        {
            bool changed = Global.Settings.Theme != theme;
            Global.Settings.Theme = theme;
            (_window as MainWindow)?.ApplyTheme(theme);
            if (changed)
                Configuration.Save();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // 必须在 MainWindow / 各页面加载之前注册；构造阶段访问 Application.Resources 会导致启动崩溃
            RegisterResources();

            StartSingleInstance(args);

            Configuration.Load();

            Global.clashController = new ClashController();
            // 默认不自动启动内核（用户反馈）：打开应用停在“启动内核”状态，
            // 由用户手动启动；计时也随内核状态走（见 OverviewPage/MainWindow）。

            RegisterURIScheme();

            Localization.Instance.SetLanguage(Global.Settings.Language);

            _window = new MainWindow();
            Global.MainWindow = _window;
            Global.AppDispatcher = _window.DispatcherQueue;
            ChangeTheme(Global.Settings.Theme);
            _window.Activate();

            Global.iconController.Start();
            _garbageCollector.StartCollecting();
        }


        private void StartSingleInstance(LaunchActivatedEventArgs args)
        {
            var singleInstance = new SingleInstance.SingleInstance(@"Global\ClashDotNetFramework");

            if (!singleInstance.IsFirstInstance)
            {
                singleInstance.PassArgumentsToFirstInstance(new[] { args.Arguments });
                Environment.Exit(-1);
            }

            singleInstance.ArgumentsReceived.Subscribe(arguments =>
            {
                foreach (var arg in arguments)
                {
                    string captured = arg;
                    Global.RunOnUI(() => URIHelper.ProcessURI(captured));
                }
            });
            singleInstance.ListenForArgumentsFromSuccessiveInstances();
        }

        private static void RegisterURIScheme()
        {
            try
            {
                if (!URISchemeHelper.Check())
                    URISchemeHelper.Set();
            }
            catch
            {
            }
        }
    }
}