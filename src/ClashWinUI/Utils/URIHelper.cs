using ClashDotNetFramework.Models.Enums;
using ClashDotNetFramework.Models.Items;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ClashDotNetFramework.Utils
{
    public static class URIHelper
    {
        public static void ProcessURI(string data)
        {
            try
            {
                Uri uri = new Uri(data);
                if (uri.Scheme != "clash")
                    return;

                if (uri.Authority != "install-config")
                    return;

                string url = GetQueryValue(uri.Query, "url");
                if (string.IsNullOrWhiteSpace(url))
                    return;

                Task.Run(() =>
                {
                    Uri profileUri = new Uri(Uri.UnescapeDataString(url));
                    ProfileItem profileItem = new ProfileItem
                    {
                        Type = ProfileType.Remote,
                        IsRemote = true,
                        Host = profileUri.Host,
                        Url = profileUri.AbsoluteUri,
                        UpdateInterval = 0,
                        LastUpdate = DateTime.Now
                    };
                    if (profileItem.Update())
                    {
                        Global.RunOnUI(() => Global.Settings.Profile.Add(profileItem));
                        Configuration.Save();
                    }
                });
            }
            catch
            {
                // Probably not URI at all
            }
        }

        private static string GetQueryValue(string query, string key)
        {
            if (string.IsNullOrEmpty(query))
                return null;
            query = query.TrimStart('?');
            foreach (var pair in query.Split('&'))
            {
                var idx = pair.IndexOf('=');
                if (idx < 0)
                    continue;
                var k = Uri.UnescapeDataString(pair.Substring(0, idx));
                if (k == key)
                    return Uri.UnescapeDataString(pair.Substring(idx + 1));
            }
            return null;
        }
    }
}