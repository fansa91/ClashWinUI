using ClashDotNetFramework.Models.Items;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Threading.Tasks;

namespace ClashDotNetFramework.Dialogs
{
    public static class ConnectionInfoDialog
    {
        public static async Task ShowAsync(ConnectionItem item)
        {
            var root = (Global.MainWindow?.Content as FrameworkElement)?.XamlRoot;
            if (root == null)
                return;

            var panel = new StackPanel { Spacing = 6 };
            AddRow(panel, "UUID", item.UUID);
            AddRow(panel, "Network", item.Network);
            AddRow(panel, "Type", item.Type);
            AddRow(panel, "Host", item.Host);
            AddRow(panel, "Source", item.Source);
            AddRow(panel, "Destination", item.Destination);
            AddRow(panel, "Chains", item.Chains != null ? string.Join(" -> ", item.Chains) : "");
            AddRow(panel, "Rule", item.Rule);
            AddRow(panel, "Time", item.Time);
            AddRow(panel, "Upload", item.Upload);
            AddRow(panel, "Download", item.Download);

            var dialog = new ContentDialog
            {
                Title = "Connection Info",
                Content = new ScrollViewer { Content = panel, MaxHeight = 400 },
                CloseButtonText = "OK",
                XamlRoot = root
            };
            await dialog.ShowAsync();
        }

        private static void AddRow(StackPanel panel, string label, string value)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var labelBlock = new TextBlock { Text = label, Opacity = 0.6 };
            var valueBlock = new TextBlock { Text = value ?? "", TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(valueBlock, 1);
            row.Children.Add(labelBlock);
            row.Children.Add(valueBlock);
            panel.Children.Add(row);
        }
    }
}