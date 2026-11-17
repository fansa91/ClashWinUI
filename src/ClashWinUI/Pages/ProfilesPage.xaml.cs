using ClashDotNetFramework.Controllers;
using ClashDotNetFramework.Dialogs;
using ClashDotNetFramework.Models.Enums;
using ClashDotNetFramework.Models.Items;
using ClashDotNetFramework.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace ClashDotNetFramework.Pages
{
    public sealed partial class ProfilesPage : Page
    {
        public ProfilesPage()
        {
            InitializeComponent();
            DataContext = Localization.Instance;
        }

        /// <summary>
        /// 本地化字符串表，供 x:Bind 使用
        /// </summary>
        public Localization Loc => Localization.Instance;

        private void ProfilesPage_Loaded(object sender, RoutedEventArgs e)
        {
            ProfileItemListView.ItemsSource = Global.Settings.Profile;
            // 默认高亮当前生效的订阅，用户点其它行即可切换操作对象
            var active = Global.Settings.Profile.FirstOrDefault(p => p.IsSelected);
            if (active != null)
                ProfileItemListView.SelectedItem = active;
            UpdateTargetState();
        }

        /// <summary>
        /// 列表高亮行即「操作对象」。三个按钮都作用于它：
        /// 未选中任何行时禁用；选中行已是生效配置时「激活」按钮显示"已激活"并禁用。
        /// </summary>
        private void UpdateTargetState()
        {
            var target = ProfileItemListView.SelectedItem as ProfileItem;
            bool hasTarget = target != null;

            // 作用对象常驻可见：未选中时给出占位文案，而不是隐藏整块
            TargetProfileText.Text = target?.Name ?? Localization.Instance["Unknown"];
            TargetStatsText.Text = hasTarget
                ? string.Format(Localization.Instance.ProfileStats,
                    target!.ProxyCount, target.GroupCount, target.RuleCount)
                : string.Empty;
            TargetActiveBadge.Visibility = hasTarget && target!.IsSelected
                ? Visibility.Visible
                : Visibility.Collapsed;

            ActivateButton.IsEnabled = hasTarget && !target!.IsSelected;
            UpdateProfileButton.IsEnabled = hasTarget;
            CopyUrlButton.IsEnabled = hasTarget && !string.IsNullOrWhiteSpace(target!.Url);

            // 已经是生效配置时，提示用户无需重复激活
            ActivateButton.Content = hasTarget && target!.IsSelected
                ? Localization.Instance["Activated"]
                : Localization.Instance["Activate"];
        }

        private void ProfileItemListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            UpdateTargetState();
        }

        /// <summary>卡片上的按钮通过 DataContext 拿到自己那一行，不依赖列表选中项。</summary>
        private static ProfileItem ItemOf(object sender)
            => (sender as FrameworkElement)?.DataContext as ProfileItem;

        private async void CardActivate_Click(object sender, RoutedEventArgs e)
        {
            var profile = ItemOf(sender);
            if (profile == null || profile.IsSelected)
                return;
            await SetAsDefaultProfile(profile);
            ProfileItemListView.SelectedItem = profile;
            UpdateTargetState();
        }

        private async void CardUpdate_Click(object sender, RoutedEventArgs e)
        {
            var profile = ItemOf(sender);
            if (profile == null)
                return;

            if (sender is Button button)
                button.IsEnabled = false;
            try
            {
                await Task.Run(() => profile.Update());
            }
            finally
            {
                if (sender is Button same)
                    same.IsEnabled = true;
            }

            Configuration.Save();
            UpdateTargetState();
        }

        private void ProfileGrid_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }

        private async void ProfileGrid_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
                return;
            var items = await e.DataView.GetStorageItemsAsync();
            foreach (var file in items.OfType<StorageFile>())
            {
                if (file.Name.EndsWith(".yml") || file.Name.EndsWith(".yaml"))
                    await ImportProfileFromFile(file.Name, file.Path);
            }
        }

        private async Task ImportProfileFromFile(string fileName, string filePath)
        {
            string path = Path.Combine(Utils.Utils.GetClashProfilesDir(), fileName);
            File.Copy(filePath, path, true);
            var profileItem = new ProfileItem { Name = fileName, FileName = path };
            profileItem.Update();
            Global.RunOnUI(() => Global.Settings.Profile.Add(profileItem));
            await SetAsDefaultProfile(profileItem);
            Global.RunOnUI(() => ImportButton.Content = "Done!");
            Configuration.Save();
        }

        private async Task SetAsDefaultProfile(ProfileItem profile)
        {
            try
            {
                // 切换或更新订阅后都要重新注入客户端设置, 否则新文件里没有 tun / sniffer 等段
                ConfigInjector.Apply(profile.FileName);

                if (Global.clashClient != null && Global.clashController?.IsRunning == true)
                    await Global.clashClient.ReloadClashConfig(Clash.SDK.Models.Enums.ConfigType.Path, false, profile.FileName);

                foreach (var p in Global.Settings.Profile)
                    p.IsSelected = false;
                profile.IsSelected = true;
                Global.Refresh = true;
                Configuration.Save();
                // 生效配置变了，列表高亮与按钮可用状态都要跟着更新
                Global.RunOnUI(() =>
                {
                    if (ProfileItemListView.SelectedItem is ProfileItem current)
                        ProfileItemListView.SelectedItem = current;
                    UpdateTargetState();
                });
            }
            catch
            {
                await DialogHelper.ShowMessageAsync("ClashWinUI", $"Load config \"{profile.Name}\" error");
            }
        }

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            string text = UrlTextBox.Text;
            if (string.IsNullOrWhiteSpace(text))
                return;
            DownloadButton.Content = "Downloading";
            Task.Run(async () =>
            {
                try
                {
                    var uri = new Uri(text);
                    var profileItem = new ProfileItem
                    {
                        Type = ProfileType.Remote,
                        IsRemote = true,
                        Host = uri.Host,
                        Url = uri.AbsoluteUri,
                        UpdateInterval = 0,
                        LastUpdate = DateTime.Now
                    };
                    if (profileItem.Update())
                    {
                        Global.RunOnUI(() =>
                        {
                            Global.Settings.Profile.Add(profileItem);
                            DownloadButton.Content = "Success!";
                        });
                        await SetAsDefaultProfile(profileItem);
                    }
                }
                catch
                {
                    Global.RunOnUI(() => DownloadButton.Content = "Error!");
                }
                Configuration.Save();
            });
        }

        private void UpdateAllButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateAllButton.Content = "Updating";
            Task.Run(() =>
            {
                foreach (var profile in Global.Settings.Profile)
                    profile.Update();
                Global.RunOnUI(() => UpdateAllButton.Content = "Done!");
                // 订阅文件被整份覆盖, 需要重新注入客户端设置后再让内核加载
                CoreManager.ApplyProfileConfig();
                Configuration.Save();
            });
        }

        private async void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".yaml");
            picker.FileTypeFilter.Add(".yml");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(Global.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file != null)
                await ImportProfileFromFile(file.Name, file.Path);
        }

        private static ProfileItem GetProfile(object sender)
            => (sender as MenuFlyoutItem)?.DataContext as ProfileItem;

        private async void SelectProfile_Click(object sender, RoutedEventArgs e)
        {
            var profile = GetProfile(sender);
            if (profile != null)
                await SetAsDefaultProfile(profile);
        }

        private void UpdateProfile_Click(object sender, RoutedEventArgs e)
        {
            var profile = GetProfile(sender);
            if (profile == null)
                return;
            Task.Run(async () =>
            {
                if (profile.Update())
                    await SetAsDefaultProfile(profile);
            });
        }

        private void EditProfile_Click(object sender, RoutedEventArgs e)
        {
            var profile = GetProfile(sender);
            if (profile == null)
                return;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = profile.FileName, UseShellExecute = true });
            }
            catch
            {
            }
        }

        private async void DuplicateProfile_Click(object sender, RoutedEventArgs e)
        {
            var profile = GetProfile(sender);
            if (profile == null)
                return;
            string name = await DuplicateProfileDialog.ShowAsync();
            if (!string.IsNullOrWhiteSpace(name))
            {
                var copy = profile.Copy(name);
                Global.RunOnUI(() => Global.Settings.Profile.Add(copy));
                Configuration.Save();
            }
        }

        private void GoToURL_Click(object sender, RoutedEventArgs e)
        {
            var profile = GetProfile(sender);
            if (profile == null || !profile.IsRemote)
                return;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = profile.Url, UseShellExecute = true });
            }
            catch
            {
            }
        }

        private async void ProfileSettings_Click(object sender, RoutedEventArgs e)
        {
            var profile = GetProfile(sender);
            if (profile == null)
                return;
            await ProfileSettingsDialog.ShowAsync(profile);
            Configuration.Save();
        }

        private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            var profile = GetProfile(sender);
            if (profile == null)
                return;
            bool confirm = await DialogHelper.ConfirmAsync("ClashWinUI", $"Are you sure to delete \"{profile.Name}\"?");
            if (!confirm)
                return;
            try
            {
                File.Delete(profile.FileName);
            }
            catch
            {
            }
            Global.RunOnUI(() =>
            {
                Global.Settings.Profile.Remove(profile);
                if (!Global.Settings.Profile.Any(p => p.IsSelected) && Global.Settings.Profile.Count > 0)
                {
                    var last = Global.Settings.Profile.Last();
                    last.IsSelected = true;
                    _ = SetAsDefaultProfile(last);
                }
            });
            Configuration.Save();
        }

        /// <summary>把当前高亮的行设为生效配置。</summary>
        private async void ActivateSubscription_Click(object sender, RoutedEventArgs e)
        {
            var target = ProfileItemListView.SelectedItem as ProfileItem;
            if (target == null || target.IsSelected)
                return;
            await SetAsDefaultProfile(target);
            UpdateTargetState();
        }

        /// <summary>只更新当前高亮的那一行，不影响其它订阅。</summary>
        private async void UpdateSubscription_Click(object sender, RoutedEventArgs e)
        {
            var target = ProfileItemListView.SelectedItem as ProfileItem;
            if (target == null)
                return;
            UpdateProfileButton.IsEnabled = false;
            try
            {
                await Task.Run(() => target.Update());
            }
            finally
            {
                UpdateProfileButton.IsEnabled = true;
            }
            UpdateTargetState();
        }

        private void CopySubscription_Click(object sender, RoutedEventArgs e)
        {
            var target = ProfileItemListView.SelectedItem as ProfileItem;
            if (target == null || string.IsNullOrWhiteSpace(target.Url))
                return;
            try
            {
                var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                package.SetText(target.Url);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            }
            catch
            {
            }
        }

        /// <summary>
        /// 语言切换后刷新由代码写入的文案（按钮文案与操作对象行）。
        /// </summary>
        public void RefreshLocalizedText()
        {
            UpdateTargetState();
        }
    }
}