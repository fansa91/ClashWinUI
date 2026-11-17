using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;

namespace ClashDotNetFramework.Utils
{
    public static class URISchemeHelper
    {
        private const string Key = "clash";

        private static string ExePath =>
            Assembly.GetEntryAssembly()?.Location is { Length: > 0 } loc
                ? loc
                : Process.GetCurrentProcess().MainModule.FileName;

        public static bool Check()
        {
            using var k = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{Key}");
            return k != null;
        }

        public static void Set()
        {
            using var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Key}");
            k.SetValue("", "URL:ClashWinUI");
            k.SetValue("URL Protocol", "");
            using var cmd = k.CreateSubKey(@"shell\open\command");
            cmd.SetValue("", $"\"{ExePath}\" \"%1\"");
        }

        public static void Delete()
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{Key}", false);
        }
    }
}