using ClashDotNetFramework.Models.Items;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Threading.Tasks;

namespace ClashDotNetFramework.Dialogs
{
    public static class DuplicateProfileDialog
    {
        public static async Task<string> ShowAsync()
        {
            var root = (Global.MainWindow?.Content as FrameworkElement)?.XamlRoot;
            if (root == null)
                return null;

            var input = new TextBox { PlaceholderText = "Input a new file name" };
            var dialog = new ContentDialog
            {
                Title = "Duplicate Profile",
                Content = input,
                PrimaryButtonText = "Ok",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = root
            };
            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? input.Text : null;
        }
    }

    public static class ProfileSettingsDialog
    {
        public static async Task ShowAsync(ProfileItem profile)
        {
            var root = (Global.MainWindow?.Content as FrameworkElement)?.XamlRoot;
            if (root == null)
                return;

            var name = new TextBox { Text = profile.Name, Header = "Name" };
            var url = new TextBox { Text = profile.Url, Header = "URL" };
            var interval = new TextBox { Text = profile.UpdateInterval.ToString(), Header = "Update Interval (hour)" };

            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(name);
            panel.Children.Add(url);
            panel.Children.Add(interval);

            var dialog = new ContentDialog
            {
                Title = "Edit Profile Information",
                Content = panel,
                PrimaryButtonText = "Ok",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = root
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                profile.Name = name.Text;
                profile.Url = url.Text;
                if (int.TryParse(interval.Text, out int value))
                    profile.UpdateInterval = value;
            }
        }
    }
}