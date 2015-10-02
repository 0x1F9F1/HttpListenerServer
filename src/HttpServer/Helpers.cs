using System;
using System.Diagnostics;
using System.IO;
using System.Web;

namespace HttpListenerServer
{
    partial class Handler
    {
        private static string ToUrl(string local, string root)
        {
            local = local.Trim();
            root = root.Trim();

            return local.Length < root.Length
                ? string.Empty
                : (!local.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : (local.Equals(root, StringComparison.OrdinalIgnoreCase)
                        ? string.Empty
                        : HttpUtility.HtmlEncode(local.Substring(root.Length))));
        }

        private static string ToLocal(string url, string root)
        {
            url = HttpUtility.HtmlDecode(url).Trim().TrimStart('/', '\\').Replace('/', '\\');
            return Path.Combine(root, url);
        }

        private static string GetParent(DirectoryInfo directory, string root)
        {
            var path = directory.FullName.Trim();
            root = root.Trim();

            if (path.Equals(root, StringComparison.InvariantCultureIgnoreCase)) return path;
            return directory.Parent?.FullName ?? path;
        }

        private static string Replace(string input, params object[] parameters)
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                input = input.Replace($"%{i}%", parameters[i].ToString());
            }
            return input;
        }

        private static void Log(object data)
        {
            Debug.WriteLine($"{DateTime.Now:R} | [Handler] {data}");
            Console.WriteLine($"{DateTime.Now:R} | [Handler] {data}");
        }
    }
}