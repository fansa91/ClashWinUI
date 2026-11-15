using ClashDotNetFramework.Controllers;
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
            InitializeComponent();
        }

        public void ChangeLanguage(LanguageType language)
        {
            Global.Settings.Language = language;
            Localization.Instance.SetLanguage(language);
        }

        public void ChangeTheme(ThemeType theme)
        {
            Global.Settings.Theme = theme;
            (_window as MainWindow)?.ApplyTheme(theme);
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            StartSingleInstance(args);

            Configuration.Load();

            Global.clashController = new ClashController();
            Global.clashController.Start();

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