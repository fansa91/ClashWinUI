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

        private void ProfilesPage_Loaded(object sender, RoutedEventArgs e)
        {
            ProfileItemListView.ItemsSource = Global.Settings.Profile;
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
                if (!profile.IsSelected)
                {
                    await Global.clashClient.ReloadClashConfig(Clash.SDK.Models.Enums.ConfigType.Path, false, profile.FileName);
                    foreach (var p in Global.Settings.Profile)
                        p.IsSelected = false;
                    profile.IsSelected = true;
                    Global.Refresh = true;
                    Configuration.Save();
                }
            }
            catch
            {
                await DialogHelper.ShowMessageAsync("Clash .NET Framework", $"Load config \"{profile.Name}\" error");
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
            bool confirm = await DialogHelper.ConfirmAsync("Clash .NET Framework", $"Are you sure to delete \"{profile.Name}\"?");
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
    }
}