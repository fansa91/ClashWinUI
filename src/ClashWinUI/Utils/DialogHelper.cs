using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace ClashDotNetFramework.Utils
{
    public static class DialogHelper
    {
        private static XamlRoot Root => (Global.MainWindow?.Content as FrameworkElement)?.XamlRoot;

        public static async Task ShowMessageAsync(string title, string message)
        {
            if (Root == null)
                return;
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = Root
            };
            await dialog.ShowAsync();
        }

        public static async Task<bool> ConfirmAsync(string title, string message)
        {
            if (Root == null)
                return false;
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = "Yes",
                CloseButtonText = "No",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Root
            };
            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }
    }
}